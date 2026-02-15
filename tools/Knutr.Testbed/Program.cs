using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

// ─────────────────────────────────────────────────────────────────────────────
// knutr testbed: CLI tool that simulates Slack for local testing.
//
// Usage:
//   knutr testbed                          # interactive mode (default)
//   knutr testbed scenario.conv            # run a conversation file
//   knutr testbed --url http://localhost:7071
//   knutr testbed --callback-port 9999
//   knutr testbed --callback-host 172.18.0.1  # for kind
//
// Interactive commands:
//   /ping                    send a slash command
//   /knutr deploy staging    send a slash command with args
//   hello knutr              send a message event
//   !thread new              start a new thread
//   !thread                  leave thread (or !exit while in a thread)
//   !health                  check /health endpoint
//   !manifest <url>          fetch /manifest from a plugin service
//   !config <plugin>         show plugin config via /knutr <plugin> status
//   !wait <ms>               pause for N milliseconds
//   !clear                   clear screen
//   !exit / quit             quit testbed
//
// Conversation files (.conv):
//   # comment or blank lines are ignored
//   /slash command            slash commands
//   !thread new               testbed commands
//   !wait 2000                pause between steps
//   plain text                message events
// ─────────────────────────────────────────────────────────────────────────────

// ── Brand colors (pastel, true-color ANSI — matches KnutrConsoleFormatter) ──
const string Rst  = "\x1b[0m";
const string Muted    = "\x1b[38;2;100;100;105m";
const string MutedDim = "\x1b[38;2;55;55;60m";
const string Teal     = "\x1b[38;2;121;204;204m";
const string Lavender = "\x1b[38;2;160;136;210m";
const string Amber    = "\x1b[38;2;219;182;112m";
const string Rose     = "\x1b[38;2;214;132;132m";
const string Orange   = "\x1b[38;2;226;183;140m";

var knutrUrl = args.FirstOrDefault(a => a.StartsWith("--url="))?.Split('=', 2)[1]
    ?? (args.Contains("--url") ? args[Array.IndexOf(args, "--url") + 1] : null)
    ?? "http://localhost:7071";

var callbackPort = int.TryParse(
    args.FirstOrDefault(a => a.StartsWith("--callback-port="))?.Split('=', 2)[1]
    ?? (args.Contains("--callback-port") ? args[Array.IndexOf(args, "--callback-port") + 1] : null),
    out var p) ? p : 9876;

var callbackHost = args.FirstOrDefault(a => a.StartsWith("--callback-host="))?.Split('=', 2)[1]
    ?? (args.Contains("--callback-host") ? args[Array.IndexOf(args, "--callback-host") + 1] : null)
    ?? "localhost";

// Check for a .conv file argument (positional, not a flag)
var convFile = args.FirstOrDefault(a => !a.StartsWith("--") && a.EndsWith(".conv", StringComparison.OrdinalIgnoreCase));

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
using var cts = new CancellationTokenSource();

// When callback-host is set, bind to all interfaces so the pod can reach us.
// Otherwise bind to localhost only.
var listenUrl = callbackHost == "localhost"
    ? $"http://localhost:{callbackPort}"
    : $"http://+:{callbackPort}";
var callbackUrl = $"http://{callbackHost}:{callbackPort}";
var callbackListener = StartCallbackListener(listenUrl, cts.Token);

// Thread context: when set, messages include thread_ts to simulate being inside a thread.
string? currentThreadTs = null;

WriteHeader(knutrUrl, callbackUrl);

if (convFile is not null)
{
    // ── Conversation file mode ───────────────────────────────────────────
    await RunConversationFile(convFile);
}
else
{
    // ── Interactive mode ─────────────────────────────────────────────────
    await RunInteractive();
}

cts.Cancel();
Console.WriteLine("\nBye!");
return;

// ─────────────────────────────────────────────────────────────────────────────
// Run a .conv file
// ─────────────────────────────────────────────────────────────────────────────
async Task RunConversationFile(string path)
{
    if (!File.Exists(path))
    {
        WriteError($"File not found: {path}");
        return;
    }

    var lines = await File.ReadAllLinesAsync(path);
    Console.WriteLine($"  {Lavender}Running conversation: {path} ({lines.Length} lines){Rst}");
    Console.WriteLine(new string('─', 60));

    foreach (var rawLine in lines)
    {
        var line = rawLine.Trim();

        // Skip empty lines and comments
        if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
            continue;

        // Show what we're executing
        var threadLabel = currentThreadTs is not null ? $" {Amber}[thread]{Rst}" : "";
        Console.WriteLine($"\n{Muted}  conv>{Rst}{threadLabel} {line}");

        try
        {
            await DispatchInput(line);
        }
        catch (HttpRequestException ex)
        {
            WriteError($"Connection failed: {ex.Message}");
            WriteHint($"Is knutr running at {knutrUrl}?");
        }
        catch (TaskCanceledException)
        {
            WriteError("Request timed out");
        }
        catch (Exception ex)
        {
            WriteError($"Error: {ex.Message}");
        }
    }

    // Wait a moment for any trailing callbacks
    Console.WriteLine($"\n{Muted}  ── conversation complete, waiting for callbacks... ──{Rst}");
    await Task.Delay(3000);
}

// ─────────────────────────────────────────────────────────────────────────────
// Interactive REPL
// ─────────────────────────────────────────────────────────────────────────────
async Task RunInteractive()
{
    while (!cts.IsCancellationRequested)
    {
        var threadLabel = currentThreadTs is not null ? $" {Amber}[thread:{currentThreadTs}]{Rst}" : "";
        Console.Write($"\n{Teal}knutr>{Rst}{threadLabel} ");

        var input = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(input)) continue;

        // Exit: leave thread first, then quit
        if (input.Equals("!exit", StringComparison.OrdinalIgnoreCase) ||
            input.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
            input.Equals("quit", StringComparison.OrdinalIgnoreCase))
        {
            if (currentThreadTs is not null)
            {
                currentThreadTs = null;
                Console.WriteLine($"  {Teal}Left thread context.{Rst}");
                continue;
            }
            break;
        }

        try
        {
            await DispatchInput(input);
        }
        catch (HttpRequestException ex)
        {
            WriteError($"Connection failed: {ex.Message}");
            WriteHint($"Is knutr running at {knutrUrl}?");
        }
        catch (TaskCanceledException)
        {
            WriteError("Request timed out");
        }
        catch (Exception ex)
        {
            WriteError($"Error: {ex.Message}");
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Dispatch a single input line (shared between interactive and conv modes)
// ─────────────────────────────────────────────────────────────────────────────
async Task DispatchInput(string input)
{
    if (input.Equals("!clear", StringComparison.OrdinalIgnoreCase))
    {
        WriteHeader(knutrUrl, callbackUrl);
    }
    else if (input.Equals("!thread", StringComparison.OrdinalIgnoreCase))
    {
        if (currentThreadTs is not null)
        {
            currentThreadTs = null;
            Console.WriteLine($"  {Teal}Left thread context.{Rst}");
        }
        else
        {
            Console.WriteLine($"  {Amber}Usage: !thread <ts>  — enter a thread, or !thread new — start a new thread{Rst}");
        }
    }
    else if (input.StartsWith("!thread ", StringComparison.OrdinalIgnoreCase))
    {
        var threadArg = input[8..].Trim();
        if (threadArg.Equals("new", StringComparison.OrdinalIgnoreCase))
        {
            currentThreadTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        }
        else
        {
            currentThreadTs = threadArg;
        }
        Console.WriteLine($"  {Teal}Entered thread context: {currentThreadTs}{Rst}");
    }
    else if (input.StartsWith("!wait ", StringComparison.OrdinalIgnoreCase))
    {
        var msStr = input[6..].Trim();
        if (int.TryParse(msStr, out var ms) && ms > 0)
        {
            Console.WriteLine($"  {Muted}Waiting {ms}ms...{Rst}");
            await Task.Delay(ms);
        }
    }
    else if (input.Equals("!health", StringComparison.OrdinalIgnoreCase))
    {
        await CheckHealth(http, knutrUrl);
    }
    else if (input.StartsWith("!manifest ", StringComparison.OrdinalIgnoreCase))
    {
        var url = input[10..].Trim();
        await FetchManifest(http, url);
    }
    else if (input.StartsWith("!config ", StringComparison.OrdinalIgnoreCase))
    {
        var plugin = input[8..].Trim();
        await SendSlashCommand(http, knutrUrl, callbackUrl, $"/knutr {plugin} status");
    }
    else if (input.StartsWith('/'))
    {
        await SendSlashCommand(http, knutrUrl, callbackUrl, input);
    }
    else
    {
        await SendMessage(http, knutrUrl, callbackUrl, input, currentThreadTs);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Send a slash command (form-encoded POST to /slack/commands)
// ─────────────────────────────────────────────────────────────────────────────
static async Task SendSlashCommand(HttpClient http, string baseUrl, string callbackUrl, string input)
{
    // Parse: /knutr deploy staging  →  command="/knutr", text="deploy staging"
    var parts = input.Split(' ', 2, StringSplitOptions.TrimEntries);
    var command = parts[0]; // e.g. "/knutr"
    var text = parts.Length > 1 ? parts[1] : "";

    var form = new Dictionary<string, string>
    {
        ["command"] = command,
        ["text"] = text,
        ["team_id"] = "T_TESTTEAM",
        ["team_domain"] = "testbed",
        ["channel_id"] = "C_TESTCHANNEL",
        ["channel_name"] = "testbed-channel",
        ["user_id"] = "U_TESTUSER",
        ["user_name"] = "testbed-user",
        ["response_url"] = $"{callbackUrl}/response",
        ["trigger_id"] = $"trigger_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}"
    };

    WriteOutbound($"POST /slack/commands  command={command}  text=\"{text}\"");

    var content = new FormUrlEncodedContent(form);
    var response = await http.PostAsync($"{baseUrl}/slack/commands", content);

    WriteStatus(response.StatusCode);

    if (response.Content.Headers.ContentLength > 0)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (!string.IsNullOrWhiteSpace(body) && body != "null")
        {
            WriteInbound($"Immediate response: {body}");
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Send a message event (JSON POST to /slack/events)
// ─────────────────────────────────────────────────────────────────────────────
static async Task SendMessage(HttpClient http, string baseUrl, string callbackUrl, string text, string? threadTs = null)
{
    var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

    // Build the event object as a dictionary so we can conditionally include thread_ts
    var evt = new Dictionary<string, object>
    {
        ["type"] = "message",
        ["channel"] = "C_TESTCHANNEL",
        ["user"] = "U_TESTUSER",
        ["text"] = text,
        ["ts"] = ts,
        ["response_url"] = $"{callbackUrl}/response",
    };
    if (threadTs is not null)
        evt["thread_ts"] = threadTs;

    var payload = new Dictionary<string, object>
    {
        ["type"] = "event_callback",
        ["team_id"] = "T_TESTTEAM",
        ["event"] = evt,
    };

    var threadInfo = threadTs is not null ? $"  thread_ts={threadTs}" : "";
    WriteOutbound($"POST /slack/events  text=\"{text}\"{threadInfo}");

    var json = JsonSerializer.Serialize(payload);
    var content = new StringContent(json, Encoding.UTF8, "application/json");
    var response = await http.PostAsync($"{baseUrl}/slack/events", content);

    WriteStatus(response.StatusCode);
}

// ─────────────────────────────────────────────────────────────────────────────
// Check /health
// ─────────────────────────────────────────────────────────────────────────────
static async Task CheckHealth(HttpClient http, string baseUrl)
{
    WriteOutbound($"GET {baseUrl}/health");
    var response = await http.GetAsync($"{baseUrl}/health");
    var body = await response.Content.ReadAsStringAsync();
    WriteStatus(response.StatusCode);
    WriteInbound(body);
}

// ─────────────────────────────────────────────────────────────────────────────
// Fetch /manifest from a plugin service
// ─────────────────────────────────────────────────────────────────────────────
static async Task FetchManifest(HttpClient http, string url)
{
    var target = url.Contains("://") ? url : $"http://{url}";
    WriteOutbound($"GET {target}/manifest");
    var response = await http.GetAsync($"{target}/manifest");
    var body = await response.Content.ReadAsStringAsync();
    WriteStatus(response.StatusCode);

    // Pretty-print JSON
    try
    {
        var doc = JsonDocument.Parse(body);
        var pretty = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        WriteInbound(pretty);
    }
    catch
    {
        WriteInbound(body);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Callback listener: captures response_url POSTs from the bot
// ─────────────────────────────────────────────────────────────────────────────
static Task StartCallbackListener(string prefix, CancellationToken ct)
{
    return Task.Run(async () =>
    {
        var listener = new HttpListener();
        listener.Prefixes.Add(prefix.TrimEnd('/') + "/");
        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            Console.WriteLine($"  {Amber}[warn] Could not start callback listener on {prefix}: {ex.Message}{Rst}");
            Console.WriteLine($"  {Amber}[warn] response_url callbacks will not be captured.{Rst}");
            return;
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var context = await listener.GetContextAsync().WaitAsync(ct);
                var body = await new StreamReader(context.Request.InputStream).ReadToEndAsync();

                Console.WriteLine();
                Console.Write($"  {Lavender}\u25c4 CALLBACK{Rst} ");
                Console.WriteLine($"{Muted}[{context.Request.HttpMethod} {context.Request.Url?.PathAndQuery}]{Rst}");

                // Pretty-print the response
                try
                {
                    var doc = JsonDocument.Parse(body);
                    var pretty = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });

                    // Extract key fields for a summary line
                    var root = doc.RootElement;
                    if (root.TryGetProperty("text", out var textProp))
                    {
                        Console.WriteLine($"    text: {textProp.GetString()}");
                    }
                    if (root.TryGetProperty("response_type", out var rtProp))
                    {
                        var rt = rtProp.GetString();
                        var rtColor = rt == "ephemeral" ? Amber : Teal;
                        Console.WriteLine($"    {Muted}response_type:{Rst} {rtColor}{rt}{Rst}");
                    }
                    if (root.TryGetProperty("blocks", out _))
                    {
                        Console.WriteLine($"    {Muted}blocks: (present){Rst}");
                    }

                    Console.WriteLine($"    {MutedDim}full payload:");
                    Console.WriteLine($"{Indent(pretty, "      ")}{Rst}");
                }
                catch
                {
                    Console.WriteLine($"    {body}");
                }

                // Respond 200 to the bot
                context.Response.StatusCode = 200;
                context.Response.Close();

                // Re-show prompt
                Console.Write($"\n{Teal}knutr>{Rst} ");
            }
            catch (OperationCanceledException) { break; }
            catch { /* listener shutting down */ break; }
        }

        listener.Stop();
    }, ct);
}

// ─────────────────────────────────────────────────────────────────────────────
// Output helpers
// ─────────────────────────────────────────────────────────────────────────────
static void WriteHeader(string knutrUrl, string callbackUrl)
{
    Console.Clear();
    Console.Write($"{Rose}");
    Console.WriteLine(@"
 _                _          _            _   _              _
| | ___ __  _   _| |_ _ __  | |_ ___  ___| |_| |__   ___  __| |
| |/ / '_ \| | | | __| '__| | __/ _ \/ __| __| '_ \ / _ \/ _` |
|   <| | | | |_| | |_| |    | ||  __/\__ \ |_| |_) |  __/ (_| |
|_|\_\_| |_|\__,_|\__|_|     \__\___||___/\__|_.__/ \___|\__,_|
    ");
    Console.Write(Rst);
    Console.WriteLine($"  {Muted}Target:{Rst}       {knutrUrl}");
    Console.WriteLine($"  {Muted}Callback:{Rst}     {callbackUrl}");
    Console.WriteLine($"  {Muted}User:{Rst}         U_TESTUSER");
    Console.WriteLine($"  {Muted}Channel:{Rst}      C_TESTCHANNEL");
    Console.WriteLine($"  {Muted}Team:{Rst}         T_TESTTEAM");
    Console.WriteLine();
    Console.WriteLine($"  Type a slash command (e.g. /ping) or a message (e.g. hello knutr).");
    Console.WriteLine($"  Threads: !thread new — start, !exit or !thread — leave");
    Console.WriteLine($"  Special: !health  !manifest <url>  !config <plugin>  !wait <ms>  !clear  !exit");
    Console.WriteLine(new string('─', 60));
}

static void WriteOutbound(string msg)
{
    Console.WriteLine($"  {Lavender}\u25ba{Rst} {msg}");
}

static void WriteInbound(string msg)
{
    Console.WriteLine($"  {Teal}\u25c4{Rst} {msg}");
}

static void WriteStatus(HttpStatusCode status)
{
    var code = (int)status;
    var color = code < 300 ? Teal : code < 400 ? Amber : Rose;
    Console.WriteLine($"  {color}\u2190 {code} {status}{Rst}");
}

static void WriteError(string msg)
{
    Console.WriteLine($"  {Rose}\u2717 {msg}{Rst}");
}

static void WriteHint(string msg)
{
    Console.WriteLine($"  {Muted}({msg}){Rst}");
}

static string Indent(string text, string prefix)
{
    return string.Join('\n', text.Split('\n').Select(l => prefix + l));
}
