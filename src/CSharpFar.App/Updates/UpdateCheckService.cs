using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CSharpFar.App.Updates;

internal sealed class UpdateCheckService
{
    internal const string GitHubApiVersion = "2026-03-10";
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/DimonSmart/CSharpFar/releases/latest";
    private static readonly HttpClient SharedHttpClient = new();

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _timeout;

    public UpdateCheckService(HttpClient? httpClient = null, TimeSpan? timeout = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
        if (_timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    public async Task<UpdateCheckResult> CheckAsync(
        ReleaseVersion? currentVersion,
        CancellationToken cancellationToken)
    {
        if (currentVersion is null)
            return UpdateCheckResult.Unavailable(null);

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
            if (!response.IsSuccessStatusCode)
                return UpdateCheckResult.Unavailable(currentVersion);

            await using Stream stream =
                await response.Content.ReadAsStreamAsync(timeoutCancellation.Token);
            GitHubReleaseDto? release = await JsonSerializer.DeserializeAsync<GitHubReleaseDto>(
                stream,
                cancellationToken: timeoutCancellation.Token);
            if (!ReleaseVersionParser.TryParse(release?.TagName, out ReleaseVersion latestVersion))
                return UpdateCheckResult.Unavailable(currentVersion);

            if (latestVersion.CompareTo(currentVersion.Value) <= 0)
            {
                return new UpdateCheckResult(
                    UpdateCheckStatus.UpToDate,
                    currentVersion,
                    latestVersion,
                    null);
            }

            if (!TryValidateReleaseUri(release?.HtmlUrl, out Uri? releaseUri))
                return UpdateCheckResult.Unavailable(currentVersion);

            return new UpdateCheckResult(
                UpdateCheckStatus.UpdateAvailable,
                currentVersion,
                latestVersion,
                releaseUri);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return UpdateCheckResult.Unavailable(currentVersion);
        }
        catch (HttpRequestException)
        {
            return UpdateCheckResult.Unavailable(currentVersion);
        }
        catch (JsonException)
        {
            return UpdateCheckResult.Unavailable(currentVersion);
        }
        catch (NotSupportedException)
        {
            return UpdateCheckResult.Unavailable(currentVersion);
        }
    }

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
