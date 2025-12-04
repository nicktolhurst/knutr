namespace Knutr.Plugins.GitLabPipeline.Workflows;

using Knutr.Abstractions.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Orchestrates a full deployment workflow including:
/// 1. Check if build exists
/// 2. Build if necessary (and wait for completion)
/// 3. Check environment availability
/// 4. Prompt user if environment is unavailable
/// 5. Deploy to target environment
/// 6. Monitor deployment progress
/// 7. Send deployment summary
/// </summary>
public sealed class DeployWorkflow : IWorkflow
{
    public string Name => "gitlab:deploy";

    private readonly IGitLabClient _client;
    private readonly GitLabOptions _options;
    private readonly IEnvironmentService _envService;
    private readonly ILogger<DeployWorkflow> _log;

    public DeployWorkflow(
        IGitLabClient client,
        IOptions<GitLabOptions> options,
        IEnvironmentService envService,
        ILogger<DeployWorkflow> log)
    {
        _client = client;
        _options = options.Value;
        _envService = envService;
        _log = log;
    }

    public async Task<WorkflowResult> ExecuteAsync(IWorkflowContext context)
    {
        // Extract initial parameters
        var branch = context.Get<string>("branch") ?? context.Get<string>("ref");
        var environment = context.Get<string>("environment");

        if (string.IsNullOrEmpty(branch))
        {
            return WorkflowResult.Fail("Missing branch/ref parameter");
        }

        if (string.IsNullOrEmpty(environment))
        {
            return WorkflowResult.Fail("Missing environment parameter");
        }

        var project = ResolveProject(environment);
        if (project is null)
        {
            return WorkflowResult.Fail($"Unknown environment: `{environment}`");
        }

        context.Set("project", project);

        try
        {
            // ─────────────────────────────────────────────────────────────
            // Step 1: Check for existing build/pipeline
            // ─────────────────────────────────────────────────────────────
            await context.SendAsync($":mag: Checking build status for `{branch}`...");

            var existingPipeline = await _client.GetLatestPipelineAsync(project, branch);
            var needsBuild = existingPipeline is null
                || existingPipeline.Status == "failed"
                || existingPipeline.Status == "canceled";

            if (needsBuild)
            {
                // ─────────────────────────────────────────────────────────
                // Step 2: Trigger build
                // ─────────────────────────────────────────────────────────
                await context.SendAsync(existingPipeline is null
                    ? $":construction: No build found for `{branch}`. Starting build..."
                    : $":construction: Previous build {existingPipeline.Status}. Starting fresh build...");

                var buildResult = await _client.TriggerPipelineAsync(project, branch);
                if (!buildResult.IsSuccess)
                {
                    return WorkflowResult.Fail($"Failed to start build: {buildResult.ErrorMessage}");
                }

                var pipelineId = buildResult.Pipeline!.Id;
                context.Set("build_pipeline_id", pipelineId);

                await context.SendAsync(
                    $":rocket: Build `#{pipelineId}` started.\n" +
                    $"• URL: {buildResult.Pipeline.WebUrl}");

                // ─────────────────────────────────────────────────────────
                // Step 3: Wait for build completion
                // ─────────────────────────────────────────────────────────
                var buildCompleted = await context.WaitUntilAsync(
                    async () =>
                    {
                        var pipeline = await _client.GetPipelineAsync(project, pipelineId);
                        if (pipeline is null) return false;

                        context.Set("build_status", pipeline.Status);

                        return pipeline.Status is "success" or "failed" or "canceled";
                    },
                    interval: TimeSpan.FromSeconds(30),
                    timeout: TimeSpan.FromMinutes(30),
                    progressMessage: ":hourglass_flowing_sand: Waiting for build to complete...");

                if (!buildCompleted)
                {
                    return WorkflowResult.Fail("Build timed out after 30 minutes");
                }

                var buildStatus = context.Get<string>("build_status");
                if (buildStatus != "success")
                {
                    var retry = await context.ConfirmAsync(
                        $":x: Build failed with status `{buildStatus}`. Would you like to retry?");

                    if (retry)
                    {
                        // Recursive retry by restarting workflow
                        await context.SendAsync(":arrows_counterclockwise: Retrying build...");
                        return await ExecuteAsync(context);
                    }

                    return WorkflowResult.Fail($"Build failed: {buildStatus}");
                }

                await context.SendAsync(":white_check_mark: Build completed successfully!");
            }
            else
            {
                await context.SendAsync(
                    $":white_check_mark: Found existing successful build `#{existingPipeline!.Id}`\n" +
                    $"• Status: `{existingPipeline.Status}`\n" +
                    $"• SHA: `{existingPipeline.Sha[..8]}`");
            }

            // ─────────────────────────────────────────────────────────────
            // Step 4: Check environment availability
            // ─────────────────────────────────────────────────────────────
            await context.SendAsync($":earth_americas: Checking environment `{environment}`...");

            var envStatus = await _envService.CheckAvailabilityAsync(environment, context.UserId);

            if (!envStatus.IsAvailable)
            {
                // ─────────────────────────────────────────────────────────
                // Step 5: Prompt for alternative environment
                // ─────────────────────────────────────────────────────────
                var availableEnvs = await _envService.GetAvailableEnvironmentsAsync(context.UserId);

                if (availableEnvs.Count == 0)
                {
                    return WorkflowResult.Fail(
                        $"Environment `{environment}` is claimed by <@{envStatus.ClaimedBy}> and no alternatives are available.");
                }

                var choice = await context.PromptAsync(
                    $":no_entry: Environment `{environment}` is claimed by <@{envStatus.ClaimedBy}>.\n\n" +
                    "Choose an alternative environment:",
                    availableEnvs);

                environment = choice;
                context.Set("environment", environment);
                project = ResolveProject(environment) ?? project;
                context.Set("project", project);

                await context.SendAsync($":white_check_mark: Switched to environment `{environment}`");
            }
            else
            {
                await context.SendAsync($":white_check_mark: Environment `{environment}` is available");
            }

            // ─────────────────────────────────────────────────────────────
            // Step 6: Deploy
            // ─────────────────────────────────────────────────────────────
            await context.SendAsync($":rocket: Deploying `{branch}` to `{environment}`...");

            var envConfig = GetEnvironmentConfig(environment);
            var deployResult = await _client.TriggerPipelineAsync(project, branch, envConfig?.Variables);

            if (!deployResult.IsSuccess)
            {
                return WorkflowResult.Fail($"Failed to trigger deployment: {deployResult.ErrorMessage}");
            }

            var deployPipelineId = deployResult.Pipeline!.Id;
            context.Set("deploy_pipeline_id", deployPipelineId);
            context.Set("deploy_url", deployResult.Pipeline.WebUrl);

            await context.SendAsync(
                $":satellite: Deployment pipeline `#{deployPipelineId}` started.\n" +
                $"• URL: {deployResult.Pipeline.WebUrl}");

            // ─────────────────────────────────────────────────────────────
            // Step 7: Monitor deployment
            // ─────────────────────────────────────────────────────────────
            var deployCompleted = await context.WaitUntilAsync(
                async () =>
                {
                    var pipeline = await _client.GetPipelineAsync(project, deployPipelineId);
                    if (pipeline is null) return false;

                    context.Set("deploy_status", pipeline.Status);
                    return pipeline.Status is "success" or "failed" or "canceled";
                },
                interval: TimeSpan.FromSeconds(15),
                timeout: TimeSpan.FromMinutes(15),
                progressMessage: ":hourglass_flowing_sand: Monitoring deployment...");

            var finalStatus = context.Get<string>("deploy_status") ?? "unknown";

            // ─────────────────────────────────────────────────────────────
            // Step 8: Deployment summary
            // ─────────────────────────────────────────────────────────────
            var statusEmoji = finalStatus switch
            {
                "success" => ":white_check_mark:",
                "failed" => ":x:",
                "canceled" => ":stop_sign:",
                _ => ":grey_question:"
            };

            var summary = $"""
                {statusEmoji} *Deployment Summary*

                • *Branch:* `{branch}`
                • *Environment:* `{environment}`
                • *Pipeline:* `#{deployPipelineId}`
                • *Status:* `{finalStatus}`
                • *URL:* {deployResult.Pipeline.WebUrl}

                """;

            if (finalStatus == "success")
            {
                summary += ":tada: Deployment completed successfully!";
            }
            else if (!deployCompleted)
            {
                summary += ":warning: Deployment is still running. Check the pipeline URL for progress.";
            }
            else
            {
                summary += ":warning: Deployment did not succeed. Check the pipeline for details.";
            }

            await context.SendAsync(summary);

            return finalStatus == "success"
                ? WorkflowResult.Ok("Deployment completed successfully")
                : WorkflowResult.Ok($"Deployment finished with status: {finalStatus}");
        }
        catch (OperationCanceledException)
        {
            return WorkflowResult.Cancelled();
        }
        catch (TimeoutException ex)
        {
            return WorkflowResult.Fail(ex.Message);
        }
    }

    private string? ResolveProject(string environment)
    {
        if (_options.Environments.TryGetValue(environment, out var config))
        {
            return config.Project ?? _options.DefaultProject;
        }

        var match = _options.Environments
            .FirstOrDefault(kv => kv.Key.Equals(environment, StringComparison.OrdinalIgnoreCase));

        if (match.Value is not null)
        {
            return match.Value.Project ?? _options.DefaultProject;
        }

        return !string.IsNullOrEmpty(_options.DefaultProject) ? _options.DefaultProject : null;
    }

    private EnvironmentConfig? GetEnvironmentConfig(string environment)
    {
        if (_options.Environments.TryGetValue(environment, out var config))
            return config;

        return _options.Environments
            .FirstOrDefault(kv => kv.Key.Equals(environment, StringComparison.OrdinalIgnoreCase))
            .Value;
    }
}
