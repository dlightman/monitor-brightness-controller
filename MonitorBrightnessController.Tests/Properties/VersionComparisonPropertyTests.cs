using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using NSubstitute;
using MonitorBrightnessController.Application;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;

namespace MonitorBrightnessController.Tests.Properties;

// Feature: enhancements-v1-4, Property 5: Semantic version comparison

/// <summary>
/// Property-based tests verifying that the UpdateChecker compares versions using only
/// numeric major.minor.patch components and ignores pre-release suffixes.
/// </summary>
public class VersionComparisonPropertyTests
{
    private static readonly string[] PreReleaseSuffixes = new[]
    {
        "", "-alpha", "-beta", "-beta.1", "-beta.2", "-rc.1", "-rc.2",
        "-alpha.3", "-preview", "-dev.42"
    };

    /// <summary>
    /// Property 5: For any two version triples (major.minor.patch) with optional pre-release
    /// suffixes, the UpdateChecker determines ordering based solely on numeric components,
    /// ignoring pre-release suffixes. A higher major, minor, or patch value is always
    /// considered newer.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 5.2, 5.6**
    /// </remarks>
    [Property(MaxTest = 100)]
    public Property VersionComparison_IgnoresPreReleaseSuffix_UsesOnlyNumericComponents()
    {
        var versionComponentGen = Gen.Choose(0, 99);
        var suffixGen = Gen.Elements(PreReleaseSuffixes);

        var testCaseGen =
            from currentMajor in versionComponentGen
            from currentMinor in versionComponentGen
            from currentPatch in versionComponentGen
            from latestMajor in versionComponentGen
            from latestMinor in versionComponentGen
            from latestPatch in versionComponentGen
            from suffix in suffixGen
            select (
                CurrentMajor: currentMajor, CurrentMinor: currentMinor, CurrentPatch: currentPatch,
                LatestMajor: latestMajor, LatestMinor: latestMinor, LatestPatch: latestPatch,
                Suffix: suffix
            );

        return Prop.ForAll(Arb.From(testCaseGen), testCase =>
        {
            var currentVersion = new Version(testCase.CurrentMajor, testCase.CurrentMinor, testCase.CurrentPatch);

            // Build a tag with optional prefix 'v' and optional pre-release suffix
            var tagName = $"v{testCase.LatestMajor}.{testCase.LatestMinor}.{testCase.LatestPatch}{testCase.Suffix}";
            var releaseUrl = "https://github.com/dlightman/monitor-brightness-controller/releases/latest";

            var mockClient = Substitute.For<IGitHubReleaseClient>();
            mockClient.GetLatestReleaseAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<GitHubRelease?>(new GitHubRelease(tagName, releaseUrl)));

            var checker = new UpdateChecker(mockClient, currentVersion);
            var result = checker.CheckForUpdateAsync(CancellationToken.None).GetAwaiter().GetResult();

            // Determine expected comparison based solely on numeric components
            bool expectedNewer = IsNewer(
                testCase.LatestMajor, testCase.LatestMinor, testCase.LatestPatch,
                testCase.CurrentMajor, testCase.CurrentMinor, testCase.CurrentPatch);

            result.IsUpdateAvailable.Should().Be(expectedNewer,
                $"tag '{tagName}' vs current {currentVersion}: " +
                $"update should {(expectedNewer ? "" : "not ")}be available based on numeric components only");
        });
    }

    /// <summary>
    /// Property 5 (supplemental): Adding any pre-release suffix to a version tag should not
    /// change the comparison outcome compared to the same version without the suffix.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 5.2, 5.6**
    /// </remarks>
    [Property(MaxTest = 100)]
    public Property VersionComparison_SameSemver_WithAndWithoutSuffix_ProducesSameResult()
    {
        var versionComponentGen = Gen.Choose(0, 99);
        var nonEmptySuffixGen = Gen.Elements(
            "-alpha", "-beta", "-beta.1", "-beta.2", "-rc.1", "-rc.2",
            "-alpha.3", "-preview", "-dev.42");

        var testCaseGen =
            from currentMajor in versionComponentGen
            from currentMinor in versionComponentGen
            from currentPatch in versionComponentGen
            from latestMajor in versionComponentGen
            from latestMinor in versionComponentGen
            from latestPatch in versionComponentGen
            from suffix in nonEmptySuffixGen
            select (
                CurrentMajor: currentMajor, CurrentMinor: currentMinor, CurrentPatch: currentPatch,
                LatestMajor: latestMajor, LatestMinor: latestMinor, LatestPatch: latestPatch,
                Suffix: suffix
            );

        return Prop.ForAll(Arb.From(testCaseGen), testCase =>
        {
            var currentVersion = new Version(testCase.CurrentMajor, testCase.CurrentMinor, testCase.CurrentPatch);
            var releaseUrl = "https://github.com/dlightman/monitor-brightness-controller/releases/latest";

            // Check with suffix
            var tagWithSuffix = $"v{testCase.LatestMajor}.{testCase.LatestMinor}.{testCase.LatestPatch}{testCase.Suffix}";
            var mockWithSuffix = Substitute.For<IGitHubReleaseClient>();
            mockWithSuffix.GetLatestReleaseAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<GitHubRelease?>(new GitHubRelease(tagWithSuffix, releaseUrl)));
            var checkerWithSuffix = new UpdateChecker(mockWithSuffix, currentVersion);
            var resultWithSuffix = checkerWithSuffix.CheckForUpdateAsync(CancellationToken.None).GetAwaiter().GetResult();

            // Check without suffix
            var tagWithoutSuffix = $"v{testCase.LatestMajor}.{testCase.LatestMinor}.{testCase.LatestPatch}";
            var mockWithoutSuffix = Substitute.For<IGitHubReleaseClient>();
            mockWithoutSuffix.GetLatestReleaseAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<GitHubRelease?>(new GitHubRelease(tagWithoutSuffix, releaseUrl)));
            var checkerWithoutSuffix = new UpdateChecker(mockWithoutSuffix, currentVersion);
            var resultWithoutSuffix = checkerWithoutSuffix.CheckForUpdateAsync(CancellationToken.None).GetAwaiter().GetResult();

            resultWithSuffix.IsUpdateAvailable.Should().Be(resultWithoutSuffix.IsUpdateAvailable,
                $"pre-release suffix '{testCase.Suffix}' should not affect comparison outcome " +
                $"(tag '{tagWithSuffix}' vs '{tagWithoutSuffix}' against current {currentVersion})");
        });
    }

    /// <summary>
    /// Determines if (latestMajor.latestMinor.latestPatch) is strictly greater than
    /// (currentMajor.currentMinor.currentPatch) using semver ordering rules.
    /// </summary>
    private static bool IsNewer(int latestMajor, int latestMinor, int latestPatch,
                                int currentMajor, int currentMinor, int currentPatch)
    {
        if (latestMajor != currentMajor) return latestMajor > currentMajor;
        if (latestMinor != currentMinor) return latestMinor > currentMinor;
        return latestPatch > currentPatch;
    }
}
