namespace Knutr.Core.Replies;

using Knutr.Abstractions.Replies;
using Knutr.Core.Messaging;
using Microsoft.Extensions.Logging;

public sealed class ReplyService(IEventBus bus, ILogger<ReplyService> log) : IReplyService
{
    public Task SendAsync(Reply reply, ReplyHandle handle, ResponseMode mode, CancellationToken ct = default)
    {
        log.LogInformation("ReplyService: sending reply (mode={Mode})", mode);
        bus.Publish(new OutboundReply(handle, reply, mode));
        return Task.CompletedTask;
    }
}
