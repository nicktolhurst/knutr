namespace Knutr.Core.Workflows;

using System.Net.Http.Json;
using Knutr.Abstractions.Events;
using Knutr.Abstractions.Workflows;
using Microsoft.Extensions.Logging;

/// <summary>
/// Handles workflow button interactions - routing button clicks to waiting workflows.
/// </summary>
public sealed class WorkflowButtonService : IWorkflowButtonService
{
    private readonly IWorkflowEngine _workflowEngine;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<WorkflowButtonService> _log;

    private const string WorkflowButtonPrefix = "wf_";

    public WorkflowButtonService(
        IWorkflowEngine workflowEngine,
        IHttpClientFactory httpFactory,
        ILogger<WorkflowButtonService> log)
    {
        _workflowEngine = workflowEngine;
        _httpFactory = httpFactory;
        _log = log;
    }

    public string GenerateActionId(string workflowId, string action)
    {
        // Format: wf_{workflowId}_{action}
        return $"{WorkflowButtonPrefix}{workflowId}_{action}";
    }

    public bool TryGetWorkflowAction(string actionId, out string? workflowId, out string? action)
    {
        workflowId = null;
        action = null;

        if (!actionId.StartsWith(WorkflowButtonPrefix))
            return false;

        // Format: wf_{workflowIdWithoutPrefix}_{action}
        // Example: wf_f226d8a9c3514_release
        // The stored workflow ID is "wf_f226d8a9c3514", so we need to reconstruct it
        var remainder = actionId[WorkflowButtonPrefix.Length..]; // "f226d8a9c3514_release"
        var underscoreIndex = remainder.IndexOf('_');

        if (underscoreIndex <= 0)
            return false;

        // Add the "wf_" prefix back to get the actual workflow ID
        workflowId = WorkflowButtonPrefix + remainder[..underscoreIndex]; // "wf_f226d8a9c3514"
        action = remainder[(underscoreIndex + 1)..]; // "release"

        return !string.IsNullOrEmpty(workflowId) && !string.IsNullOrEmpty(action);
    }

    public async Task HandleButtonClickAsync(BlockActionContext ctx, string workflowId, string action, CancellationToken ct = default)
    {
        _log.LogInformation("Button click: workflow={WorkflowId}, action={Action}, user={UserId}",
            workflowId, action, ctx.UserId);

        // Resume the workflow with the button action as input, passing the response URL
        // so the workflow can update the button message
        var resumed = await _workflowEngine.ResumeWithInputAsync(workflowId, action, ctx.ResponseUrl);

        if (!resumed)
        {
            _log.LogWarning("Failed to resume workflow {WorkflowId} - may have expired or completed", workflowId);

            // Update the button message to show it's expired
            if (!string.IsNullOrEmpty(ctx.ResponseUrl))
            {
                await UpdateButtonMessageAsync(ctx.ResponseUrl, ":clock3: This interaction has expired.", ct: ct);
            }
        }
    }

    public async Task UpdateButtonMessageAsync(string responseUrl, string text, object[]? blocks = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(responseUrl))
            return;

        using var client = _httpFactory.CreateClient();

        var payload = new Dictionary<string, object>
        {
            ["text"] = text,
            ["replace_original"] = true
        };

        if (blocks != null)
        {
            payload["blocks"] = blocks;
        }

        try
        {
            var response = await client.PostAsJsonAsync(responseUrl, payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _log.LogWarning("Failed to update button message: {Status} {Body}", response.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Error updating button message");
        }
    }
}
