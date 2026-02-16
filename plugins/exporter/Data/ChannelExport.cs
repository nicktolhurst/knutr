namespace Knutr.Plugins.Exporter.Data;

public sealed class ChannelExport
{
    public required string ChannelId { get; set; }
    public required string Status { get; set; }
    public string? LastSyncTs { get; set; }
    public string? ErrorMessage { get; set; }
    public required string RequestedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
