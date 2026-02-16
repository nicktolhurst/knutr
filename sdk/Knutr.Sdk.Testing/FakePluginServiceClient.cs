namespace Knutr.Sdk.Testing;

/// <summary>
/// Test double for <see cref="IPluginServiceClient"/> that returns canned or dynamic responses.
/// </summary>
public sealed class FakePluginServiceClient : IPluginServiceClient
{
    private readonly Dictionary<string, Func<PluginExecuteRequest, PluginExecuteResponse>> _handlers = new();
    private readonly List<(string ServiceName, PluginExecuteRequest Request)> _calls = [];

    /// <summary>
    /// All calls made through this client, for assertion purposes.
    /// </summary>
    public IReadOnlyList<(string ServiceName, PluginExecuteRequest Request)> Calls => _calls;

    /// <summary>
    /// Register a canned response for a given service name.
    /// </summary>
    public void Setup(string serviceName, PluginExecuteResponse response)
        => _handlers[serviceName] = _ => response;

    /// <summary>
    /// Register a dynamic handler for a given service name.
    /// </summary>
    public void Setup(string serviceName, Func<PluginExecuteRequest, PluginExecuteResponse> handler)
        => _handlers[serviceName] = handler;

    public Task<PluginExecuteResponse> CallAsync(string serviceName, PluginExecuteRequest request, CancellationToken ct = default)
    {
        _calls.Add((serviceName, request));

        if (!_handlers.TryGetValue(serviceName, out var handler))
            throw new InvalidOperationException(
                $"FakePluginServiceClient has no setup for service '{serviceName}'. " +
                $"Call Setup(\"{serviceName}\", response) before invoking CallAsync.");

        return Task.FromResult(handler(request));
    }
}
