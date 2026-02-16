namespace Knutr.Plugins.Exporter.Data;

public sealed class ExportedMessage
{
    public long Id { get; set; }
    public required string ChannelId { get; set; }
    public required string MessageTs { get; set; }
    public string? ThreadTs { get; set; }
    public required string UserId { get; set; }
    public required string Text { get; set; }
    public string? EditedTs { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
}
