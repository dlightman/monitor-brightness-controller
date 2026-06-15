using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MonitorBrightnessController.Application;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MonitorBrightnessController.Tests;

/// <summary>
/// Unit tests for <see cref="UpdateChecker"/> verifying version comparison logic,
/// error handling for network failures, timeouts, and invalid responses.
/// Uses NSubstitute to mock <see cref="IGitHubReleaseClient"/>.
/// </summary>
public class UpdateCheckerTests
{
    private readonly IGitHubReleaseClient _releaseClient;

    public UpdateCheckerTests()
    {
        _releaseClient = Substitute.For<IGitHubReleaseClient>();
    }

    /// <summary>
    /// When the server reports a version newer than the current version,
    /// the result should indicate an update is available with the correct version and URL.
    /// Validates: Requirements 5.1, 5.2
    /// </summary>
    [Fact]
    public async Task CheckForUpdateAsync_NewerVersionAvailable_ReturnsUpdateAvailable()
    {
        var currentVersion = new Version(1, 3, 0);
        var release = new GitHubRelease("v1.4.0", "https://github.com/dlightman/monitor-brightness-controller/releases/tag/v1.4.0");
        _releaseClient.GetLatestReleaseAsync(Arg.Any<CancellationToken>()).Returns(release);

        var checker = new UpdateChecker(_releaseClient, currentVersion);
        var result = await checker.CheckForUpdateAsync();

        result.IsUpdateAvailable.Should().BeTrue();
        result.LatestVersion.Should().Be("1.4.0");
        result.ReleaseUrl.Should().Be("https://github.com/dlightman/monitor-brightness-controller/releases/tag/v1.4.0");
    }

    /// <summary>
    /// When the server reports the same version as the current version,
    /// the result should indicate no update is available.
    /// Validates: Requirements 5.2
    /// </summary>
    [Fact]
    public async Task CheckForUpdateAsync_SameVersion_ReturnsNoUpdate()
    {
        var currentVersion = new Version(1, 4, 0);
        var release = new GitHubRelease("v1.4.0", "https://github.com/example/releases/tag/v1.4.0");
        _releaseClient.GetLatestReleaseAsync(Arg.Any<CancellationToken>()).Returns(release);

        var checker = new UpdateChecker(_releaseClient, currentVersion);
        var result = await checker.CheckForUpdateAsync();

        result.IsUpdateAvailable.Should().BeFalse();
        result.LatestVersion.Should().BeNull();
        result.ReleaseUrl.Should().BeNull();
    }

    /// <summary>
    /// When the server reports an older version than the current version,
    /// the result should indicate no update is available.
    /// Validates: Requirements 5.2
    /// </summary>
    [Fact]
    public async Task CheckForUpdateAsync_OlderVersionOnServer_ReturnsNoUpdate()
    {
        var currentVersion = new Version(2, 0, 0);
        var release = new GitHubRelease("v1.4.0", "https://github.com/example/releases/tag/v1.4.0");
        _releaseClient.GetLatestReleaseAsync(Arg.Any<CancellationToken>()).Returns(release);

        var checker = new UpdateChecker(_releaseClient, currentVersion);
        var result = await checker.CheckForUpdateAsync();

        result.IsUpdateAvailable.Should().BeFalse();
        result.LatestVersion.Should().BeNull();
        result.ReleaseUrl.Should().BeNull();
    }

    /// <summary>
    /// When the release client throws an exception (network failure),
    /// the checker should gracefully return no update available.
    /// Validates: Requirements 5.5
    /// </summary>
    [Fact]
    public async Task CheckForUpdateAsync_NetworkFailure_ReturnsNoUpdateGracefully()
    {
        var currentVersion = new Version(1, 3, 0);
        _releaseClient.GetLatestReleaseAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network unreachable"));

        var checker = new UpdateChecker(_releaseClient, currentVersion);
        var result = await checker.CheckForUpdateAsync();

        result.IsUpdateAvailable.Should().BeFalse();
        result.LatestVersion.Should().BeNull();
        result.ReleaseUrl.Should().BeNull();
    }

    /// <summary>
    /// When the release client throws a TaskCanceledException (simulating a timeout > 10s),
    /// the checker should gracefully return no update available.
    /// Validates: Requirements 5.5
    /// </summary>
    [Fact]
    public async Task CheckForUpdateAsync_Timeout_ReturnsNoUpdateGracefully()
    {
        var currentVersion = new Version(1, 3, 0);
        _releaseClient.GetLatestReleaseAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("The request was canceled due to timeout."));

        var checker = new UpdateChecker(_releaseClient, currentVersion);
        var result = await checker.CheckForUpdateAsync();

        result.IsUpdateAvailable.Should().BeFalse();
        result.LatestVersion.Should().BeNull();
        result.ReleaseUrl.Should().BeNull();
    }

    /// <summary>
    /// When the release client returns a release with an unparseable tag name (invalid JSON response scenario),
    /// the checker should gracefully return no update available.
    /// Validates: Requirements 5.5
    /// </summary>
    [Fact]
    public async Task CheckForUpdateAsync_InvalidTagName_ReturnsNoUpdateGracefully()
    {
        var currentVersion = new Version(1, 3, 0);
        var release = new GitHubRelease("not-a-version", "https://github.com/example/releases");
        _releaseClient.GetLatestReleaseAsync(Arg.Any<CancellationToken>()).Returns(release);

        var checker = new UpdateChecker(_releaseClient, currentVersion);
        var result = await checker.CheckForUpdateAsync();

        result.IsUpdateAvailable.Should().BeFalse();
        result.LatestVersion.Should().BeNull();
        result.ReleaseUrl.Should().BeNull();
    }

    /// <summary>
    /// When the release client returns null (could not retrieve release),
    /// the checker should gracefully return no update available.
    /// Validates: Requirements 5.5
    /// </summary>
    [Fact]
    public async Task CheckForUpdateAsync_NullRelease_ReturnsNoUpdateGracefully()
    {
        var currentVersion = new Version(1, 3, 0);
        _releaseClient.GetLatestReleaseAsync(Arg.Any<CancellationToken>()).Returns((GitHubRelease?)null);

        var checker = new UpdateChecker(_releaseClient, currentVersion);
        var result = await checker.CheckForUpdateAsync();

        result.IsUpdateAvailable.Should().BeFalse();
        result.LatestVersion.Should().BeNull();
        result.ReleaseUrl.Should().BeNull();
    }

    /// <summary>
    /// When the tag name has a 'v' prefix, it should be stripped correctly
    /// and the version compared numerically.
    /// Validates: Requirements 5.6
    /// </summary>
    [Fact]
    public async Task CheckForUpdateAsync_VersionWithVPrefix_StrippedCorrectly()
    {
        var currentVersion = new Version(1, 3, 0);
        var release = new GitHubRelease("v1.5.0", "https://github.com/example/releases/tag/v1.5.0");
        _releaseClient.GetLatestReleaseAsync(Arg.Any<CancellationToken>()).Returns(release);

        var checker = new UpdateChecker(_releaseClient, currentVersion);
        var result = await checker.CheckForUpdateAsync();

        result.IsUpdateAvailable.Should().BeTrue();
        result.LatestVersion.Should().Be("1.5.0");
    }

    /// <summary>
    /// When the tag name has a 'V' uppercase prefix, it should also be stripped correctly.
    /// Validates: Requirements 5.6
    /// </summary>
    [Fact]
    public async Task CheckForUpdateAsync_VersionWithUppercaseVPrefix_StrippedCorrectly()
    {
        var currentVersion = new Version(1, 3, 0);
        var release = new GitHubRelease("V2.0.0", "https://github.com/example/releases/tag/V2.0.0");
        _releaseClient.GetLatestReleaseAsync(Arg.Any<CancellationToken>()).Returns(release);

        var checker = new UpdateChecker(_releaseClient, currentVersion);
        var result = await checker.CheckForUpdateAsync();

        result.IsUpdateAvailable.Should().BeTrue();
        result.LatestVersion.Should().Be("2.0.0");
    }

    /// <summary>
    /// When the tag name has a pre-release suffix (e.g., "-beta.1"), it should be stripped
    /// and only the numeric major.minor.patch compared.
    /// Validates: Requirements 5.6, 5.7
    /// </summary>
    [Fact]
    public async Task CheckForUpdateAsync_VersionWithPreReleaseSuffix_SuffixIgnored()
    {
        var currentVersion = new Version(1, 3, 0);
        var release = new GitHubRelease("v1.4.0-beta.1", "https://github.com/example/releases/tag/v1.4.0-beta.1");
        _releaseClient.GetLatestReleaseAsync(Arg.Any<CancellationToken>()).Returns(release);

        var checker = new UpdateChecker(_releaseClient, currentVersion);
        var result = await checker.CheckForUpdateAsync();

        result.IsUpdateAvailable.Should().BeTrue();
        result.LatestVersion.Should().Be("1.4.0");
    }

    /// <summary>
    /// When the tag name has a pre-release suffix and the numeric version equals the current version,
    /// no update should be reported (pre-release suffixes are ignored, not treated as newer).
    /// Validates: Requirements 5.6
    /// </summary>
    [Fact]
    public async Task CheckForUpdateAsync_SameVersionWithPreReleaseSuffix_ReturnsNoUpdate()
    {
        var currentVersion = new Version(1, 4, 0);
        var release = new GitHubRelease("v1.4.0-rc.2", "https://github.com/example/releases/tag/v1.4.0-rc.2");
        _releaseClient.GetLatestReleaseAsync(Arg.Any<CancellationToken>()).Returns(release);

        var checker = new UpdateChecker(_releaseClient, currentVersion);
        var result = await checker.CheckForUpdateAsync();

        result.IsUpdateAvailable.Should().BeFalse();
    }
}
