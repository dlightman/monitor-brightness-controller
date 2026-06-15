using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;

namespace MonitorBrightnessController.Infrastructure;

/// <summary>
/// Retrieves the latest release from the GitHub Releases API using <see cref="HttpClient"/>.
/// Returns <c>null</c> on any failure (network, timeout, deserialization) to allow the
/// application to continue without interruption.
/// </summary>
public sealed class GitHubReleaseClient : IGitHubReleaseClient
{
    private const string ReleasesUrl =
        "https://api.github.com/repos/dlightman/monitor-brightness-controller/releases/latest";

    private const string UserAgent = "MonitorBrightnessController";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;

    /// <summary>
    /// Creates a new <see cref="GitHubReleaseClient"/> using the supplied <paramref name="httpClient"/>.
    /// </summary>
    /// <param name="httpClient">
    /// An <see cref="HttpClient"/> instance. The caller is responsible for its lifecycle.
    /// </param>
    public GitHubReleaseClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <inheritdoc />
    public async Task<GitHubRelease?> GetLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
            request.Headers.UserAgent.ParseAdd(UserAgent);

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var release = JsonSerializer.Deserialize<GitHubReleaseDto>(json, JsonOptions);

            if (release is null || string.IsNullOrWhiteSpace(release.TagName) || string.IsNullOrWhiteSpace(release.HtmlUrl))
            {
                return null;
            }

            return new GitHubRelease(release.TagName, release.HtmlUrl);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"GitHubReleaseClient: failed to fetch latest release: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Internal DTO used for JSON deserialization of the GitHub API response.
    /// Maps the snake_case <c>tag_name</c> and <c>html_url</c> fields.
    /// </summary>
    private sealed class GitHubReleaseDto
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }
    }
}
