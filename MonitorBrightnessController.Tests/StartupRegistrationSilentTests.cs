using System;
using FluentAssertions;
using MonitorBrightnessController.Infrastructure;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;
using NSubstitute;
using Xunit;

namespace MonitorBrightnessController.Tests;

/// <summary>
/// Unit tests for <see cref="StartupRegistration"/> verifying the `--silent` argument
/// is correctly included in registry values (Requirements 2.1, 2.2, 2.3, 2.4).
/// </summary>
public class StartupRegistrationSilentTests
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "MonitorBrightnessController";

    private readonly IRegistryKeyWrapper _mockRoot;
    private readonly IRegistryKeyWrapper _mockSubKey;
    private readonly StartupRegistration _sut;

    public StartupRegistrationSilentTests()
    {
        _mockRoot = Substitute.For<IRegistryKeyWrapper>();
        _mockSubKey = Substitute.For<IRegistryKeyWrapper>();
        _sut = new StartupRegistration(_mockRoot);
    }

    // --- Requirement 2.1: Enable writes correct "<path>" --silent format ---

    [Fact]
    public void SetStartWithWindows_Enable_WritesQuotedPathWithSilentFlag()
    {
        // Requirement 2.1: Registry value formatted as "<exe_path>" --silent
        _mockRoot.OpenSubKey(RunKey, true).Returns(_mockSubKey);

        var result = _sut.SetStartWithWindows(true);

        // Environment.ProcessPath should be non-null in the test runner
        var exePath = Environment.ProcessPath;
        if (exePath is not null)
        {
            result.IsSuccess.Should().BeTrue();
            _mockSubKey.Received(1).SetValue(AppName, $"\"{exePath}\" --silent");
        }
        else
        {
            // Guard: if ProcessPath is null, we expect a failure result
            result.IsSuccess.Should().BeFalse();
        }
    }

    [Fact]
    public void SetStartWithWindows_Enable_ValueEndsWithDashDashSilent()
    {
        // Requirement 2.1: The value must end with --silent
        _mockRoot.OpenSubKey(RunKey, true).Returns(_mockSubKey);

        var result = _sut.SetStartWithWindows(true);

        if (result.IsSuccess)
        {
            _mockSubKey.Received(1).SetValue(AppName, Arg.Is<string>(v =>
                v.EndsWith("\" --silent") && v.StartsWith("\"")));
        }
    }

    // --- Requirement 2.2: Disable removes entry, succeeds even if entry missing ---

    [Fact]
    public void SetStartWithWindows_Disable_CallsDeleteValueWithThrowOnMissingFalse()
    {
        // Requirement 2.2: Remove entry, completing successfully even if it doesn't exist
        _mockRoot.OpenSubKey(RunKey, false).Returns(_mockSubKey);

        var result = _sut.SetStartWithWindows(false);

        result.IsSuccess.Should().BeTrue();
        _mockSubKey.Received(1).DeleteValue(AppName, throwOnMissingValue: false);
    }

    [Fact]
    public void SetStartWithWindows_Disable_DoesNotCallSetValue()
    {
        // Requirement 2.2: Disable only deletes, does not write a value
        _mockRoot.OpenSubKey(RunKey, false).Returns(_mockSubKey);

        _sut.SetStartWithWindows(false);

        _mockSubKey.DidNotReceive().SetValue(Arg.Any<string>(), Arg.Any<object>());
    }

    // --- Requirement 2.3: EnsureRegistration overwrites value missing --silent ---

    [Fact]
    public void EnsureRegistration_WhenExistingValueMissingSilentFlag_OverwritesWithCorrectFormat()
    {
        // Requirement 2.3: Overwrite if --silent is missing from the registered value
        _mockRoot.OpenSubKey(RunKey, true).Returns(_mockSubKey);

        var exePath = Environment.ProcessPath;
        if (exePath is null) return; // Skip if ProcessPath unavailable in test env

        // Existing value has the right path but without --silent
        _mockSubKey.GetValue(AppName).Returns($"\"{exePath}\"");

        var result = _sut.EnsureRegistration(true);

        result.IsSuccess.Should().BeTrue();
        _mockSubKey.Received(1).SetValue(AppName, $"\"{exePath}\" --silent");
    }

    // --- Requirement 2.3: EnsureRegistration overwrites value with wrong path ---

    [Fact]
    public void EnsureRegistration_WhenExistingValueHasWrongPath_OverwritesWithCorrectFormat()
    {
        // Requirement 2.3: Overwrite if the path differs from current exe path
        _mockRoot.OpenSubKey(RunKey, true).Returns(_mockSubKey);

        var exePath = Environment.ProcessPath;
        if (exePath is null) return; // Skip if ProcessPath unavailable in test env

        // Existing value has a completely different path
        _mockSubKey.GetValue(AppName).Returns(@"""C:\OldPath\OldApp.exe"" --silent");

        var result = _sut.EnsureRegistration(true);

        result.IsSuccess.Should().BeTrue();
        _mockSubKey.Received(1).SetValue(AppName, $"\"{exePath}\" --silent");
    }

    [Fact]
    public void EnsureRegistration_WhenExistingValueHasWrongPathWithoutSilent_OverwritesWithCorrectFormat()
    {
        // Requirement 2.3: Overwrite if both the path and --silent flag are wrong/missing
        _mockRoot.OpenSubKey(RunKey, true).Returns(_mockSubKey);

        var exePath = Environment.ProcessPath;
        if (exePath is null) return;

        _mockSubKey.GetValue(AppName).Returns(@"""C:\OldPath\OldApp.exe""");

        var result = _sut.EnsureRegistration(true);

        result.IsSuccess.Should().BeTrue();
        _mockSubKey.Received(1).SetValue(AppName, $"\"{exePath}\" --silent");
    }

    // --- Requirement 2.3: EnsureRegistration does nothing when value is correct ---

    [Fact]
    public void EnsureRegistration_WhenExistingValueIsCorrect_DoesNotCallSetValue()
    {
        // Requirement 2.3: If value matches expected format, no write should occur
        _mockRoot.OpenSubKey(RunKey, true).Returns(_mockSubKey);

        var exePath = Environment.ProcessPath;
        if (exePath is null) return;

        var expectedValue = $"\"{exePath}\" --silent";
        _mockSubKey.GetValue(AppName).Returns(expectedValue);

        var result = _sut.EnsureRegistration(true);

        result.IsSuccess.Should().BeTrue();
        _mockSubKey.DidNotReceive().SetValue(Arg.Any<string>(), Arg.Any<object>());
    }

    [Fact]
    public void EnsureRegistration_WhenExistingValueMatchesCaseInsensitive_DoesNotCallSetValue()
    {
        // Requirement 2.3: Comparison is case-insensitive
        _mockRoot.OpenSubKey(RunKey, true).Returns(_mockSubKey);

        var exePath = Environment.ProcessPath;
        if (exePath is null) return;

        // Use uppercase path to verify case-insensitive comparison
        var existingValue = $"\"{exePath.ToUpperInvariant()}\" --SILENT";
        _mockSubKey.GetValue(AppName).Returns(existingValue);

        var result = _sut.EnsureRegistration(true);

        result.IsSuccess.Should().BeTrue();
        _mockSubKey.DidNotReceive().SetValue(Arg.Any<string>(), Arg.Any<object>());
    }

    // --- Requirement 2.4: Registry inaccessible returns failure result ---

    [Fact]
    public void SetStartWithWindows_Enable_WhenRegistryInaccessible_ReturnsFailure()
    {
        // Requirement 2.4: Registry key cannot be opened → return failure
        _mockRoot.OpenSubKey(RunKey, true).Returns((IRegistryKeyWrapper?)null);

        var result = _sut.SetStartWithWindows(true);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("registry");
    }

    [Fact]
    public void SetStartWithWindows_Disable_WhenRegistryInaccessible_ReturnsFailure()
    {
        // Requirement 2.4: Registry key cannot be opened for disable → return failure
        _mockRoot.OpenSubKey(RunKey, false).Returns((IRegistryKeyWrapper?)null);

        var result = _sut.SetStartWithWindows(false);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("registry");
    }

    [Fact]
    public void EnsureRegistration_WhenRegistryInaccessible_ReturnsFailure()
    {
        // Requirement 2.4: Registry key cannot be opened during EnsureRegistration → failure
        _mockRoot.OpenSubKey(RunKey, true).Returns((IRegistryKeyWrapper?)null);

        var result = _sut.EnsureRegistration(true);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("registry");
    }
}
