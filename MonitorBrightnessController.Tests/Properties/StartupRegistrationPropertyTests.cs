using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using MonitorBrightnessController.Infrastructure;
using MonitorBrightnessController.Interfaces;
using NSubstitute;

namespace MonitorBrightnessController.Tests.Properties;

// Feature: enhancements-v1-4, Property 2: Startup registration value format

/// <summary>
/// Generators for producing valid Windows executable paths for startup registration testing.
/// </summary>
public static class StartupRegistrationPathArbitraries
{
    private static readonly char[] DriveLetters = "CDEFGH".ToCharArray();

    private static readonly string[] FolderSegments = new[]
    {
        "Program Files", "Program Files (x86)", "Users", "Apps", "Tools",
        "MonitorBrightnessController", "MyApp", "Utilities", "Bin", "Release",
        "Debug", "Windows", "Documents", "Projects", "Software"
    };

    private static readonly string[] ExeNames = new[]
    {
        "MonitorBrightnessController.exe", "app.exe", "test.exe",
        "program.exe", "MyTool.exe", "setup.exe", "launcher.exe"
    };

    /// <summary>
    /// Generates random valid Windows executable paths with 1-5 folder segments.
    /// </summary>
    public static Arbitrary<string> ValidWindowsExePaths()
    {
        var gen =
            from drive in Gen.Elements(DriveLetters)
            from segCount in Gen.Choose(1, 5)
            from segments in Gen.ArrayOf(segCount, Gen.Elements(FolderSegments))
            from exeName in Gen.Elements(ExeNames)
            select $"{drive}:\\{string.Join("\\", segments)}\\{exeName}";

        return Arb.From(gen);
    }
}

/// <summary>
/// Property-based tests for startup registration value format.
/// Verifies that UpdateRegisteredPath writes the correct format to the registry.
/// </summary>
public class StartupRegistrationPropertyTests
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "MonitorBrightnessController";

    // -------------------------------------------------------------------------
    // Property 2: Startup registration value format
    // -------------------------------------------------------------------------

    /// <summary>
    /// Property 2: For any valid Windows executable path, calling UpdateRegisteredPath
    /// shall produce a registry value exactly equal to the quoted path followed by a space
    /// and the literal string --silent (i.e., "&lt;path&gt;" --silent).
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 2.1**
    /// </remarks>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(StartupRegistrationPathArbitraries) })]
    public Property UpdateRegisteredPath_Writes_QuotedPath_With_Silent(string exePath)
    {
        // Arrange
        var registryRoot = Substitute.For<IRegistryKeyWrapper>();
        var runKeyMock = Substitute.For<IRegistryKeyWrapper>();
        registryRoot.OpenSubKey(RunKey, writable: true).Returns(runKeyMock);

        var sut = new StartupRegistration(registryRoot);

        // Capture the value written to registry
        object? capturedValue = null;
        runKeyMock.When(x => x.SetValue(AppName, Arg.Any<object>()))
            .Do(callInfo => capturedValue = callInfo.ArgAt<object>(1));

        // Act
        var result = sut.UpdateRegisteredPath(exePath);

        // Assert
        var expectedValue = $"\"{exePath}\" --silent";

        return (result.IsSuccess &&
                capturedValue is string written &&
                written == expectedValue)
            .Label($"Expected registry value '{expectedValue}', got '{capturedValue}'")
            .And(
                (capturedValue is string s && s.StartsWith("\"") && s.EndsWith(" --silent", StringComparison.Ordinal))
                .Label("Value must start with quote and end with ' --silent'"));
    }

    /// <summary>
    /// Property 2 (format structure): For any valid Windows executable path, the registry
    /// value written by UpdateRegisteredPath starts with a double quote, ends with ' --silent',
    /// and the path within the quotes matches the input path exactly.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 2.1**
    /// </remarks>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(StartupRegistrationPathArbitraries) })]
    public void UpdateRegisteredPath_ValueFormat_Structure(string exePath)
    {
        // Arrange
        var registryRoot = Substitute.For<IRegistryKeyWrapper>();
        var runKeyMock = Substitute.For<IRegistryKeyWrapper>();
        registryRoot.OpenSubKey(RunKey, writable: true).Returns(runKeyMock);

        var sut = new StartupRegistration(registryRoot);

        // Act
        var result = sut.UpdateRegisteredPath(exePath);

        // Assert
        result.IsSuccess.Should().BeTrue("UpdateRegisteredPath should succeed when registry key is accessible");

        runKeyMock.Received(1).SetValue(AppName, Arg.Is<string>(value =>
            value.StartsWith("\"", StringComparison.Ordinal) &&
            value.EndsWith(" --silent", StringComparison.Ordinal) &&
            value == $"\"{exePath}\" --silent"));
    }

    // -------------------------------------------------------------------------
    // Property 3: EnsureRegistration corrects mismatched values
    // -------------------------------------------------------------------------

    /// <summary>
    /// Property 3: For any existing registry value that does not equal
    /// "&lt;current_exe_path&gt;" --silent (case-insensitive), calling EnsureRegistration(true)
    /// shall overwrite the registry value with the correct format.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 2.3**
    /// </remarks>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(MismatchedRegistryValueArbitraries) })]
    public void EnsureRegistration_Corrects_Mismatched_RegistryValue(string mismatchedValue)
    {
        // Arrange
        var currentExePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExePath))
        {
            // Cannot run this test if ProcessPath is unavailable
            return;
        }

        var expectedValue = $"\"{currentExePath}\" --silent";

        // Ensure the generated value is actually mismatched (case-insensitive)
        if (string.Equals(mismatchedValue, expectedValue, StringComparison.OrdinalIgnoreCase))
        {
            // Skip this case - it's not actually mismatched
            return;
        }

        var registryRoot = Substitute.For<IRegistryKeyWrapper>();
        var runKeyMock = Substitute.For<IRegistryKeyWrapper>();
        registryRoot.OpenSubKey(RunKey, writable: true).Returns(runKeyMock);
        runKeyMock.GetValue(AppName).Returns(mismatchedValue);

        var sut = new StartupRegistration(registryRoot);

        // Act
        var result = sut.EnsureRegistration(true);

        // Assert
        result.IsSuccess.Should().BeTrue("EnsureRegistration should succeed when registry key is accessible");

        runKeyMock.Received(1).SetValue(AppName, expectedValue);
    }
}

// -------------------------------------------------------------------------
// Generators for mismatched registry values (Property 3)
// -------------------------------------------------------------------------

/// <summary>
/// Generators for producing registry values that do NOT match the expected format.
/// </summary>
public static class MismatchedRegistryValueArbitraries
{
    private static readonly string[] RandomPaths = new[]
    {
        @"C:\Program Files\SomeApp\app.exe",
        @"D:\Tools\old.exe",
        @"C:\Windows\System32\notepad.exe",
        @"E:\MyApps\MonitorBrightnessController\v1.2\app.exe",
        @"C:\Users\User\Desktop\test.exe"
    };

    private static readonly string[] Suffixes = new[]
    {
        "", " --quiet", " --hidden", " --minimized", " --start",
        " --verbose", " /silent", " -s"
    };

    /// <summary>
    /// Generates random registry values that are guaranteed to NOT match
    /// the expected format of "&lt;current_exe_path&gt;" --silent.
    /// Produces values like: wrong paths, missing --silent, missing quotes,
    /// completely random strings, null-like empty values.
    /// </summary>
    public static Arbitrary<string> MismatchedRegistryValues()
    {
        var currentExePath = Environment.ProcessPath ?? "unknown";
        var correctValue = $"\"{currentExePath}\" --silent";

        // Strategy 1: Random path with quotes but wrong --silent suffix
        var wrongSuffix =
            from path in Gen.Elements(RandomPaths)
            from suffix in Gen.Elements(Suffixes)
            select $"\"{path}\"{suffix}";

        // Strategy 2: Correct path but missing --silent
        var missingFlag = Gen.Constant($"\"{currentExePath}\"");

        // Strategy 3: Correct path but wrong flag
        var wrongFlag =
            from flag in Gen.Elements(" --quiet", " --hidden", " --start", " /silent", " -s", "")
            select $"\"{currentExePath}\"{flag}";

        // Strategy 4: Unquoted path with --silent
        var unquotedPath =
            from path in Gen.Elements(RandomPaths)
            select $"{path} --silent";

        // Strategy 5: Completely random strings
        var randomStrings =
            from len in Gen.Choose(1, 50)
            from chars in Gen.ArrayOf(len, Gen.Elements(
                "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 :\\-./\"".ToCharArray()))
            select new string(chars);

        // Strategy 6: Old-style registration without --silent (just quoted path)
        var oldStylePaths =
            from path in Gen.Elements(RandomPaths)
            select $"\"{path}\"";

        // Combine all strategies
        var combined = Gen.OneOf(wrongSuffix, missingFlag, wrongFlag, unquotedPath, randomStrings, oldStylePaths);

        // Filter out values that accidentally match the correct value (case-insensitive)
        var filtered = combined.Where(v => !string.Equals(v, correctValue, StringComparison.OrdinalIgnoreCase));

        return Arb.From(filtered);
    }
}
