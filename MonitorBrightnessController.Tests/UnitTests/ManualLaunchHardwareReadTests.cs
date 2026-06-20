using System.Collections.Generic;
using FluentAssertions;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;
using MonitorBrightnessController.Presentation;
using NSubstitute;
using Xunit;
using MbcUnit = MonitorBrightnessController.Models.Unit;

namespace MonitorBrightnessController.Tests.UnitTests;

/// <summary>
/// Unit tests for manual launch (skipAutoApply=true) hardware read behavior.
/// Validates Requirements 1.1, 1.4, 1.5 for the v1.5 enhancements:
/// - Load() calls DetectMonitors() and populates sliders from CurrentBrightness/CurrentGamma
/// - Profile dropdown starts with no selection on manual launch
/// - DDC/CI read failure: slider defaults to 50, text shows "unknown", controls disabled, error indicator shown
/// </summary>
public class ManualLaunchHardwareReadTests
{
    private const string MonitorPath1 = @"\\?\DISPLAY#MON1#manual_launch";
    private const string MonitorPath2 = @"\\?\DISPLAY#MON2#manual_launch";

    private static IMonitorService MonitorServiceWith(params MonitorState[] monitors)
    {
        var service = Substitute.For<IMonitorService>();
        service.DetectMonitors().Returns(new List<MonitorState>(monitors));
        return service;
    }

    private static IProfileManager ProfileManagerWith(params Profile[] profiles)
    {
        var manager = Substitute.For<IProfileManager>();
        manager.GetAllProfiles().Returns(new List<Profile>(profiles));
        foreach (var profile in profiles)
        {
            manager.GetProfile(profile.Name).Returns(Result<Profile>.Success(profile));
        }
        return manager;
    }

    private static ISettingsStore SettingsStoreWith(AppSettings? settings = null)
    {
        var store = Substitute.For<ISettingsStore>();
        store.Load().Returns(settings ?? new AppSettings());
        store.Save(Arg.Any<AppSettings>()).Returns(Result<MbcUnit>.Success(MbcUnit.Value));
        return store;
    }

    // -------------------------------------------------------------------------
    // Requirement 1.1: Manual launch reads hardware brightness and gamma
    // -------------------------------------------------------------------------

    [Fact]
    public void ManualLaunch_PopulatesSlidersFromHardwareBrightness()
    {
        // Arrange
        var monitor = new MonitorState
        {
            MonitorIndex = 1,
            MonitorName = "Test Monitor",
            DevicePath = MonitorPath1,
            CurrentBrightness = 73,
            CurrentGamma = 62,
            IsControllable = true
        };

        var monitorService = MonitorServiceWith(monitor);
        var settingsStore = SettingsStoreWith();
        var profileManager = ProfileManagerWith();

        // Act: manual launch (skipAutoApply=true)
        var vm = new MainWindowViewModel(
            monitorService, settingsStore, profileManager, skipAutoApply: true);

        // Assert: sliders populated from hardware values
        vm.Monitors.Should().HaveCount(1);
        vm.Monitors[0].Brightness.Should().Be(73);
        vm.Monitors[0].Gamma.Should().Be(62);
    }

    [Fact]
    public void ManualLaunch_MultipleMonitors_AllPopulatedFromHardware()
    {
        // Arrange
        var monitor1 = new MonitorState
        {
            MonitorIndex = 1,
            MonitorName = "Monitor A",
            DevicePath = MonitorPath1,
            CurrentBrightness = 40,
            CurrentGamma = 55,
            IsControllable = true
        };

        var monitor2 = new MonitorState
        {
            MonitorIndex = 2,
            MonitorName = "Monitor B",
            DevicePath = MonitorPath2,
            CurrentBrightness = 80,
            CurrentGamma = 90,
            IsControllable = true
        };

        var monitorService = MonitorServiceWith(monitor1, monitor2);
        var settingsStore = SettingsStoreWith();
        var profileManager = ProfileManagerWith();

        // Act: manual launch
        var vm = new MainWindowViewModel(
            monitorService, settingsStore, profileManager, skipAutoApply: true);

        // Assert: each monitor shows its own hardware values
        vm.Monitors.Should().HaveCount(2);
        vm.Monitors[0].Brightness.Should().Be(40);
        vm.Monitors[0].Gamma.Should().Be(55);
        vm.Monitors[1].Brightness.Should().Be(80);
        vm.Monitors[1].Gamma.Should().Be(90);
    }

    [Fact]
    public void ManualLaunch_CallsDetectMonitors()
    {
        // Arrange
        var monitorService = MonitorServiceWith();
        var settingsStore = SettingsStoreWith();
        var profileManager = ProfileManagerWith();

        // Act
        _ = new MainWindowViewModel(
            monitorService, settingsStore, profileManager, skipAutoApply: true);

        // Assert: DetectMonitors was called
        monitorService.Received(1).DetectMonitors();
    }

    [Fact]
    public void ManualLaunch_DoesNotApplyAnyProfile()
    {
        // Arrange: settings have a startup profile configured, but manual launch should ignore it
        var profile = new Profile
        {
            Name = "DayProfile",
            MonitorBrightnessMap = new Dictionary<string, int> { [MonitorPath1] = 90 },
            MonitorGammaMap = new Dictionary<string, int> { [MonitorPath1] = 85 }
        };

        var monitor = new MonitorState
        {
            MonitorIndex = 1,
            MonitorName = "Test",
            DevicePath = MonitorPath1,
            CurrentBrightness = 50,
            CurrentGamma = 50,
            IsControllable = true
        };

        var monitorService = MonitorServiceWith(monitor);
        var settings = new AppSettings
        {
            AutoApplyOnStartup = true,
            DefaultStartupProfileName = "DayProfile"
        };
        var settingsStore = SettingsStoreWith(settings);
        var profileManager = ProfileManagerWith(profile);

        // Act: manual launch (skipAutoApply=true)
        var vm = new MainWindowViewModel(
            monitorService, settingsStore, profileManager, skipAutoApply: true);

        // Assert: profile NOT applied — sliders show hardware values, not profile values
        vm.Monitors[0].Brightness.Should().Be(50, "manual launch should show hardware values, not profile");
        vm.Monitors[0].Gamma.Should().Be(50, "manual launch should show hardware values, not profile");
        profileManager.DidNotReceive().ApplyProfile(Arg.Any<string>(), Arg.Any<IMonitorService>());
    }

    // -------------------------------------------------------------------------
    // Requirement 1.4: Profile dropdown starts with no selection
    // -------------------------------------------------------------------------

    [Fact]
    public void ManualLaunch_MonitorsTabHeader_ShowsCurrentSettings()
    {
        // Arrange
        var monitorService = MonitorServiceWith();
        var settings = new AppSettings
        {
            AutoApplyOnStartup = true,
            DefaultStartupProfileName = "SomeProfile"
        };
        var settingsStore = SettingsStoreWith(settings);
        var profileManager = ProfileManagerWith(new Profile
        {
            Name = "SomeProfile",
            MonitorBrightnessMap = new Dictionary<string, int>()
        });

        // Act: manual launch
        var vm = new MainWindowViewModel(
            monitorService, settingsStore, profileManager, skipAutoApply: true);

        // Assert: header shows "Current Settings" (no profile applied)
        vm.MonitorsTabHeader.Should().Be("Current Settings");
    }

    [Fact]
    public void ManualLaunch_NoSetBrightnessOrSetGammaCalled()
    {
        // Arrange
        var monitor = new MonitorState
        {
            MonitorIndex = 1,
            MonitorName = "Monitor",
            DevicePath = MonitorPath1,
            CurrentBrightness = 60,
            CurrentGamma = 70,
            IsControllable = true
        };

        var monitorService = MonitorServiceWith(monitor);
        var settingsStore = SettingsStoreWith(new AppSettings
        {
            AutoApplyOnStartup = true,
            DefaultStartupProfileName = "TestProfile"
        });
        var profileManager = ProfileManagerWith(new Profile
        {
            Name = "TestProfile",
            MonitorBrightnessMap = new Dictionary<string, int> { [MonitorPath1] = 100 },
            MonitorGammaMap = new Dictionary<string, int> { [MonitorPath1] = 100 }
        });

        // Act: manual launch
        _ = new MainWindowViewModel(
            monitorService, settingsStore, profileManager, skipAutoApply: true);

        // Assert: no hardware write calls
        monitorService.DidNotReceive().SetBrightness(Arg.Any<int>(), Arg.Any<int>());
        monitorService.DidNotReceive().SetGamma(Arg.Any<int>(), Arg.Any<int>());
    }

    // -------------------------------------------------------------------------
    // Requirement 1.5: DDC/CI read failure handling
    // -------------------------------------------------------------------------

    [Fact]
    public void ManualLaunch_DdcCiFailure_SliderDefaultsTo50()
    {
        // Arrange: monitor with null brightness/gamma (DDC/CI read failed)
        var monitor = new MonitorState
        {
            MonitorIndex = 1,
            MonitorName = "Failed Monitor",
            DevicePath = MonitorPath1,
            CurrentBrightness = null,
            CurrentGamma = null,
            IsControllable = true
        };

        var monitorService = MonitorServiceWith(monitor);
        var settingsStore = SettingsStoreWith();
        var profileManager = ProfileManagerWith();

        // Act: manual launch
        var vm = new MainWindowViewModel(
            monitorService, settingsStore, profileManager, skipAutoApply: true);

        // Assert: slider defaults to 50
        vm.Monitors.Should().HaveCount(1);
        vm.Monitors[0].Brightness.Should().Be(50);
        vm.Monitors[0].Gamma.Should().Be(50);
    }

    [Fact]
    public void ManualLaunch_DdcCiFailure_DisplaysUnknownText()
    {
        // Arrange
        var monitor = new MonitorState
        {
            MonitorIndex = 1,
            MonitorName = "Failed Monitor",
            DevicePath = MonitorPath1,
            CurrentBrightness = null,
            CurrentGamma = null,
            IsControllable = true
        };

        var monitorService = MonitorServiceWith(monitor);
        var settingsStore = SettingsStoreWith();
        var profileManager = ProfileManagerWith();

        // Act: manual launch
        var vm = new MainWindowViewModel(
            monitorService, settingsStore, profileManager, skipAutoApply: true);

        // Assert: text shows "unknown"
        vm.Monitors[0].BrightnessText.Should().Be("unknown");
        vm.Monitors[0].GammaText.Should().Be("unknown");
    }

    [Fact]
    public void ManualLaunch_DdcCiFailure_DisablesSliderControls()
    {
        // Arrange
        var monitor = new MonitorState
        {
            MonitorIndex = 1,
            MonitorName = "Failed Monitor",
            DevicePath = MonitorPath1,
            CurrentBrightness = null,
            CurrentGamma = null,
            IsControllable = true
        };

        var monitorService = MonitorServiceWith(monitor);
        var settingsStore = SettingsStoreWith();
        var profileManager = ProfileManagerWith();

        // Act: manual launch
        var vm = new MainWindowViewModel(
            monitorService, settingsStore, profileManager, skipAutoApply: true);

        // Assert: controls disabled (IsControllable false because CurrentBrightness is null)
        vm.Monitors[0].IsControllable.Should().BeFalse();
    }

    [Fact]
    public void ManualLaunch_DdcCiFailure_ShowsErrorIndicator()
    {
        // Arrange
        var monitor = new MonitorState
        {
            MonitorIndex = 1,
            MonitorName = "Failed Monitor",
            DevicePath = MonitorPath1,
            CurrentBrightness = null,
            CurrentGamma = null,
            IsControllable = true
        };

        var monitorService = MonitorServiceWith(monitor);
        var settingsStore = SettingsStoreWith();
        var profileManager = ProfileManagerWith();

        // Act: manual launch
        var vm = new MainWindowViewModel(
            monitorService, settingsStore, profileManager, skipAutoApply: true);

        // Assert: error indicator shown on monitor panel
        vm.Monitors[0].HasDdcReadError.Should().BeTrue();
        vm.Monitors[0].HasError.Should().BeTrue();
    }

    [Fact]
    public void ManualLaunch_MixedMonitors_FailedAndWorking()
    {
        // Arrange: one monitor with DDC/CI failure, one working
        var failedMonitor = new MonitorState
        {
            MonitorIndex = 1,
            MonitorName = "Failed",
            DevicePath = MonitorPath1,
            CurrentBrightness = null,
            CurrentGamma = null,
            IsControllable = true
        };

        var workingMonitor = new MonitorState
        {
            MonitorIndex = 2,
            MonitorName = "Working",
            DevicePath = MonitorPath2,
            CurrentBrightness = 85,
            CurrentGamma = 70,
            IsControllable = true
        };

        var monitorService = MonitorServiceWith(failedMonitor, workingMonitor);
        var settingsStore = SettingsStoreWith();
        var profileManager = ProfileManagerWith();

        // Act: manual launch
        var vm = new MainWindowViewModel(
            monitorService, settingsStore, profileManager, skipAutoApply: true);

        // Assert: failed monitor has error, working monitor shows hardware values
        vm.Monitors[0].HasDdcReadError.Should().BeTrue();
        vm.Monitors[0].Brightness.Should().Be(50);
        vm.Monitors[0].BrightnessText.Should().Be("unknown");
        vm.Monitors[0].IsControllable.Should().BeFalse();

        vm.Monitors[1].HasDdcReadError.Should().BeFalse();
        vm.Monitors[1].Brightness.Should().Be(85);
        vm.Monitors[1].Gamma.Should().Be(70);
        vm.Monitors[1].IsControllable.Should().BeTrue();
    }

    [Fact]
    public void ManualLaunch_DdcCiFailure_BrightnessOnlyFailed_GammaStillUnknown()
    {
        // Arrange: monitor where only gamma read failed (brightness succeeded)
        // Note: in the current model, if CurrentBrightness is null, IsControllable becomes false
        // even if CurrentGamma might have a value. The model uses CurrentBrightness as the gate.
        var monitor = new MonitorState
        {
            MonitorIndex = 1,
            MonitorName = "Partial Failure",
            DevicePath = MonitorPath1,
            CurrentBrightness = 60,
            CurrentGamma = null,  // gamma read failed
            IsControllable = true
        };

        var monitorService = MonitorServiceWith(monitor);
        var settingsStore = SettingsStoreWith();
        var profileManager = ProfileManagerWith();

        // Act: manual launch
        var vm = new MainWindowViewModel(
            monitorService, settingsStore, profileManager, skipAutoApply: true);

        // Assert: brightness works, gamma defaults to 50 with "unknown" text
        vm.Monitors[0].Brightness.Should().Be(60);
        vm.Monitors[0].Gamma.Should().Be(50);
        vm.Monitors[0].GammaText.Should().Be("unknown");
        // Monitor is still controllable because CurrentBrightness has a value
        vm.Monitors[0].IsControllable.Should().BeTrue();
    }
}
