namespace Knutr.Core.Replies;

using Knutr.Abstractions.Replies;

public interface IReplyService
{
    Task SendAsync(Reply reply, ReplyHandle handle, ResponseMode mode, CancellationToken ct = default);
}
