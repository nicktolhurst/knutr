namespace Knutr.Core.Messaging;

public interface IEventBus
{
    void Publish<T>(T message);
    void Subscribe<T>(Func<T, CancellationToken, Task> handler);
}
