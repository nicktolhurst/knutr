namespace Knutr.Plugins.GitLabPipeline.Workflows;

using Knutr.Abstractions.Workflows;
using Knutr.Plugins.GitLabPipeline.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Orchestrates a full deployment workflow with elegant, minimal messaging.
/// Uses a single progress message that updates in-place as the deployment progresses.
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
        var startTime = DateTime.UtcNow;

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

        // Create the message builder for elegant status updates
        var msg = new DeploymentMessageBuilder(branch, environment);
        string? progressTs = null;

        try
        {
            // ─────────────────────────────────────────────────────────────
            // Main channel message (the status dashboard that updates in-place)
            // This also establishes the thread for prompts and detailed logs
            // ─────────────────────────────────────────────────────────────
            msg.AddStep("Checking build", StepState.InProgress);
            msg.AddStep("Checking environment", StepState.Pending);
            msg.AddStep("Running pipeline", StepState.Pending);
            progressTs = await context.SendBlocksAsync(msg.BuildText(), msg.BuildBlocks());

            var existingPipeline = await _client.GetLatestPipelineAsync(project, branch);
            var needsBuild = existingPipeline is null
                || existingPipeline.Status == "failed"
                || existingPipeline.Status == "canceled";

            if (needsBuild)
            {
                // ─────────────────────────────────────────────────────────
                // Step 2: Trigger build
                // ─────────────────────────────────────────────────────────
                msg.AddStep("Checking build", StepState.InProgress, "triggering...");
                await UpdateProgress(context, progressTs, msg);

                var buildResult = await _client.TriggerPipelineAsync(project, branch);
                if (!buildResult.IsSuccess)
                {
                    msg.AddStep("Checking build", StepState.Failed, "trigger failed");
                    msg.MarkFailed(buildResult.ErrorMessage);
                    await UpdateProgress(context, progressTs, msg);
                    return WorkflowResult.Fail($"Failed to start build: {buildResult.ErrorMessage}");
                }

                var pipelineId = buildResult.Pipeline!.Id;
                context.Set("build_pipeline_id", pipelineId);
                msg.SetPipelineUrl(buildResult.Pipeline.WebUrl);

                // ─────────────────────────────────────────────────────────
                // Step 3: Wait for build completion
                // ─────────────────────────────────────────────────────────
                msg.AddStep("Checking build", StepState.InProgress, $"#{pipelineId} running");
                await UpdateProgress(context, progressTs, msg);

                var buildCompleted = await WaitForPipelineAsync(
                    context, project, pipelineId, "build_status",
                    TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(30));

                var buildStatus = context.Get<string>("build_status");
                if (!buildCompleted || buildStatus != "success")
                {
                    msg.AddStep("Checking build", StepState.Failed, $"#{pipelineId} {buildStatus}");
                    msg.MarkFailed($"Build {buildStatus}");
                    await UpdateProgress(context, progressTs, msg);

                    if (buildStatus == "failed")
                    {
                        var retry = await context.ConfirmAsync(
                            $"Build failed. Would you like to retry?");

                        if (retry)
                        {
                            return await ExecuteAsync(context);
                        }
                    }

                    return WorkflowResult.Fail($"Build failed: {buildStatus}");
                }

                msg.AddStep("Checking build", StepState.Success, $"#{pipelineId}");
            }
            else
            {
                msg.AddStep("Checking build", StepState.Success, $"#{existingPipeline!.Id}");
                msg.SetPipelineUrl(existingPipeline.WebUrl);
            }

            // ─────────────────────────────────────────────────────────────
            // Step 4: Check environment availability
            // ─────────────────────────────────────────────────────────────
            msg.AddStep("Checking environment", StepState.InProgress);
            await UpdateProgress(context, progressTs, msg);

            var envStatus = await _envService.CheckAvailabilityAsync(environment, context.UserId);

            if (!envStatus.IsAvailable)
            {
                // ─────────────────────────────────────────────────────────
                // Step 5: Prompt for alternative environment
                // ─────────────────────────────────────────────────────────
                var availableEnvs = await _envService.GetAvailableEnvironmentsAsync(context.UserId);

                if (availableEnvs.Count == 0)
                {
                    msg.AddStep("Checking environment", StepState.Failed, $"claimed by <@{envStatus.ClaimedBy}>");
                    msg.MarkFailed("No available environments");
                    await UpdateProgress(context, progressTs, msg);
                    return WorkflowResult.Fail(
                        $"Environment `{environment}` is claimed by <@{envStatus.ClaimedBy}> and no alternatives are available.");
                }

                var choice = await context.PromptAsync(
                    $"Environment `{environment}` is claimed by <@{envStatus.ClaimedBy}>. Choose alternative:",
                    availableEnvs);

                environment = choice;
                context.Set("environment", environment);
                project = ResolveProject(environment) ?? project;
                context.Set("project", project);

                // Recreate message builder with new environment
                msg = new DeploymentMessageBuilder(branch, environment);
                msg.AddStep("Checking build", StepState.Success);
                msg.AddStep("Checking environment", StepState.Success, environment);
            }
            else
            {
                msg.AddStep("Checking environment", StepState.Success);
            }

            // ─────────────────────────────────────────────────────────────
            // Step 6: Deploy
            // ─────────────────────────────────────────────────────────────
            msg.AddStep("Running pipeline", StepState.InProgress, "triggering...");
            await UpdateProgress(context, progressTs, msg);

            var envConfig = GetEnvironmentConfig(environment);
            var deployResult = await _client.TriggerPipelineAsync(project, branch, envConfig?.Variables);

            if (!deployResult.IsSuccess)
            {
                msg.AddStep("Running pipeline", StepState.Failed, "trigger failed");
                msg.MarkFailed(deployResult.ErrorMessage);
                await UpdateProgress(context, progressTs, msg);
                return WorkflowResult.Fail($"Failed to trigger deployment: {deployResult.ErrorMessage}");
            }

            var deployPipelineId = deployResult.Pipeline!.Id;
            context.Set("deploy_pipeline_id", deployPipelineId);
            context.Set("deploy_url", deployResult.Pipeline.WebUrl);
            msg.SetPipelineUrl(deployResult.Pipeline.WebUrl);

            // ─────────────────────────────────────────────────────────────
            // Step 7: Monitor deployment
            // ─────────────────────────────────────────────────────────────
            msg.AddStep("Running pipeline", StepState.InProgress, $"#{deployPipelineId}");
            await UpdateProgress(context, progressTs, msg);

            var deployCompleted = await WaitForPipelineAsync(
                context, project, deployPipelineId, "deploy_status",
                TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(15));

            var finalStatus = context.Get<string>("deploy_status") ?? "unknown";
            var duration = DateTime.UtcNow - startTime;
            msg.SetDuration(duration);

            // ─────────────────────────────────────────────────────────────
            // Final status update
            // ─────────────────────────────────────────────────────────────
            if (finalStatus == "success")
            {
                msg.AddStep("Running pipeline", StepState.Success, $"#{deployPipelineId}");
                msg.MarkSuccess();
            }
            else if (!deployCompleted)
            {
                msg.AddStep("Running pipeline", StepState.InProgress, $"#{deployPipelineId} (timed out)");
            }
            else
            {
                msg.AddStep("Running pipeline", StepState.Failed, $"#{deployPipelineId} {finalStatus}");
                msg.MarkFailed($"Pipeline {finalStatus}");
            }

            await UpdateProgress(context, progressTs, msg);

            return finalStatus == "success"
                ? WorkflowResult.Ok("Deployment completed successfully")
                : WorkflowResult.Ok($"Deployment finished with status: {finalStatus}");
        }
        catch (OperationCanceledException)
        {
            msg.MarkCancelled();
            if (progressTs != null)
            {
                await UpdateProgress(context, progressTs, msg);
            }
            return WorkflowResult.Cancelled();
        }
        catch (TimeoutException ex)
        {
            msg.MarkFailed(ex.Message);
            if (progressTs != null)
            {
                await UpdateProgress(context, progressTs, msg);
            }
            return WorkflowResult.Fail(ex.Message);
        }
    }

    private static async Task UpdateProgress(IWorkflowContext context, string? messageTs, DeploymentMessageBuilder msg)
    {
        if (messageTs != null)
        {
            await context.UpdateBlocksAsync(messageTs, msg.BuildText(), msg.BuildBlocks());
        }
    }

    private async Task<bool> WaitForPipelineAsync(
        IWorkflowContext context,
        string project,
        int pipelineId,
        string statusKey,
        TimeSpan interval,
        TimeSpan timeout)
    {
        return await context.WaitUntilAsync(
            async () =>
            {
                var pipeline = await _client.GetPipelineAsync(project, pipelineId);
                if (pipeline is null) return false;

                context.Set(statusKey, pipeline.Status);
                return pipeline.Status is "success" or "failed" or "canceled";
            },
            interval,
            timeout,
            progressMessage: null);  // No separate progress message - we update in-place
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
