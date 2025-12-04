namespace Knutr.Core.Workflows;

using System.Collections.Concurrent;
using Knutr.Abstractions.Events;
using Knutr.Abstractions.Workflows;
using Knutr.Core.Replies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Engine for executing and managing workflows.
/// </summary>
public sealed class WorkflowEngine : IWorkflowEngine
{
    private readonly IServiceProvider _services;
    private readonly IReplyService _replyService;
    private readonly ILogger<WorkflowEngine> _log;

    // Active workflow instances indexed by workflow ID
    private readonly ConcurrentDictionary<string, WorkflowContext> _activeWorkflows = new();

    // Index for finding workflows waiting for input in a channel/thread
    private readonly ConcurrentDictionary<string, string> _waitingByLocation = new();

    public WorkflowEngine(
        IServiceProvider services,
        IReplyService replyService,
        ILogger<WorkflowEngine> log)
    {
        _services = services;
        _replyService = replyService;
        _log = log;
    }

    public async Task<string> StartAsync<TWorkflow>(
        CommandContext commandContext,
        IReadOnlyDictionary<string, object>? initialState = null)
        where TWorkflow : IWorkflow
    {
        var workflow = _services.GetRequiredService<TWorkflow>();
        return await StartInternalAsync(workflow, commandContext, initialState);
    }

    public async Task<string> StartAsync(
        string workflowName,
        CommandContext commandContext,
        IReadOnlyDictionary<string, object>? initialState = null)
    {
        var workflows = _services.GetServices<IWorkflow>();
        var workflow = workflows.FirstOrDefault(w => w.Name.Equals(workflowName, StringComparison.OrdinalIgnoreCase));

        if (workflow is null)
        {
            throw new InvalidOperationException($"Workflow '{workflowName}' not found");
        }

        return await StartInternalAsync(workflow, commandContext, initialState);
    }

    private async Task<string> StartInternalAsync(
        IWorkflow workflow,
        CommandContext commandContext,
        IReadOnlyDictionary<string, object>? initialState)
    {
        var workflowId = GenerateWorkflowId();

        var context = new WorkflowContext(
            workflowId,
            workflow.Name,
            commandContext,
            _replyService,
            initialState);

        _activeWorkflows[workflowId] = context;

        _log.LogInformation("Starting workflow {WorkflowName} ({WorkflowId}) for user {UserId}",
            workflow.Name, workflowId, commandContext.UserId);

        // Run workflow in background
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await workflow.ExecuteAsync(context);

                context.Status = result.Success ? WorkflowStatus.Completed : WorkflowStatus.Failed;
                context.CompletedAt = DateTime.UtcNow;

                _log.LogInformation("Workflow {WorkflowId} completed with status {Status}: {Message}",
                    workflowId, context.Status, result.Message);

                if (!result.Success && !string.IsNullOrEmpty(result.Message))
                {
                    await context.SendAsync($":x: {result.Message}");
                }
            }
            catch (OperationCanceledException)
            {
                context.Status = WorkflowStatus.Cancelled;
                context.CompletedAt = DateTime.UtcNow;
                _log.LogInformation("Workflow {WorkflowId} was cancelled", workflowId);
            }
            catch (Exception ex)
            {
                context.Status = WorkflowStatus.Failed;
                context.CompletedAt = DateTime.UtcNow;
                _log.LogError(ex, "Workflow {WorkflowId} failed with exception", workflowId);
                await context.SendAsync($":x: Workflow failed: {ex.Message}");
            }
            finally
            {
                // Cleanup after some delay to allow status queries
                _ = Task.Delay(TimeSpan.FromMinutes(5)).ContinueWith(_ =>
                {
                    _activeWorkflows.TryRemove(workflowId, out _);
                    RemoveFromWaitingIndex(workflowId);
                });
            }
        });

        return workflowId;
    }

    public Task<bool> ResumeWithInputAsync(string workflowId, string input)
    {
        if (!_activeWorkflows.TryGetValue(workflowId, out var context))
        {
            _log.LogWarning("Attempted to resume non-existent workflow {WorkflowId}", workflowId);
            return Task.FromResult(false);
        }

        if (context.Status != WorkflowStatus.WaitingForInput)
        {
            _log.LogWarning("Attempted to resume workflow {WorkflowId} but it's not waiting for input (status: {Status})",
                workflowId, context.Status);
            return Task.FromResult(false);
        }

        var result = context.TryProvideInput(input);
        if (result)
        {
            RemoveFromWaitingIndex(workflowId);
            _log.LogDebug("Resumed workflow {WorkflowId} with input", workflowId);
        }

        return Task.FromResult(result);
    }

    public Task<bool> CancelAsync(string workflowId, string? reason = null)
    {
        if (!_activeWorkflows.TryGetValue(workflowId, out var context))
        {
            return Task.FromResult(false);
        }

        context.Cancel(reason);
        RemoveFromWaitingIndex(workflowId);

        _log.LogInformation("Cancelled workflow {WorkflowId}: {Reason}", workflowId, reason);
        return Task.FromResult(true);
    }

    public Task<WorkflowInfo?> GetWorkflowAsync(string workflowId)
    {
        if (!_activeWorkflows.TryGetValue(workflowId, out var context))
        {
            return Task.FromResult<WorkflowInfo?>(null);
        }

        return Task.FromResult<WorkflowInfo?>(ToWorkflowInfo(context));
    }

    public Task<IReadOnlyList<WorkflowInfo>> GetActiveWorkflowsAsync(string userId)
    {
        var workflows = _activeWorkflows.Values
            .Where(c => c.UserId == userId && c.Status is WorkflowStatus.Running
                or WorkflowStatus.WaitingForInput
                or WorkflowStatus.WaitingForEvent)
            .Select(ToWorkflowInfo)
            .ToList();

        return Task.FromResult<IReadOnlyList<WorkflowInfo>>(workflows);
    }

    public Task<WorkflowInfo?> FindWaitingWorkflowAsync(string channelId, string? threadTs)
    {
        var locationKey = BuildLocationKey(channelId, threadTs);

        if (_waitingByLocation.TryGetValue(locationKey, out var workflowId) &&
            _activeWorkflows.TryGetValue(workflowId, out var context) &&
            context.Status == WorkflowStatus.WaitingForInput)
        {
            return Task.FromResult<WorkflowInfo?>(ToWorkflowInfo(context));
        }

        // Also try the base channel if we're in a thread
        if (threadTs != null)
        {
            var channelKey = BuildLocationKey(channelId, null);
            if (_waitingByLocation.TryGetValue(channelKey, out workflowId) &&
                _activeWorkflows.TryGetValue(workflowId, out context) &&
                context.Status == WorkflowStatus.WaitingForInput)
            {
                return Task.FromResult<WorkflowInfo?>(ToWorkflowInfo(context));
            }
        }

        return Task.FromResult<WorkflowInfo?>(null);
    }

    /// <summary>
    /// Called when a workflow starts waiting for input to index its location.
    /// </summary>
    internal void RegisterWaiting(string workflowId, string channelId, string? threadTs)
    {
        var locationKey = BuildLocationKey(channelId, threadTs);
        _waitingByLocation[locationKey] = workflowId;
    }

    private void RemoveFromWaitingIndex(string workflowId)
    {
        var keysToRemove = _waitingByLocation
            .Where(kv => kv.Value == workflowId)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _waitingByLocation.TryRemove(key, out _);
        }
    }

    private static string BuildLocationKey(string channelId, string? threadTs)
        => threadTs != null ? $"{channelId}:{threadTs}" : channelId;

    private static WorkflowInfo ToWorkflowInfo(WorkflowContext context)
        => new(
            context.WorkflowId,
            context.WorkflowName,
            context.Status,
            context.UserId,
            context.ChannelId,
            context.ThreadTs,
            context.StartedAt,
            context.CompletedAt,
            context.GetState());

    private static string GenerateWorkflowId()
        => $"wf_{Guid.NewGuid():N}"[..16];
}
