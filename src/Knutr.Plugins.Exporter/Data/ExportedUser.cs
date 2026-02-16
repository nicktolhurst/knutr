namespace Knutr.Plugins.Exporter.Data;

public sealed class ExportedUser
{
    public required string UserId { get; set; }
    public string? DisplayName { get; set; }
    public string? RealName { get; set; }
    public bool IsBot { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
