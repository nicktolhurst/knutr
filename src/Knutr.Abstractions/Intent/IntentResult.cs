namespace Knutr.Abstractions.Intent;

/// <summary>
/// Represents a recognized intent from natural language input.
/// </summary>
public sealed record IntentResult(
    string? Command,
    string? Action,
    Dictionary<string, string> Parameters,
    float Confidence
)
{
    public static IntentResult None => new(null, null, new(), 0f);

    public bool HasIntent => Command != null && Confidence > 0.5f;

    /// <summary>
    /// Creates a deploy intent with branch and environment parameters.
    /// </summary>
    public static IntentResult Deploy(string? branch, string? environment, float confidence = 1f)
        => new("gitlab", "deploy", new Dictionary<string, string>
        {
            ["branch"] = branch ?? "main",
            ["env"] = environment ?? ""
        }, confidence);

    /// <summary>
    /// Creates a build intent with branch parameter.
    /// </summary>
    public static IntentResult Build(string? branch, float confidence = 1f)
        => new("gitlab", "build", new Dictionary<string, string>
        {
            ["branch"] = branch ?? "main"
        }, confidence);

    /// <summary>
    /// Creates a status intent.
    /// </summary>
    public static IntentResult Status(float confidence = 1f)
        => new("gitlab", "status", new(), confidence);
}
