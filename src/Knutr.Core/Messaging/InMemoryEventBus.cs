using System.Collections.Concurrent;

namespace Knutr.Core.Messaging;

public sealed class InMemoryEventBus : IEventBus
{
    private readonly ConcurrentDictionary<Type, List<Func<object, CancellationToken, Task>>> _subscribers = new();

    public void Publish<T>(T message)
    {
        if (_subscribers.TryGetValue(typeof(T), out var handlers))
        {
            foreach (var h in handlers.ToArray())
            {
                _ = h(message!, CancellationToken.None);
            }
        }
    }

    public void Subscribe<T>(Func<T, CancellationToken, Task> handler)
    {
        var list = _subscribers.GetOrAdd(typeof(T), _ => []);
        list.Add(async (obj, ct) => await handler((T)obj, ct));
    }
}
