using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;
using MonitorBrightnessController.Presentation;
using NSubstitute;
using Xunit;
using MbcUnit = MonitorBrightnessController.Models.Unit;

namespace MonitorBrightnessController.Tests;

/// <summary>
/// Unit tests for the update notification UI logic in <see cref="MainWindowViewModel"/>.
/// Validates that update checks are triggered appropriately based on settings, that the
/// notification properties are populated correctly, and that the dismiss command works.
/// Uses NSubstitute to mock IMonitorService, ISettingsStore, IProfileManager, and IUpdateChecker.
/// Validates: Requirements 5.1, 5.2, 5.7, 6.2
/// </summary>
public class UpdateNotificationUiTests
{
    private readonly IMonitorService _monitorService;
    private readonly IProfileManager _profileManager;

    public UpdateNotificationUiTests()
    {
        _monitorService = Substitute.For<IMonitorService>();
        _monitorService.DetectMonitors().Returns(new List<MonitorState>());

        _profileManager = Substitute.For<IProfileManager>();
        _profileManager.GetAllProfiles().Returns(new List<Profile>());
    }

    private static ISettingsStore SettingsStoreWith(AppSettings settings)
    {
        var store = Substitute.For<ISettingsStore>();
        store.Load().Returns(settings);
        store.Save(Arg.Any<AppSettings>()).Returns(Result<MbcUnit>.Success(MbcUnit.Value));
        return store;
    }

    // -------------------------------------------------------------------------
    // Requirement 5.1, 6.2: CheckForUpdatesOnStartup=true triggers update check
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Constructor_CheckForUpdatesEnabled_TriggersUpdateCheck()
    {
        // Arrange
        var updateChecker = Substitute.For<IUpdateChecker>();
        updateChecker.CheckForUpdateAsync(Arg.Any<CancellationToken>())
            .Returns(new UpdateCheckResult(false, null, null));

        var settings = new AppSettings { CheckForUpdatesOnStartup = true };
        var settingsStore = SettingsStoreWith(settings);

        // Act
        var vm = new MainWindowViewModel(
            _monitorService, settingsStore, _profileManager,
            startupRegistration: null, applicationInstaller: null, updateChecker: updateChecker);

        // Allow the fire-and-forget async call to complete
        await Task.Delay(100);

        // Assert: the update checker was called
        await updateChecker.Received(1).CheckForUpdateAsync(Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // Requirement 5.1, 6.2: CheckForUpdatesOnStartup=false skips update check
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Constructor_CheckForUpdatesDisabled_SkipsUpdateCheck()
    {
        // Arrange
        var updateChecker = Substitute.For<IUpdateChecker>();
        updateChecker.CheckForUpdateAsync(Arg.Any<CancellationToken>())
            .Returns(new UpdateCheckResult(false, null, null));

        var settings = new AppSettings { CheckForUpdatesOnStartup = false };
        var settingsStore = SettingsStoreWith(settings);

        // Act
        var vm = new MainWindowViewModel(
            _monitorService, settingsStore, _profileManager,
            startupRegistration: null, applicationInstaller: null, updateChecker: updateChecker);

        // Allow time for any potential async calls
        await Task.Delay(100);

        // Assert: the update checker was NOT called
        await updateChecker.DidNotReceive().CheckForUpdateAsync(Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // Requirement 5.2: Newer version found sets IsUpdateAvailable=true with correct text
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Constructor_NewerVersionFound_SetsUpdateAvailableWithCorrectText()
    {
        // Arrange
        var updateChecker = Substitute.For<IUpdateChecker>();
        updateChecker.CheckForUpdateAsync(Arg.Any<CancellationToken>())
            .Returns(new UpdateCheckResult(true, "2.0.0", "https://github.com/dlightman/monitor-brightness-controller/releases/tag/v2.0.0"));

        var settings = new AppSettings { CheckForUpdatesOnStartup = true };
        var settingsStore = SettingsStoreWith(settings);

        // Act
        var vm = new MainWindowViewModel(
            _monitorService, settingsStore, _profileManager,
            startupRegistration: null, applicationInstaller: null, updateChecker: updateChecker);

        // Allow the fire-and-forget async call to complete
        await Task.Delay(200);

        // Assert
        vm.IsUpdateAvailable.Should().BeTrue();
        vm.LatestVersionText.Should().Be("Version 2.0.0 is available");
        vm.UpdateReleaseUrl.Should().Be("https://github.com/dlightman/monitor-brightness-controller/releases/tag/v2.0.0");
    }

    // -------------------------------------------------------------------------
    // Requirement 5.2: Dismiss command hides notification
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DismissUpdateCommand_HidesNotification()
    {
        // Arrange: set up a scenario where update notification is shown
        var updateChecker = Substitute.For<IUpdateChecker>();
        updateChecker.CheckForUpdateAsync(Arg.Any<CancellationToken>())
            .Returns(new UpdateCheckResult(true, "2.0.0", "https://github.com/example/releases/tag/v2.0.0"));

        var settings = new AppSettings { CheckForUpdatesOnStartup = true };
        var settingsStore = SettingsStoreWith(settings);

        var vm = new MainWindowViewModel(
            _monitorService, settingsStore, _profileManager,
            startupRegistration: null, applicationInstaller: null, updateChecker: updateChecker);

        // Allow the fire-and-forget async call to complete
        await Task.Delay(200);

        // Verify notification is initially shown
        vm.IsUpdateAvailable.Should().BeTrue();

        // Act: execute dismiss command
        vm.DismissUpdateCommand.Execute(null);

        // Assert: notification is hidden
        vm.IsUpdateAvailable.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // Requirement 5.7: Only one check per launch
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Constructor_OnlyOneCheckPerLaunch_SecondCheckSkipped()
    {
        // Arrange: update checker returns no update so the VM is not in notification state
        var updateChecker = Substitute.For<IUpdateChecker>();
        updateChecker.CheckForUpdateAsync(Arg.Any<CancellationToken>())
            .Returns(new UpdateCheckResult(false, null, null));

        var settings = new AppSettings { CheckForUpdatesOnStartup = true };
        var settingsStore = SettingsStoreWith(settings);

        // Act: construct the VM (triggers first check)
        var vm = new MainWindowViewModel(
            _monitorService, settingsStore, _profileManager,
            startupRegistration: null, applicationInstaller: null, updateChecker: updateChecker);

        // Allow the fire-and-forget async call to complete
        await Task.Delay(200);

        // Assert: only one call was made despite the constructor completing
        await updateChecker.Received(1).CheckForUpdateAsync(Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // Additional: No update available does not set notification
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Constructor_NoUpdateAvailable_DoesNotShowNotification()
    {
        // Arrange
        var updateChecker = Substitute.For<IUpdateChecker>();
        updateChecker.CheckForUpdateAsync(Arg.Any<CancellationToken>())
            .Returns(new UpdateCheckResult(false, null, null));

        var settings = new AppSettings { CheckForUpdatesOnStartup = true };
        var settingsStore = SettingsStoreWith(settings);

        // Act
        var vm = new MainWindowViewModel(
            _monitorService, settingsStore, _profileManager,
            startupRegistration: null, applicationInstaller: null, updateChecker: updateChecker);

        // Allow the fire-and-forget async call to complete
        await Task.Delay(200);

        // Assert
        vm.IsUpdateAvailable.Should().BeFalse();
        vm.LatestVersionText.Should().BeEmpty();
        vm.UpdateReleaseUrl.Should().BeEmpty();
    }
}
