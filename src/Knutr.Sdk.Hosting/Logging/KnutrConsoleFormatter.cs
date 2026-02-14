namespace Knutr.Sdk.Hosting.Logging;

using Serilog.Events;
using Serilog.Formatting;

/// <summary>
/// Custom Serilog text formatter that produces colorized, Nerd Font-enhanced console output.
/// Each service gets a deterministic color so interleaved kubectl logs are easy to scan.
/// </summary>
public sealed class KnutrConsoleFormatter : ITextFormatter
{
    private const string Reset = "\x1b[0m";
    private const string Dim = "\x1b[90m";

    private const int ServiceColumnWidth = 14;

    private readonly string _serviceTag;

    // Distinct ANSI colors assigned to services by name hash.
    private static readonly string[] ServiceColors =
    [
        "\x1b[36m",  // cyan
        "\x1b[32m",  // green
        "\x1b[35m",  // magenta
        "\x1b[33m",  // yellow
        "\x1b[34m",  // blue
        "\x1b[91m",  // bright red
        "\x1b[92m",  // bright green
        "\x1b[95m",  // bright magenta
        "\x1b[96m",  // bright cyan
        "\x1b[94m",  // bright blue
    ];

    public KnutrConsoleFormatter(string serviceName)
    {
        var color = GetServiceColor(serviceName);
        _serviceTag = $"{color}{serviceName.PadRight(ServiceColumnWidth)}{Reset}";
    }

    public void Format(LogEvent logEvent, TextWriter output)
    {
        var (icon, levelColor, messageColor) = GetLevelStyle(logEvent.Level);

        // Timestamp (dim)
        output.Write(Dim);
        output.Write(logEvent.Timestamp.ToString("HH:mm:ss"));
        output.Write(Reset);
        output.Write(' ');

        // Level icon (colored by severity)
        output.Write(levelColor);
        output.Write(icon);
        output.Write(Reset);
        output.Write("  ");

        // Service tag (deterministic color per service)
        output.Write(_serviceTag);
        output.Write(' ');

        // Message (colored by severity for warnings/errors, default for info)
        if (messageColor.Length > 0)
            output.Write(messageColor);

        logEvent.RenderMessage(output);

        if (messageColor.Length > 0)
            output.Write(Reset);

        output.WriteLine();

        // Exception block (red, indented to align with message column)
        if (logEvent.Exception is not null)
        {
            var indent = new string(' ', 11 + ServiceColumnWidth);
            output.Write("\x1b[31m");
            foreach (var line in logEvent.Exception.ToString().AsSpan().EnumerateLines())
            {
                output.Write(indent);
                output.WriteLine(line.TrimEnd());
            }
            output.Write(Reset);
        }
    }

    /// <summary>
    /// Returns (NerdFont icon, level ANSI color, message ANSI color) for a given log level.
    /// Info messages use default terminal color so they don't distract;
    /// warnings and errors stand out in yellow / red.
    /// </summary>
    private static (string Icon, string LevelColor, string MessageColor) GetLevelStyle(LogEventLevel level) =>
        level switch
        {
            LogEventLevel.Verbose     => ("\uf10c", Dim,          Dim),          //  circle-o
            LogEventLevel.Debug       => ("\uf188", Dim,          Dim),          //  bug
            LogEventLevel.Information => ("\uf05a", "\x1b[36m",   ""),           //  info-circle
            LogEventLevel.Warning     => ("\uf071", "\x1b[33m",   "\x1b[33m"),  //  warning
            LogEventLevel.Error       => ("\uf057", "\x1b[31m",   "\x1b[31m"),  //  times-circle
            LogEventLevel.Fatal       => ("\uf06d", "\x1b[91;1m", "\x1b[91;1m"),//  fire
            _                         => ("?",      "",            ""),
        };

    private static string GetServiceColor(string serviceName)
    {
        var hash = 0;
        foreach (var c in serviceName)
            hash = hash * 31 + c;
        return ServiceColors[Math.Abs(hash) % ServiceColors.Length];
    }
}
