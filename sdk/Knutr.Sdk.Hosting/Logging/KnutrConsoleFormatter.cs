namespace Knutr.Sdk.Hosting.Logging;

using Serilog.Events;
using Serilog.Formatting;
using Serilog.Parsing;

/// <summary>
/// Custom Serilog text formatter that produces colorized, Nerd Font-enhanced console output.
/// Each service gets a deterministic color so interleaved kubectl logs are easy to scan.
///
/// Brand palette:
///   Theme   — a small set of pastel colors for UI chrome (icons, values, warnings).
///   Core    — a dedicated greyscale pair so the core service is always visually distinct.
///   Service — a curated 8-color pastel palette (~45° hue spacing) for plugin identification.
///   Colors are derived from the full service name before any truncation for display,
///   so they remain stable even if the column width changes.
/// </summary>
public sealed class KnutrConsoleFormatter : ITextFormatter
{
    private const string Reset = "\x1b[0m";
    private const string Dim = "\x1b[2m";
    private const string Bold = "\x1b[1m";

    // ── Brand / theme colors (pastel) ───────────────────────────────────────
    private const string ThemeTeal     = "\x1b[38;2;121;204;204m"; // info icon
    private const string ThemeAmber    = "\x1b[38;2;219;182;112m"; // warning icon + message
    private const string ThemeRose     = "\x1b[38;2;214;132;132m"; // error icon + message
    private const string ThemeCoral    = "\x1b[38;2;214;128;116m"; // fatal icon + message

    private const int DefaultColumnWidth = 4;

    private readonly string _serviceTag;
    private readonly string _serviceColorMuted;
    private readonly int _columnWidth;

    // ── Greyscale for "core" service ────────────────────────────────────────
    private const string CoreTag   = "\x1b[38;2;180;180;180m"; // light grey
    private const string CoreMuted = "\x1b[38;2;100;100;105m"; // dim grey

    // ── Curated 8-color plugin palette (~45° hue spacing) ─────────────────
    // Hand-picked pastels that stay visually distinct even when adjacent
    // FNV-1a hash slots are close together.
    private static readonly string[] ServiceColors =
    [
        "\x1b[38;2;222;144;144m", // 0 — Rose    (  0°)
        "\x1b[38;2;222;186;144m", // 1 — Peach   ( 35°)
        "\x1b[38;2;222;222;144m", // 2 — Gold    ( 60°)
        "\x1b[38;2;144;222;163m", // 3 — Mint    (140°)
        "\x1b[38;2;144;222;222m", // 4 — Cyan    (180°)
        "\x1b[38;2;144;166;222m", // 5 — Sky     (220°)
        "\x1b[38;2;183;144;222m", // 6 — Lavender(270°)
        "\x1b[38;2;222;144;193m", // 7 — Pink    (320°)
    ];

    // ── Muted plugin palette (same hues, lower saturation/lightness) ────────
    private static readonly string[] ServiceColorsMuted =
    [
        "\x1b[38;2;117;86;86m",  // 0 — Rose
        "\x1b[38;2;117;101;86m", // 1 — Peach
        "\x1b[38;2;117;117;86m", // 2 — Gold
        "\x1b[38;2;86;117;94m",  // 3 — Mint
        "\x1b[38;2;86;117;117m", // 4 — Cyan
        "\x1b[38;2;86;96;117m",  // 5 — Sky
        "\x1b[38;2;101;86;117m", // 6 — Lavender
        "\x1b[38;2;117;86;105m", // 7 — Pink
    ];

    public KnutrConsoleFormatter(string serviceName, int columnWidth = DefaultColumnWidth)
    {
        _columnWidth = columnWidth;

        string serviceColor;
        if (serviceName == "core")
        {
            serviceColor = CoreTag;
            _serviceColorMuted = CoreMuted;
        }
        else
        {
            // Color is always derived from the full name — stable across truncation widths.
            var idx = GetServiceColorIndex(serviceName);
            serviceColor = ServiceColors[idx];
            _serviceColorMuted = ServiceColorsMuted[idx];
        }

        var display = serviceName.Length > columnWidth
            ? serviceName[..columnWidth]
            : serviceName.PadRight(columnWidth);
        _serviceTag = $"{serviceColor}{display}{Reset}";
    }

    public void Format(LogEvent logEvent, TextWriter output)
    {
        var (icon, levelColor, messageColor) = GetLevelStyle(logEvent.Level);

        // Timestamp (muted service color)
        output.Write(_serviceColorMuted);
        output.Write('[');
        output.Write(logEvent.Timestamp.ToString("HH:mm:ss"));
        output.Write(']');
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
        // Template values are highlighted in the service's own color.
        if (messageColor.Length > 0)
            output.Write(messageColor);

        foreach (var token in logEvent.MessageTemplate.Tokens)
        {
            if (token is PropertyToken)
            {
                output.Write(_serviceColorMuted);
                token.Render(logEvent.Properties, output);
                output.Write(messageColor.Length > 0 ? messageColor : Reset);
            }
            else
            {
                token.Render(logEvent.Properties, output);
            }
        }

        if (messageColor.Length > 0)
            output.Write(Reset);

        output.WriteLine();

        // Exception block (rose, indented to align with message column)
        if (logEvent.Exception is not null)
        {
            var indent = new string(' ', 11 + _columnWidth);
            output.Write(ThemeRose);
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
    /// warnings and errors stand out in pastel amber / rose.
    /// </summary>
    private static (string Icon, string LevelColor, string MessageColor) GetLevelStyle(LogEventLevel level) =>
        level switch
        {
            LogEventLevel.Verbose     => ("\uf10c", Dim,                    Dim),               //  circle-o
            LogEventLevel.Debug       => ("\uf188", Dim,                    Dim),               //  bug
            LogEventLevel.Information => ("\uf05a", ThemeTeal,              ""),                 //  info-circle
            LogEventLevel.Warning     => ("\uf071", ThemeAmber,             ThemeAmber),         //  warning
            LogEventLevel.Error       => ("\uf057", ThemeRose,              ThemeRose),          //  times-circle
            LogEventLevel.Fatal       => ("\uf06d", ThemeCoral + Bold,      ThemeCoral + Bold),  //  fire
            _                         => ("?",      "",                      ""),
        };

    // FNV-1a hash for deterministic, well-distributed color assignment.
    // Mirrored in the knutr CLI script so colors match everywhere.
    private static int GetServiceColorIndex(string serviceName)
    {
        uint hash = 2166136261;
        foreach (var c in serviceName)
            hash = (hash ^ c) * 16777619;
        return (int)(hash % (uint)ServiceColors.Length);
    }
}
