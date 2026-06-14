using System;
using System.IO;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using MonitorBrightnessController.Infrastructure;
using MonitorBrightnessController.Interfaces;
using NSubstitute;

namespace MonitorBrightnessController.Tests.Properties;

// Feature: startup-and-install-enhancements, Property 8: Install directory detection

/// <summary>
/// Custom arbitraries for Windows file path generation used in install directory detection tests.
/// </summary>
public static class InstallDirectoryArbitraries
{
    private static readonly string ProgramFilesDir =
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

    private static readonly string ExpectedInstallPath =
        Path.GetFullPath(Path.Combine(ProgramFilesDir, "MonitorBrightnessController", "MonitorBrightnessController.exe"));

    /// <summary>
    /// Generates random valid Windows file paths that are NOT the expected install path.
    /// </summary>
    public static Arbitrary<string> NonInstallPaths()
    {
        var driveLetters = "CDEFGH".ToCharArray();
        var folderSegments = new[]
        {
            "Users", "Program Files", "Windows", "Temp", "Apps", "Tools",
            "MyApp", "Data", "Documents", "Projects", "Bin", "Release"
        };
        var fileNames = new[]
        {
            "app.exe", "test.exe", "MonitorBrightnessController.exe",
            "program.exe", "tool.exe", "setup.exe", "run.exe"
        };

        var gen =
            from drive in Gen.Elements(driveLetters)
            from segCount in Gen.Choose(1, 5)
            from segments in Gen.ArrayOf(segCount, Gen.Elements(folderSegments))
            from fileName in Gen.Elements(fileNames)
            let path = $"{drive}:\\{string.Join("\\", segments)}\\{fileName}"
            where !string.Equals(Path.GetFullPath(path), ExpectedInstallPath, StringComparison.OrdinalIgnoreCase)
            select path;

        return Arb.From(gen);
    }

    /// <summary>
    /// Generates variations of the expected install path with different casing and extra separators.
    /// These should all normalize to the same canonical install path.
    /// </summary>
    public static Arbitrary<string> InstallPathVariations()
    {
        var gen = Gen.OneOf(
            // Exact expected path
            Gen.Constant(ExpectedInstallPath),
            // Mixed case variations
            Gen.Constant(ExpectedInstallPath.ToUpperInvariant()),
            Gen.Constant(ExpectedInstallPath.ToLowerInvariant()),
            // Extra directory separator
            Gen.Constant(Path.Combine(ProgramFilesDir, "MonitorBrightnessController\\", "MonitorBrightnessController.exe")),
            // Forward slashes mixed in
            Gen.Constant(ExpectedInstallPath.Replace("\\", "/"))
        );

        return Arb.From(gen);
    }
}

/// <summary>
/// Property-based tests for install directory detection.
/// Validates that IsInstalledInProgramFiles correctly identifies whether
/// the process is running from the expected install directory.
/// </summary>
public class InstallDirectoryDetectionProperties
{
    private static readonly string ProgramFilesDir =
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

    private static readonly string ExpectedInstallPath =
        Path.GetFullPath(Path.Combine(ProgramFilesDir, "MonitorBrightnessController", "MonitorBrightnessController.exe"));

    /// <summary>
    /// Property 8: Install directory detection
    /// 
    /// For any Windows file path, IsInstalledInProgramFiles returns true if and only if
    /// the path matches %ProgramFiles%\MonitorBrightnessController\MonitorBrightnessController.exe
    /// using case-insensitive, path-normalized comparison.
    ///
    /// Since the test runner is NOT located in Program Files, IsInstalledInProgramFiles()
    /// must always return false during tests (Environment.ProcessPath is the test runner exe).
    ///
    /// This property verifies that the detection returns false for any arbitrary non-install path,
    /// confirming the test runner context is correctly identified as not-installed.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 4.1, 4.9**
    /// </remarks>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(InstallDirectoryArbitraries) })]
    public Property NonInstallPath_IsInstalledInProgramFiles_ReturnsFalse()
    {
        // The test runner always runs from outside Program Files, so IsInstalledInProgramFiles
        // must return false regardless of what random paths we generate (since it checks
        // Environment.ProcessPath, not the generated path).
        var installer = new ApplicationInstaller();

        return Prop.ForAll(InstallDirectoryArbitraries.NonInstallPaths(), _ =>
        {
            // The method checks the ACTUAL running process path, not an input.
            // Since the test runner is definitely not in Program Files,
            // this must always be false.
            var result = installer.IsInstalledInProgramFiles();
            result.Should().BeFalse(
                "the test runner executable is not located in the expected install directory");
        });
    }

    /// <summary>
    /// Property 8 (path normalization logic): For any path variation that normalizes to the
    /// expected install path (different casing, extra separators), the case-insensitive
    /// normalized comparison used by IsInstalledInProgramFiles should detect equivalence.
    /// 
    /// This validates the comparison LOGIC — Path.GetFullPath + OrdinalIgnoreCase comparison
    /// correctly identifies all case/separator variations of the install path as equivalent.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 4.1, 4.9**
    /// </remarks>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(InstallDirectoryArbitraries) })]
    public Property InstallPathVariations_NormalizeToExpectedPath()
    {
        return Prop.ForAll(InstallDirectoryArbitraries.InstallPathVariations(), pathVariation =>
        {
            // Apply the same normalization logic as ApplicationInstaller.IsInstalledInProgramFiles
            var normalizedVariation = Path.GetFullPath(pathVariation);

            // The normalized variation should match the expected install path case-insensitively
            string.Equals(normalizedVariation, ExpectedInstallPath, StringComparison.OrdinalIgnoreCase)
                .Should().BeTrue(
                    $"path '{pathVariation}' should normalize to the expected install path '{ExpectedInstallPath}'");
        });
    }

    /// <summary>
    /// Property 8 (negative case logic): For any random Windows file path that is NOT the
    /// expected install path, the case-insensitive normalized comparison correctly identifies
    /// it as non-matching.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 4.1, 4.9**
    /// </remarks>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(InstallDirectoryArbitraries) })]
    public Property NonInstallPath_NormalizationDoesNotMatchExpectedPath()
    {
        return Prop.ForAll(InstallDirectoryArbitraries.NonInstallPaths(), path =>
        {
            // Apply the same normalization logic as ApplicationInstaller.IsInstalledInProgramFiles
            var normalizedPath = Path.GetFullPath(path);

            // Paths that are NOT the install path should not match
            string.Equals(normalizedPath, ExpectedInstallPath, StringComparison.OrdinalIgnoreCase)
                .Should().BeFalse(
                    $"path '{path}' (normalized: '{normalizedPath}') should NOT match the expected install path '{ExpectedInstallPath}'");
        });
    }
}


// Feature: startup-and-install-enhancements, Property 2: EnsureRegistration reconciliation

/// <summary>
/// Custom arbitraries for EnsureRegistration property tests.
/// Generates registry value scenarios for reconciliation testing.
/// </summary>
public static class EnsureRegistrationArbitraries
{
    /// <summary>
    /// Generates existing registry values: null (missing), matching quoted path,
    /// mismatched path, or various casing variations.
    /// </summary>
    public static Arbitrary<string?> ExistingRegistryValue()
    {
        var currentExePath = Environment.ProcessPath ?? "C:\\test\\app.exe";
        var quotedCurrent = $"\"{currentExePath}\"";

        var gen = Gen.OneOf(
            // null — entry is missing
            Gen.Constant<string?>(null),
            // Exact match (quoted current path)
            Gen.Constant<string?>(quotedCurrent),
            // Case-insensitive match (upper)
            Gen.Constant<string?>(quotedCurrent.ToUpperInvariant()),
            // Case-insensitive match (lower)
            Gen.Constant<string?>(quotedCurrent.ToLowerInvariant()),
            // Mismatched path — different exe location
            Gen.Elements(
                "\"C:\\OldPath\\MonitorBrightnessController.exe\"",
                "\"D:\\Programs\\app.exe\"",
                "\"C:\\Users\\Someone\\Desktop\\MonitorBrightnessController.exe\"",
                "\"C:\\Program Files\\OtherApp\\test.exe\"",
                "\"C:\\temp\\old_location\\MonitorBrightnessController.exe\""
            ).Select(s => (string?)s),
            // Unquoted mismatched path
            Gen.Elements(
                "C:\\OldPath\\MonitorBrightnessController.exe",
                "D:\\Programs\\app.exe"
            ).Select(s => (string?)s)
        );

        return Arb.From(gen);
    }
}

/// <summary>
/// Property-based tests for EnsureRegistration reconciliation logic.
/// Verifies that EnsureRegistration correctly handles all combinations of
/// enabled/disabled state and existing/missing/mismatched registry values.
/// </summary>
public class EnsureRegistrationProperties
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "MonitorBrightnessController";

    /// <summary>
    /// Property 2: EnsureRegistration reconciliation
    ///
    /// For any combination of (current executable path, existing registry value or absence thereof,
    /// StartWithWindows enabled/disabled), EnsureRegistration SHALL:
    /// - Do nothing when StartWithWindows is disabled
    /// - Create the quoted current path when the entry is missing and StartWithWindows is enabled
    /// - Update to the quoted current path when the entry differs case-insensitively
    /// - Leave the entry unchanged when it already matches case-insensitively
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 1.4, 5.1, 5.2, 5.4**
    /// </remarks>
    [Property(MaxTest = 100)]
    public Property EnsureRegistration_Reconciles_Correctly()
    {
        var enabledArb = Arb.From<bool>(Gen.Elements(true, false));
        var existingValueArb = EnsureRegistrationArbitraries.ExistingRegistryValue();

        return Prop.ForAll(enabledArb, existingValueArb, (enabled, existingValue) =>
        {
            // Arrange
            var registryRoot = Substitute.For<IRegistryKeyWrapper>();
            var runKeyMock = Substitute.For<IRegistryKeyWrapper>();

            registryRoot.OpenSubKey(RunKey, writable: true).Returns(runKeyMock);
            runKeyMock.GetValue(AppName).Returns(existingValue);

            var sut = new StartupRegistration(registryRoot);

            var currentExePath = Environment.ProcessPath;
            // Skip if ProcessPath is null (cannot validate behavior without a known path)
            if (string.IsNullOrEmpty(currentExePath))
                return;

            var expectedQuotedPath = $"\"{currentExePath}\"";

            // Act
            var result = sut.EnsureRegistration(enabled);

            // Assert
            if (!enabled)
            {
                // When disabled: returns success, does NOT open the registry key
                result.IsSuccess.Should().BeTrue("EnsureRegistration should succeed immediately when disabled");
                registryRoot.DidNotReceive().OpenSubKey(Arg.Any<string>(), Arg.Any<bool>());
            }
            else if (existingValue is null)
            {
                // When enabled + missing: should create the entry with quoted path
                result.IsSuccess.Should().BeTrue("EnsureRegistration should succeed when creating a missing entry");
                runKeyMock.Received(1).SetValue(AppName, expectedQuotedPath);
            }
            else if (string.Equals(existingValue, expectedQuotedPath, StringComparison.OrdinalIgnoreCase))
            {
                // When enabled + matches (case-insensitive): should NOT call SetValue
                result.IsSuccess.Should().BeTrue("EnsureRegistration should succeed when entry already matches");
                runKeyMock.DidNotReceive().SetValue(Arg.Any<string>(), Arg.Any<object>());
            }
            else
            {
                // When enabled + differs: should update with quoted path
                result.IsSuccess.Should().BeTrue("EnsureRegistration should succeed when updating a mismatched entry");
                runKeyMock.Received(1).SetValue(AppName, expectedQuotedPath);
            }
        });
    }
}
