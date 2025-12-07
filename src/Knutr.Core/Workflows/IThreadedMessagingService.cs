namespace Knutr.Core.Workflows;

using Knutr.Abstractions.Messaging;

/// <summary>
/// Alias for backward compatibility.
/// New code should use IMessagingService from Knutr.Abstractions.Messaging.
/// </summary>
public interface IThreadedMessagingService : IMessagingService
{
}
