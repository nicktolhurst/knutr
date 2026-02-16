using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Knutr.Core.Messaging;

public sealed class InMemoryEventBus(ILogger<InMemoryEventBus> logger) : IEventBus
{
    private readonly ConcurrentDictionary<Type, List<Func<object, CancellationToken, Task>>> _subscribers = new();

    public void Publish<T>(T message)
    {
        if (_subscribers.TryGetValue(typeof(T), out var handlers))
        {
            logger.LogDebug("Publishing {EventType} to {Count} subscriber(s)", typeof(T).Name, handlers.Count);
            foreach (var h in handlers.ToArray())
            {
                _ = h(message!, CancellationToken.None).ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        logger.LogError(t.Exception, "Event handler for {EventType} failed", typeof(T).Name);
                    }
                }, TaskScheduler.Default);
            }
        }
    }

    public void Subscribe<T>(Func<T, CancellationToken, Task> handler)
    {
        var list = _subscribers.GetOrAdd(typeof(T), _ => []);
        list.Add(async (obj, ct) => await handler((T)obj, ct));
        logger.LogDebug("Subscribed handler for {EventType}", typeof(T).Name);
    }
}
