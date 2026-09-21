using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpFar.App.Diagnostics;

namespace CSharpFar.App.Updates;

internal sealed class UpdateCheckService
{
    internal const string GitHubApiVersion = "2026-03-10";
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/DimonSmart/CSharpFar/releases/latest";
    private static readonly HttpClient SharedHttpClient = new();

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _timeout;
    private readonly IDiagnosticLog _diagnostics;

    public UpdateCheckService(
        HttpClient? httpClient = null,
        TimeSpan? timeout = null,
        IDiagnosticLog? diagnostics = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
        _diagnostics = diagnostics ?? DisabledDiagnosticLog.Instance;
        if (_timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    public async Task<UpdateCheckResult> CheckAsync(
        ReleaseVersion? currentVersion,
        CancellationToken cancellationToken)
    {
        if (currentVersion is null)
            return Unavailable(null, "current comparable version unavailable");

        Stopwatch? stopwatch = _diagnostics.IsEnabled ? Stopwatch.StartNew() : null;
        if (_diagnostics.IsEnabled)
        {
            _diagnostics.Write(
                DiagnosticCategory.UpdateCheck,
                $"Request started. Current={currentVersion} Endpoint={LatestReleaseUrl} Timeout={_timeout.TotalSeconds:0.###}s");
        }

        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(_timeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue(
                "CSharpFar",
                currentVersion.Value.ToString()));
            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", GitHubApiVersion);

            using HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCancellation.Token);

            LogHttpResponse(response, stopwatch);
            if (!response.IsSuccessStatusCode)
            {
                return Unavailable(
                    currentVersion,
                    $"non-success HTTP status {(int)response.StatusCode} {response.ReasonPhrase ?? response.StatusCode.ToString()}");
            }

            await using Stream stream =
                await response.Content.ReadAsStreamAsync(timeoutCancellation.Token);
            GitHubReleaseDto? release = await JsonSerializer.DeserializeAsync<GitHubReleaseDto>(
                stream,
                cancellationToken: timeoutCancellation.Token);

            if (string.IsNullOrWhiteSpace(release?.TagName))
                return Unavailable(currentVersion, "missing tag_name");

            if (!ReleaseVersionParser.TryParse(release.TagName, out ReleaseVersion latestVersion))
                return Unavailable(currentVersion, "invalid release version");

            if (_diagnostics.IsEnabled)
            {
                _diagnostics.Write(
                    DiagnosticCategory.UpdateCheck,
                    $"Latest release tag={release.TagName}");
            }

            if (latestVersion.CompareTo(currentVersion.Value) <= 0)
            {
                if (_diagnostics.IsEnabled)
                {
                    _diagnostics.Write(
                        DiagnosticCategory.UpdateCheck,
                        $"Result=UpToDate Current={currentVersion} Latest={latestVersion}");
                }

                return new UpdateCheckResult(
                    UpdateCheckStatus.UpToDate,
                    currentVersion,
                    latestVersion,
                    null);
            }

            if (!TryValidateReleaseUri(release.HtmlUrl, out Uri? releaseUri))
                return Unavailable(currentVersion, "invalid release URL");

            if (_diagnostics.IsEnabled)
            {
                _diagnostics.Write(
                    DiagnosticCategory.UpdateCheck,
                    $"Result=UpdateAvailable Current={currentVersion} Latest={latestVersion}");
            }

            return new UpdateCheckResult(
                UpdateCheckStatus.UpdateAvailable,
                currentVersion,
                latestVersion,
                releaseUri);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (_diagnostics.IsEnabled)
            {
                _diagnostics.Write(
                    DiagnosticCategory.UpdateCheck,
                    $"Request cancelled by caller after {Elapsed(stopwatch)}");
            }

            throw;
        }
        catch (OperationCanceledException)
        {
            if (_diagnostics.IsEnabled)
            {
                _diagnostics.Write(
                    DiagnosticCategory.UpdateCheck,
                    $"Request failed after {Elapsed(stopwatch)}: timeout");
            }

            return Unavailable(currentVersion, "timeout");
        }
        catch (HttpRequestException ex)
        {
            if (_diagnostics.IsEnabled)
            {
                _diagnostics.WriteException(
                    DiagnosticCategory.UpdateCheck,
                    ex,
                    $"Request failed after {Elapsed(stopwatch)}: HttpRequestException");
            }

            return Unavailable(currentVersion, "HttpRequestException");
        }
        catch (JsonException ex)
        {
            if (_diagnostics.IsEnabled)
            {
                _diagnostics.WriteException(
                    DiagnosticCategory.UpdateCheck,
                    ex,
                    $"Request failed after {Elapsed(stopwatch)}: invalid JSON");
            }

            return Unavailable(currentVersion, "invalid JSON");
        }
        catch (NotSupportedException ex)
        {
            if (_diagnostics.IsEnabled)
            {
                _diagnostics.WriteException(
                    DiagnosticCategory.UpdateCheck,
                    ex,
                    $"Request failed after {Elapsed(stopwatch)}: unsupported response format");
            }

            return Unavailable(currentVersion, "unsupported response format");
        }
    }

    private UpdateCheckResult Unavailable(
        ReleaseVersion? currentVersion,
        string reason)
    {
        if (_diagnostics.IsEnabled)
        {
            _diagnostics.Write(
                DiagnosticCategory.UpdateCheck,
                $"Result=Unavailable Current={currentVersion?.ToString() ?? "unavailable"} Reason={reason}");
        }

        return UpdateCheckResult.Unavailable(currentVersion);
    }

    private void LogHttpResponse(HttpResponseMessage response, Stopwatch? stopwatch)
    {
        if (!_diagnostics.IsEnabled)
            return;

        var parts = new List<string>
        {
            $"HTTP {(int)response.StatusCode} {response.ReasonPhrase ?? response.StatusCode.ToString()} after {Elapsed(stopwatch)}",
        };

        AddHeader(response, parts, "X-RateLimit-Limit");
        AddHeader(response, parts, "X-RateLimit-Remaining");
        AddHeader(response, parts, "X-RateLimit-Reset");

        _diagnostics.Write(DiagnosticCategory.UpdateCheck, string.Join("; ", parts));
    }

    private static void AddHeader(
        HttpResponseMessage response,
        ICollection<string> parts,
        string name)
    {
        if (response.Headers.TryGetValues(name, out IEnumerable<string>? values))
            parts.Add($"{name}={string.Join(",", values)}");
    }

    private static string Elapsed(Stopwatch? stopwatch) =>
        stopwatch is null ? "unknown" : $"{stopwatch.Elapsed.TotalSeconds:0.00}s";

    private static bool TryValidateReleaseUri(string? value, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? candidate) ||
            !string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(candidate.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !candidate.AbsolutePath.StartsWith(
                "/DimonSmart/CSharpFar/releases/",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        uri = candidate;
        return true;
    }

    private sealed class GitHubReleaseDto
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }
    }
}
