using System.Collections.Generic;
using FluentAssertions;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;
using MonitorBrightnessController.Presentation;
using NSubstitute;
using Xunit;
using MbcUnit = MonitorBrightnessController.Models.Unit;

namespace MonitorBrightnessController.Tests;

/// <summary>
/// Unit tests for monitors tab initialization behavior (Requirements 3.1, 3.2, 3.3, 3.5, 3.6).
/// Validates that the ViewModel populates monitor sliders from the correct source:
/// - Profile values when a startup profile was applied successfully
/// - Live DDC/CI values when no profile applies
/// - Error indicators when DDC/CI communication fails
/// </summary>
public class MonitorsTabInitializationTests
{
    private const string MonitorPath1 = @"\\?\DISPLAY#MON1#init_test";
    private const string MonitorPath2 = @"\\?\DISPLAY#MON2#init_test";
    private const string MonitorPathUnmapped = @"\\?\DISPLAY#MON3#unmapped";

    /// <summary>
    /// Creates an IMonitorService mock returning the specified monitor states from DetectMonitors.
    /// </summary>
    private static IMonitorService MonitorServiceWith(params MonitorState[] monitors)
    {
        var service = Substitute.For<IMonitorService>();
        service.DetectMonitors().Returns(new List<MonitorState>(monitors));
        return service;
    }

    /// <summary>
    /// Creates an IProfileManager mock with the specified profiles. GetProfile returns success
    /// for matching names.
    /// </summary>
    private static IProfileManager ProfileManagerWith(params Profile[] profiles)
    {
        var manager = Substitute.For<IProfileManager>();
        manager.GetAllProfiles().Returns(new List<Profile>(profiles));

        foreach (var profile in profiles)
        {
            manager.GetProfile(profile.Name)
                .Returns(Result<Profile>.Success(profile));
        }

        return manager;
    }

    /// <summary>
    /// Creates a settings store mock returning the given settings.
    /// </summary>
    private static ISettingsStore SettingsStoreWith(AppSettings settings)
    {
        var store = Substitute.For<ISettingsStore>();
        store.Load().Returns(settings);
        store.Save(Arg.Any<AppSettings>()).Returns(Result<MbcUnit>.Success(MbcUnit.Value));
        return store;
    }

    // -------------------------------------------------------------------------
    // Requirement 3.1: Applied profile → uses profile values for matched monitors
    // -------------------------------------------------------------------------

    [Fact]
    public void Init_WithAppliedProfile_UsesProfileValuesForMatchedMonitors()
    {
        // Arrange: a monitor with live DDC/CI values of 30/40, but profile sets 80/90
        var monitor = new MonitorState
        {
            MonitorIndex = 1,
            MonitorName = "Monitor 1",
            DevicePath = MonitorPath1,
            CurrentBrightness = 30,
            CurrentGamma = 40,
            IsControllable = true
        };

        var profile = new Profile
        {
            Name = "DayMode",
            MonitorBrightnessMap = new Dictionary<string, int> { [MonitorPath1] = 80 },
            MonitorGammaMap = new Dictionary<string, int> { [MonitorPath1] = 90 }
        };

        var monitorService = MonitorServiceWith(monitor);
        var profileManager = ProfileManagerWith(profile);
        profileManager.ApplyProfile("DayMode", Arg.Any<IMonitorService>())
            .Returns(Result<MbcUnit>.Success(MbcUnit.Value));

        var settings = new AppSettings
        {
            AutoApplyOnStartup = true,
            DefaultStartupProfileName = "DayMode"
        };
        var settingsStore = SettingsStoreWith(settings);

        // Act
        var vm = new MainWindowViewModel(monitorService, settingsStore, profileManager);

        // Assert: profile values are used, not the live DDC/CI values
        vm.Monitors.Should().HaveCount(1);
        vm.Monitors[0].Brightness.Should().Be(80);
        vm.Monitors[0].Gamma.Should().Be(90);
        vm.MonitorsTabHeader.Should().Be("Profile: DayMode");
    }

    // -------------------------------------------------------------------------
    // Requirement 3.2: No profiles → reads live DDC/CI values
    // -------------------------------------------------------------------------

    [Fact]
    public void Init_NoProfiles_ReadsLiveDdcCiValues()
    {
        // Arrange: monitor with live brightness/gamma, no profiles at all
        var monitor = new MonitorState
        {
            MonitorIndex = 1,
            MonitorName = "Monitor 1",
            DevicePath = MonitorPath1,
            CurrentBrightness = 65,
            CurrentGamma = 55,
            IsControllable = true
        };

        var monitorService = MonitorServiceWith(monitor);
        var profileManager = ProfileManagerWith(); // No profiles
        var settings = new AppSettings
        {
            AutoApplyOnStartup = false,
            DefaultStartupProfileName = null
        };
        var settingsStore = SettingsStoreWith(settings);

        // Act
        var vm = new MainWindowViewModel(monitorService, settingsStore, profileManager);

        // Assert: live DDC/CI values are displayed
        vm.Monitors.Should().HaveCount(1);
        vm.Monitors[0].Brightness.Should().Be(65);
        vm.Monitors[0].Gamma.Should().Be(55);
        vm.MonitorsTabHeader.Should().Be("Current Settings");
    }

    // -------------------------------------------------------------------------
    // Requirement 3.3: AutoApply=false → reads live DDC/CI values
    // -------------------------------------------------------------------------

    [Fact]
    public void Init_AutoApplyFalse_ReadsLiveDdcCiValues()
    {
        // Arrange: profiles exist but AutoApply is disabled
        var monitor = new MonitorState
        {
            MonitorIndex = 1,
            MonitorName = "Monitor 1",
            DevicePath = MonitorPath1,
            CurrentBrightness = 42,
            CurrentGamma = 38,
            IsControllable = true
        };

        var profile = new Profile
        {
            Name = "NightMode",
            MonitorBrightnessMap = new Dictionary<string, int> { [MonitorPath1] = 20 },
            MonitorGammaMap = new Dictionary<string, int> { [MonitorPath1] = 15 }
        };

        var monitorService = MonitorServiceWith(monitor);
        var profileManager = ProfileManagerWith(profile);
        var settings = new AppSettings
        {
            AutoApplyOnStartup = false,
            DefaultStartupProfileName = "NightMode"
        };
        var settingsStore = SettingsStoreWith(settings);

        // Act
        var vm = new MainWindowViewModel(monitorService, settingsStore, profileManager);

        // Assert: live values used since AutoApply is off
        vm.Monitors.Should().HaveCount(1);
        vm.Monitors[0].Brightness.Should().Be(42);
        vm.Monitors[0].Gamma.Should().Be(38);
        vm.MonitorsTabHeader.Should().Be("Current Settings");
    }

    // -------------------------------------------------------------------------
    // Requirement 3.5: DDC/CI failure → shows error indicator
    // -------------------------------------------------------------------------

    [Fact]
    public void Init_DdcCiFailure_ShowsErrorIndicator()
    {
        // Arrange: monitor has null CurrentBrightness/CurrentGamma (DDC/CI read failed)
        var monitor = new MonitorState
        {
            MonitorIndex = 1,
            MonitorName = "Monitor 1",
            DevicePath = MonitorPath1,
            CurrentBrightness = null,
            CurrentGamma = null,
            IsControllable = true
        };

        var monitorService = MonitorServiceWith(monitor);
        var profileManager = ProfileManagerWith();
        var settings = new AppSettings
        {
            AutoApplyOnStartup = false
        };
        var settingsStore = SettingsStoreWith(settings);

        // Act
        var vm = new MainWindowViewModel(monitorService, settingsStore, profileManager);

        // Assert: error indicator shown, slider defaults to midpoint 50
        vm.Monitors.Should().HaveCount(1);
        vm.Monitors[0].HasDdcReadError.Should().BeTrue();
        vm.Monitors[0].Brightness.Should().Be(50, "DDC/CI failure defaults brightness to midpoint");
        vm.Monitors[0].Gamma.Should().Be(50, "DDC/CI failure defaults gamma to midpoint");
    }

    // -------------------------------------------------------------------------
    // Requirement 3.6: Profile applied but monitor not in profile → uses live DDC/CI value
    // -------------------------------------------------------------------------

    [Fact]
    public void Init_ProfileApplied_MonitorNotInProfile_UsesLiveDdcCiValue()
    {
        // Arrange: two monitors, profile only maps one of them
        var monitorMapped = new MonitorState
        {
            MonitorIndex = 1,
            MonitorName = "Mapped Monitor",
            DevicePath = MonitorPath1,
            CurrentBrightness = 30,
            CurrentGamma = 35,
            IsControllable = true
        };

        var monitorUnmapped = new MonitorState
        {
            MonitorIndex = 2,
            MonitorName = "Unmapped Monitor",
            DevicePath = MonitorPathUnmapped,
            CurrentBrightness = 70,
            CurrentGamma = 60,
            IsControllable = true
        };

        var profile = new Profile
        {
            Name = "Partial",
            MonitorBrightnessMap = new Dictionary<string, int> { [MonitorPath1] = 85 },
            MonitorGammaMap = new Dictionary<string, int> { [MonitorPath1] = 95 }
        };

        var monitorService = MonitorServiceWith(monitorMapped, monitorUnmapped);
        var profileManager = ProfileManagerWith(profile);
        profileManager.ApplyProfile("Partial", Arg.Any<IMonitorService>())
            .Returns(Result<MbcUnit>.Success(MbcUnit.Value));

        var settings = new AppSettings
        {
            AutoApplyOnStartup = true,
            DefaultStartupProfileName = "Partial"
        };
        var settingsStore = SettingsStoreWith(settings);

        // Act
        var vm = new MainWindowViewModel(monitorService, settingsStore, profileManager);

        // Assert: mapped monitor uses profile values
        vm.Monitors[0].Brightness.Should().Be(85);
        vm.Monitors[0].Gamma.Should().Be(95);

        // Assert: unmapped monitor retains live DDC/CI values
        vm.Monitors[1].Brightness.Should().Be(70);
        vm.Monitors[1].Gamma.Should().Be(60);
        vm.MonitorsTabHeader.Should().Be("Profile: Partial");
    }

    // -------------------------------------------------------------------------
    // Additional: DDC/CI failure for one monitor with successful profile on another
    // -------------------------------------------------------------------------

    [Fact]
    public void Init_DdcCiFailureForOneMonitor_OtherMonitorWorksNormally()
    {
        // Arrange: one monitor with DDC/CI failure, one working
        var failedMonitor = new MonitorState
        {
            MonitorIndex = 1,
            MonitorName = "Failed Monitor",
            DevicePath = MonitorPath1,
            CurrentBrightness = null,
            CurrentGamma = null,
            IsControllable = true
        };

        var workingMonitor = new MonitorState
        {
            MonitorIndex = 2,
            MonitorName = "Working Monitor",
            DevicePath = MonitorPath2,
            CurrentBrightness = 75,
            CurrentGamma = 50,
            IsControllable = true
        };

        var monitorService = MonitorServiceWith(failedMonitor, workingMonitor);
        var profileManager = ProfileManagerWith();
        var settings = new AppSettings { AutoApplyOnStartup = false };
        var settingsStore = SettingsStoreWith(settings);

        // Act
        var vm = new MainWindowViewModel(monitorService, settingsStore, profileManager);

        // Assert: failed monitor has error, working monitor shows live values
        vm.Monitors[0].HasDdcReadError.Should().BeTrue();
        vm.Monitors[0].Brightness.Should().Be(50);

        vm.Monitors[1].HasDdcReadError.Should().BeFalse();
        vm.Monitors[1].Brightness.Should().Be(75);
        vm.Monitors[1].Gamma.Should().Be(50);
    }
}
