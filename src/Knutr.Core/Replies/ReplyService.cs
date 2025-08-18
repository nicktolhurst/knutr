namespace Knutr.Core.Replies;

using Knutr.Abstractions.Replies;
using Knutr.Core.Messaging;
using Microsoft.Extensions.Logging;

public sealed class ReplyService : IReplyService
{
    private readonly IEventBus _bus;
    private readonly ILogger<ReplyService> _log;

    public ReplyService(IEventBus bus, ILogger<ReplyService> log)
    {
        _bus = bus; _log = log;
    }

    public Task SendAsync(Reply reply, ReplyHandle handle, ResponseMode mode, CancellationToken ct = default)
    {
        _log.LogInformation("ReplyService: sending reply (mode={Mode})", mode);
        _bus.Publish(new OutboundReply(handle, reply, mode));
        return Task.CompletedTask;
    }
}
