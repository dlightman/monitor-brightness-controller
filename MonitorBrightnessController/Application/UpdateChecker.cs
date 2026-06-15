using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;

namespace MonitorBrightnessController.Application;

/// <summary>
/// Checks for application updates by querying the latest GitHub release via
/// <see cref="IGitHubReleaseClient"/> and comparing the published version against
/// the currently running assembly version. Returns a safe default (no update available)
/// on any failure.
/// </summary>
public sealed class UpdateChecker : IUpdateChecker
{
    private readonly IGitHubReleaseClient _releaseClient;
    private readonly Version _currentVersion;

    /// <summary>
    /// Creates a new <see cref="UpdateChecker"/>.
    /// </summary>
    /// <param name="releaseClient">The client used to fetch the latest release from GitHub.</param>
    /// <param name="currentVersion">The currently running assembly version.</param>
    public UpdateChecker(IGitHubReleaseClient releaseClient, Version currentVersion)
    {
        _releaseClient = releaseClient ?? throw new ArgumentNullException(nameof(releaseClient));
        _currentVersion = currentVersion ?? throw new ArgumentNullException(nameof(currentVersion));
    }

    /// <inheritdoc />
    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var release = await _releaseClient.GetLatestReleaseAsync(cancellationToken).ConfigureAwait(false);

            if (release is null)
            {
                return new UpdateCheckResult(false, null, null);
            }

            var latestVersion = ParseVersion(release.TagName);

            if (latestVersion is null)
            {
                return new UpdateCheckResult(false, null, null);
            }

            bool isNewer = CompareVersions(latestVersion, _currentVersion) > 0;

            if (isNewer)
            {
                var versionString = $"{latestVersion.Major}.{latestVersion.Minor}.{latestVersion.Build}";
                return new UpdateCheckResult(true, versionString, release.HtmlUrl);
            }

            return new UpdateCheckResult(false, null, null);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"UpdateChecker: failed to check for updates: {ex.Message}");
            return new UpdateCheckResult(false, null, null);
        }
    }

    /// <summary>
    /// Parses a version tag by stripping the leading 'v' (if present) and any
    /// pre-release suffix (anything after '-'), then parsing as <see cref="System.Version"/>.
    /// </summary>
    private static Version? ParseVersion(string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return null;
        }

        var versionText = tagName.TrimStart('v', 'V');

        // Strip pre-release suffix (e.g., "1.5.0-beta.1" → "1.5.0")
        var hyphenIndex = versionText.IndexOf('-');
        if (hyphenIndex >= 0)
        {
            versionText = versionText[..hyphenIndex];
        }

        if (Version.TryParse(versionText, out var version))
        {
            return version;
        }

        return null;
    }

    /// <summary>
    /// Compares two versions using only major.minor.patch (build in <see cref="Version"/> terms).
    /// Ignores the revision component.
    /// </summary>
    private static int CompareVersions(Version latest, Version current)
    {
        int majorCmp = latest.Major.CompareTo(current.Major);
        if (majorCmp != 0) return majorCmp;

        int minorCmp = latest.Minor.CompareTo(current.Minor);
        if (minorCmp != 0) return minorCmp;

        // Version.Build corresponds to the "patch" in semver
        int latestPatch = latest.Build >= 0 ? latest.Build : 0;
        int currentPatch = current.Build >= 0 ? current.Build : 0;

        return latestPatch.CompareTo(currentPatch);
    }
}
