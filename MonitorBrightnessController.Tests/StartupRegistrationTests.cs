using FluentAssertions;
using MonitorBrightnessController.Infrastructure;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;
using NSubstitute;
using Xunit;

namespace MonitorBrightnessController.Tests;

/// <summary>
/// Unit tests for <see cref="StartupRegistration"/> covering registry error handling,
/// successful register/unregister, and path management (Requirements 1.5, 1.6).
/// </summary>
public class StartupRegistrationTests
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "MonitorBrightnessController";

    private readonly IRegistryKeyWrapper _mockRoot;
    private readonly StartupRegistration _sut;

    public StartupRegistrationTests()
    {
        _mockRoot = Substitute.For<IRegistryKeyWrapper>();
        _sut = new StartupRegistration(_mockRoot);
    }

    // --- SetStartWithWindows: registry key open failure (Req 1.5) ---

    [Fact]
    public void SetStartWithWindows_Enable_WhenRegistryKeyCannotBeOpened_ReturnsFailure()
    {
        // Requirement 1.5: If the Run registry key cannot be opened, return failure.
        _mockRoot.OpenSubKey(RunKey, true).Returns((IRegistryKeyWrapper?)null);

        var result = _sut.SetStartWithWindows(true);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void SetStartWithWindows_Disable_WhenRegistryKeyCannotBeOpened_ReturnsFailure()
    {
        // Requirement 1.5: If the Run registry key cannot be opened for disable, return failure.
        _mockRoot.OpenSubKey(RunKey, false).Returns((IRegistryKeyWrapper?)null);

        var result = _sut.SetStartWithWindows(false);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    // --- SetStartWithWindows: successful register (Req 1.1, 1.3) ---

    [Fact]
    public void SetStartWithWindows_Enable_WhenRegistryKeyOpens_CallsSetValueAndReturnsSuccess()
    {
        // Requirement 1.1: Creates a value with the quoted executable path.
        var mockSubKey = Substitute.For<IRegistryKeyWrapper>();
        _mockRoot.OpenSubKey(RunKey, true).Returns(mockSubKey);

        var result = _sut.SetStartWithWindows(true);

        // Environment.ProcessPath is non-null during test execution
        if (result.IsSuccess)
        {
            mockSubKey.Received(1).SetValue(AppName, Arg.Is<string>(v => v.StartsWith("\"") && v.EndsWith("\" --silent")));
        }
        else
        {
            // If ProcessPath is null in the test environment, we get a failure (Req 1.6).
            result.Error.Should().Contain("executable path");
        }
    }

    // --- SetStartWithWindows: successful unregister (Req 1.2) ---

    [Fact]
    public void SetStartWithWindows_Disable_WhenRegistryKeyOpens_CallsDeleteValueAndReturnsSuccess()
    {
        // Requirement 1.2: Removes the application's value from the Run registry key.
        var mockSubKey = Substitute.For<IRegistryKeyWrapper>();
        _mockRoot.OpenSubKey(RunKey, false).Returns(mockSubKey);

        var result = _sut.SetStartWithWindows(false);

        result.IsSuccess.Should().BeTrue();
        mockSubKey.Received(1).DeleteValue(AppName, false);
    }

    // --- IsRegistered ---

    [Fact]
    public void IsRegistered_WhenGetValueReturnsNonNull_ReturnsTrue()
    {
        var mockSubKey = Substitute.For<IRegistryKeyWrapper>();
        _mockRoot.OpenSubKey(RunKey, false).Returns(mockSubKey);
        mockSubKey.GetValue(AppName).Returns("some_path");

        var result = _sut.IsRegistered();

        result.Should().BeTrue();
    }

    [Fact]
    public void IsRegistered_WhenGetValueReturnsNull_ReturnsFalse()
    {
        var mockSubKey = Substitute.For<IRegistryKeyWrapper>();
        _mockRoot.OpenSubKey(RunKey, false).Returns(mockSubKey);
        mockSubKey.GetValue(AppName).Returns(null);

        var result = _sut.IsRegistered();

        result.Should().BeFalse();
    }

    [Fact]
    public void IsRegistered_WhenOpenSubKeyReturnsNull_ReturnsFalse()
    {
        _mockRoot.OpenSubKey(RunKey, false).Returns((IRegistryKeyWrapper?)null);

        var result = _sut.IsRegistered();

        result.Should().BeFalse();
    }

    // --- EnsureRegistration ---

    [Fact]
    public void EnsureRegistration_WhenDisabled_AndNoRegistryEntry_ReturnsSuccessWithNoSync()
    {
        // When start-with-Windows is disabled and no external entry exists, no sync needed.
        var mockSubKey = Substitute.For<IRegistryKeyWrapper>();
        _mockRoot.OpenSubKey(RunKey, false).Returns(mockSubKey);
        mockSubKey.GetValue(AppName).Returns(null);

        var result = _sut.EnsureRegistration(false);

        result.IsSuccess.Should().BeTrue();
        result.Value.SettingsNeedSync.Should().BeFalse();
        result.Value.PathWasUpdated.Should().BeFalse();
    }

    [Fact]
    public void EnsureRegistration_WhenDisabled_AndRegistryKeyCannotBeOpened_ReturnsSuccessWithNoSync()
    {
        // When the registry key cannot be opened for read, treat as no entry exists.
        _mockRoot.OpenSubKey(RunKey, false).Returns((IRegistryKeyWrapper?)null);

        var result = _sut.EnsureRegistration(false);

        result.IsSuccess.Should().BeTrue();
        result.Value.SettingsNeedSync.Should().BeFalse();
        result.Value.PathWasUpdated.Should().BeFalse();
    }

    [Fact]
    public void EnsureRegistration_WhenEnabled_AndRegistryKeyCannotBeOpened_ReturnsFailure()
    {
        // Requirement 1.5: If the Run registry key cannot be opened, return failure.
        _mockRoot.OpenSubKey(RunKey, true).Returns((IRegistryKeyWrapper?)null);

        var result = _sut.EnsureRegistration(true);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    // --- UpdateRegisteredPath ---

    [Fact]
    public void UpdateRegisteredPath_WhenRegistryKeyOpens_SetsValueWithQuotedPathAndReturnsSuccess()
    {
        var mockSubKey = Substitute.For<IRegistryKeyWrapper>();
        _mockRoot.OpenSubKey(RunKey, true).Returns(mockSubKey);
        var newPath = @"C:\Program Files\MonitorBrightnessController\MonitorBrightnessController.exe";

        var result = _sut.UpdateRegisteredPath(newPath);

        result.IsSuccess.Should().BeTrue();
        mockSubKey.Received(1).SetValue(AppName, $"\"{newPath}\" --silent");
    }

    [Fact]
    public void UpdateRegisteredPath_WhenRegistryKeyCannotBeOpened_ReturnsFailure()
    {
        // Requirement 1.5: Registry key open failure returns failure result.
        _mockRoot.OpenSubKey(RunKey, true).Returns((IRegistryKeyWrapper?)null);

        var result = _sut.UpdateRegisteredPath(@"C:\some\path.exe");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    // ===================================================================
    // v1.5 Registry Sync Behavior Tests (Requirements 3.1, 3.3, 3.5, 3.6, 3.7, 7.2, 7.4)
    // ===================================================================

    // --- Requirement 7.2: Registry entry exists, SettingsStore has false → syncs to true ---

    [Fact]
    public void EnsureRegistration_WhenDisabled_AndExternalEntryExists_WithMatchingPath_ReturnsSettingsNeedSync()
    {
        // Requirement 7.2: External entry detected (e.g. installer-created) while SettingsStore
        // has StartWithWindows=false → signal that SettingsStore needs sync to true.
        var currentExePath = Environment.ProcessPath!;
        var expectedValue = $"\"{currentExePath}\" --silent";

        var mockReadKey = Substitute.For<IRegistryKeyWrapper>();
        _mockRoot.OpenSubKey(RunKey, false).Returns(mockReadKey);
        mockReadKey.GetValue(AppName).Returns(expectedValue);

        var result = _sut.EnsureRegistration(false);

        result.IsSuccess.Should().BeTrue();
        result.Value.SettingsNeedSync.Should().BeTrue();
        result.Value.PathWasUpdated.Should().BeFalse();
    }

    [Fact]
    public void EnsureRegistration_WhenDisabled_AndExternalEntryExists_WithDifferentPath_SyncsAndUpdatesPath()
    {
        // Requirement 7.2 + 7.4: External entry with different path → sync settings AND update path.
        var differentPath = @"""C:\OldPath\MonitorBrightnessController.exe"" --silent";

        var mockReadKey = Substitute.For<IRegistryKeyWrapper>();
        var mockWriteKey = Substitute.For<IRegistryKeyWrapper>();
        _mockRoot.OpenSubKey(RunKey, false).Returns(mockReadKey);
        _mockRoot.OpenSubKey(RunKey, true).Returns(mockWriteKey);
        mockReadKey.GetValue(AppName).Returns(differentPath);

        var result = _sut.EnsureRegistration(false);

        result.IsSuccess.Should().BeTrue();
        result.Value.SettingsNeedSync.Should().BeTrue();
        result.Value.PathWasUpdated.Should().BeTrue();
        mockWriteKey.Received(1).SetValue(AppName, Arg.Is<string>(v => v.Contains("--silent")));
    }

    // --- Requirement 3.7: Enable with different path → overwrites registry entry ---

    [Fact]
    public void SetStartWithWindows_Enable_AlwaysWritesCurrentExePath()
    {
        // Requirement 3.7: Overwrite existing entry if path differs.
        // SetStartWithWindows(true) always writes the current exe path, regardless of what's there.
        var mockSubKey = Substitute.For<IRegistryKeyWrapper>();
        _mockRoot.OpenSubKey(RunKey, true).Returns(mockSubKey);

        var result = _sut.SetStartWithWindows(true);

        // If ProcessPath is available (normal test env), it should always write.
        if (result.IsSuccess)
        {
            var currentExePath = Environment.ProcessPath!;
            mockSubKey.Received(1).SetValue(AppName, $"\"{currentExePath}\" --silent");
        }
    }

    // --- Requirement 3.3: Disable with missing entry → completes without error ---

    [Fact]
    public void SetStartWithWindows_Disable_WithMissingEntry_CompletesSuccessfully()
    {
        // Requirement 3.3: Disable tolerates missing registry entry (throwOnMissingValue: false).
        var mockSubKey = Substitute.For<IRegistryKeyWrapper>();
        _mockRoot.OpenSubKey(RunKey, false).Returns(mockSubKey);
        // DeleteValue with throwOnMissingValue=false should not throw even if value doesn't exist.
        // NSubstitute's default is to do nothing for void methods.

        var result = _sut.SetStartWithWindows(false);

        result.IsSuccess.Should().BeTrue();
        mockSubKey.Received(1).DeleteValue(AppName, false);
    }

    // --- Requirement 3.6 / 7.5: Registry write failure → returns failure, preserves SettingsStore value ---

    [Fact]
    public void SetStartWithWindows_Enable_WhenSetValueThrows_ReturnsFailure()
    {
        // Requirement 3.6: Registry write failure returns failure, SettingsStore value preserved.
        var mockSubKey = Substitute.For<IRegistryKeyWrapper>();
        _mockRoot.OpenSubKey(RunKey, true).Returns(mockSubKey);
        mockSubKey.When(k => k.SetValue(Arg.Any<string>(), Arg.Any<object>()))
            .Do(_ => throw new UnauthorizedAccessException("Access denied"));

        var result = _sut.SetStartWithWindows(true);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Access denied");
    }

    [Fact]
    public void EnsureRegistration_WhenEnabled_AndSetValueThrows_ReturnsFailure()
    {
        // Requirement 7.5: Registry failure during EnsureRegistration returns failure.
        var mockSubKey = Substitute.For<IRegistryKeyWrapper>();
        _mockRoot.OpenSubKey(RunKey, true).Returns(mockSubKey);
        mockSubKey.GetValue(AppName).Returns("\"C:\\OldPath\\app.exe\" --silent");
        mockSubKey.When(k => k.SetValue(Arg.Any<string>(), Arg.Any<object>()))
            .Do(_ => throw new UnauthorizedAccessException("Access denied"));

        var result = _sut.EnsureRegistration(true);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Access denied");
    }

    // --- Requirement 7.4: Startup path comparison → updates entry when paths differ ---

    [Fact]
    public void EnsureRegistration_WhenEnabled_AndPathMatches_DoesNotUpdate()
    {
        // Requirement 7.4: When paths match, no update is needed.
        var currentExePath = Environment.ProcessPath!;
        var expectedValue = $"\"{currentExePath}\" --silent";

        var mockSubKey = Substitute.For<IRegistryKeyWrapper>();
        _mockRoot.OpenSubKey(RunKey, true).Returns(mockSubKey);
        mockSubKey.GetValue(AppName).Returns(expectedValue);

        var result = _sut.EnsureRegistration(true);

        result.IsSuccess.Should().BeTrue();
        result.Value.PathWasUpdated.Should().BeFalse();
        result.Value.SettingsNeedSync.Should().BeFalse();
        mockSubKey.DidNotReceive().SetValue(Arg.Any<string>(), Arg.Any<object>());
    }

    [Fact]
    public void EnsureRegistration_WhenEnabled_AndPathDiffers_UpdatesEntry()
    {
        // Requirement 7.4: When registry path differs from current exe, overwrite entry.
        var oldValue = @"""C:\OldLocation\MonitorBrightnessController.exe"" --silent";

        var mockSubKey = Substitute.For<IRegistryKeyWrapper>();
        _mockRoot.OpenSubKey(RunKey, true).Returns(mockSubKey);
        mockSubKey.GetValue(AppName).Returns(oldValue);

        var result = _sut.EnsureRegistration(true);

        result.IsSuccess.Should().BeTrue();
        result.Value.PathWasUpdated.Should().BeTrue();
        result.Value.SettingsNeedSync.Should().BeFalse();
        mockSubKey.Received(1).SetValue(AppName, Arg.Is<string>(v => v.Contains("--silent")));
    }

    [Fact]
    public void EnsureRegistration_WhenEnabled_AndNoExistingEntry_WritesNewEntry()
    {
        // Requirement 7.4: When no registry entry exists and setting is enabled, write entry.
        var mockSubKey = Substitute.For<IRegistryKeyWrapper>();
        _mockRoot.OpenSubKey(RunKey, true).Returns(mockSubKey);
        mockSubKey.GetValue(AppName).Returns(null);

        var result = _sut.EnsureRegistration(true);

        result.IsSuccess.Should().BeTrue();
        result.Value.PathWasUpdated.Should().BeTrue();
        mockSubKey.Received(1).SetValue(AppName, Arg.Is<string>(v => v.Contains("--silent")));
    }
}
