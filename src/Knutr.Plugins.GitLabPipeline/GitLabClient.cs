namespace Knutr.Plugins.GitLabPipeline;

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public interface IGitLabClient
{
    Task<PipelineResult> TriggerPipelineAsync(string project, string refName, Dictionary<string, string>? variables = null, CancellationToken ct = default);
    Task<PipelineInfo?> GetPipelineAsync(string project, int pipelineId, CancellationToken ct = default);
    Task<PipelineInfo?> GetLatestPipelineAsync(string project, string refName, CancellationToken ct = default);
    Task<bool> CancelPipelineAsync(string project, int pipelineId, CancellationToken ct = default);
    Task<bool> RetryPipelineAsync(string project, int pipelineId, CancellationToken ct = default);
}

public sealed class GitLabClient : IGitLabClient
{
    private readonly HttpClient _http;
    private readonly GitLabOptions _options;
    private readonly ILogger<GitLabClient> _log;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public GitLabClient(HttpClient http, IOptions<GitLabOptions> options, ILogger<GitLabClient> log)
    {
        _http = http;
        _options = options.Value;
        _log = log;

        _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/api/v4/");
        _http.DefaultRequestHeaders.Add("PRIVATE-TOKEN", _options.AccessToken);
    }

    public async Task<PipelineResult> TriggerPipelineAsync(
        string project,
        string refName,
        Dictionary<string, string>? variables = null,
        CancellationToken ct = default)
    {
        var encodedProject = Uri.EscapeDataString(project);
        var url = $"projects/{encodedProject}/pipeline";

        var payload = new PipelineCreateRequest
        {
            Ref = refName,
            Variables = variables?.Select(kv => new PipelineVariable { Key = kv.Key, Value = kv.Value }).ToList()
        };

        _log.LogInformation("Triggering pipeline for {Project} on ref {Ref}", project, refName);

        try
        {
            var response = await _http.PostAsJsonAsync(url, payload, JsonOptions, ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                _log.LogWarning("GitLab API error: {StatusCode} - {Error}", response.StatusCode, error);
                return PipelineResult.Failure($"GitLab API error: {response.StatusCode} - {error}");
            }

            var pipeline = await response.Content.ReadFromJsonAsync<PipelineInfo>(JsonOptions, ct);
            return pipeline is not null
                ? PipelineResult.Success(pipeline)
                : PipelineResult.Failure("Failed to parse pipeline response");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to trigger pipeline");
            return PipelineResult.Failure($"Failed to trigger pipeline: {ex.Message}");
        }
    }

    public async Task<PipelineInfo?> GetPipelineAsync(string project, int pipelineId, CancellationToken ct = default)
    {
        var encodedProject = Uri.EscapeDataString(project);
        var url = $"projects/{encodedProject}/pipelines/{pipelineId}";

        try
        {
            return await _http.GetFromJsonAsync<PipelineInfo>(url, JsonOptions, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to get pipeline {PipelineId}", pipelineId);
            return null;
        }
    }

    public async Task<PipelineInfo?> GetLatestPipelineAsync(string project, string refName, CancellationToken ct = default)
    {
        var encodedProject = Uri.EscapeDataString(project);
        var encodedRef = Uri.EscapeDataString(refName);
        var url = $"projects/{encodedProject}/pipelines?ref={encodedRef}&per_page=1";

        try
        {
            var pipelines = await _http.GetFromJsonAsync<List<PipelineInfo>>(url, JsonOptions, ct);
            return pipelines?.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to get latest pipeline for ref {Ref}", refName);
            return null;
        }
    }

    public async Task<bool> CancelPipelineAsync(string project, int pipelineId, CancellationToken ct = default)
    {
        var encodedProject = Uri.EscapeDataString(project);
        var url = $"projects/{encodedProject}/pipelines/{pipelineId}/cancel";

        try
        {
            var response = await _http.PostAsync(url, null, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to cancel pipeline {PipelineId}", pipelineId);
            return false;
        }
    }

    public async Task<bool> RetryPipelineAsync(string project, int pipelineId, CancellationToken ct = default)
    {
        var encodedProject = Uri.EscapeDataString(project);
        var url = $"projects/{encodedProject}/pipelines/{pipelineId}/retry";

        try
        {
            var response = await _http.PostAsync(url, null, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to retry pipeline {PipelineId}", pipelineId);
            return false;
        }
    }
}

// Request/Response models
public sealed class PipelineCreateRequest
{
    public string Ref { get; set; } = string.Empty;
    public List<PipelineVariable>? Variables { get; set; }
}

public sealed class PipelineVariable
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string VariableType { get; set; } = "env_var";
}

public sealed class PipelineInfo
{
    public int Id { get; set; }
    public int Iid { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Ref { get; set; } = string.Empty;
    public string Sha { get; set; } = string.Empty;
    public string WebUrl { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string Source { get; set; } = string.Empty;
}

public sealed class PipelineResult
{
    public bool IsSuccess { get; init; }
    public PipelineInfo? Pipeline { get; init; }
    public string? ErrorMessage { get; init; }

    public static PipelineResult Success(PipelineInfo pipeline) => new() { IsSuccess = true, Pipeline = pipeline };
    public static PipelineResult Failure(string error) => new() { IsSuccess = false, ErrorMessage = error };
}
