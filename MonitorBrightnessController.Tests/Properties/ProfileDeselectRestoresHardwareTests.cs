using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;
using MonitorBrightnessController.Presentation;
using NSubstitute;
using MbcUnit = MonitorBrightnessController.Models.Unit;

namespace MonitorBrightnessController.Tests.Properties;

// Feature: enhancements-v1-5, Property 9: Profile deselect restores hardware values

/// <summary>
/// Property-based tests verifying that after previewing any profile, calling RestoreHardwareValues
/// restores each monitor's brightness and gamma sliders to the hardware-reported values returned
/// by GetBrightness and GetGamma.
/// </summary>
public class ProfileDeselectRestoresHardwareTests
{
    private static readonly char[] AllowedChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-".ToCharArray();

    /// <summary>
    /// Generates a valid profile name (1-20 chars, alphanumeric + underscore + dash).
    /// </summary>
    private static Gen<string> ProfileNameGen =>
        Gen.Choose(1, 20)
            .SelectMany(len =>
                Gen.ArrayOf(len, Gen.Elements(AllowedChars))
                    .Select(chars => new string(chars)));

    /// <summary>
    /// Generates a device path string for a monitor.
    /// </summary>
    private static Gen<string> DevicePathGen =>
        Gen.Choose(1, 10)
            .Select(i => $@"\\?\DISPLAY#MON{i}#prop9_{Guid.NewGuid():N}");

    /// <summary>
    /// Represents a monitor with known hardware values and initial display state.
    /// </summary>
    private record MonitorTestData(
        int MonitorIndex,
        string DevicePath,
        int HardwareBrightness,
        int HardwareGamma,
        int InitialBrightness,
        int InitialGamma);

    /// <summary>
    /// Generates a MonitorTestData with random hardware values and initial values in [0, 100].
    /// </summary>
    private static Gen<MonitorTestData> MonitorTestDataGen =>
        from index in Gen.Choose(1, 20)
        from devicePath in DevicePathGen
        from hwBrightness in Gen.Choose(0, 100)
        from hwGamma in Gen.Choose(0, 100)
        from initBrightness in Gen.Choose(0, 100)
        from initGamma in Gen.Choose(0, 100)
        select new MonitorTestData(index, devicePath, hwBrightness, hwGamma, initBrightness, initGamma);

    /// <summary>
    /// Generates a list of 1-5 MonitorTestData instances with unique indices.
    /// </summary>
    private static Gen<List<MonitorTestData>> MonitorListGen =>
        Gen.Choose(1, 5)
            .SelectMany(count =>
                Gen.ListOf(count, MonitorTestDataGen)
                    .Select(list =>
                    {
                        // Ensure unique indices by reassigning
                        var result = new List<MonitorTestData>();
                        int idx = 1;
                        foreach (var m in list)
                        {
                            result.Add(m with { MonitorIndex = idx });
                            idx++;
                        }
                        return result;
                    }));

    /// <summary>
    /// Generates a profile that maps the given device paths to random brightness/gamma values.
    /// The profile may or may not include a gamma map (to test both modern and legacy profiles).
    /// </summary>
    private static Gen<Profile> ProfileGen(IReadOnlyList<string> devicePaths) =>
        from name in ProfileNameGen
        from brightnessValues in Gen.ListOf(devicePaths.Count, Gen.Choose(0, 100))
        from hasGammaMap in Arb.Generate<bool>()
        from gammaValues in Gen.ListOf(devicePaths.Count, Gen.Choose(0, 100))
        select new Profile
        {
            Name = name,
            MonitorBrightnessMap = devicePaths
                .Zip(brightnessValues, (path, val) => new { path, val })
                .ToDictionary(x => x.path, x => x.val),
            MonitorGammaMap = hasGammaMap
                ? devicePaths
                    .Zip(gammaValues, (path, val) => new { path, val })
                    .ToDictionary(x => x.path, x => x.val)
                : null
        };

    /// <summary>
    /// Property 9: Profile deselect restores hardware values.
    ///
    /// For any set of monitors with known hardware-reported brightness and gamma values,
    /// after previewing any profile (which may change slider values), deselecting the profile
    /// (calling RestoreHardwareValues) shall restore each monitor's brightness and gamma sliders
    /// to the hardware-reported values returned by GetBrightness and GetGamma.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 4.5**
    /// </remarks>
    [Property(MaxTest = 100)]
    public Property RestoreHardwareValues_AfterPreview_RestoresSlidersToBrightnessAndGammaFromHardware()
    {
        var inputGen =
            from monitors in MonitorListGen
            from profile in ProfileGen(monitors.Select(m => m.DevicePath).ToList())
            select new { Monitors = monitors, Profile = profile };

        return Prop.ForAll(Arb.From(inputGen), input =>
        {
            // Arrange
            var monitorService = Substitute.For<IMonitorService>();

            // Build MonitorState list with initial values (from hardware reads at startup)
            var monitorStates = input.Monitors.Select(m => new MonitorState
            {
                MonitorIndex = m.MonitorIndex,
                MonitorName = $"Monitor {m.MonitorIndex}",
                DevicePath = m.DevicePath,
                CurrentBrightness = m.InitialBrightness,
                CurrentGamma = m.InitialGamma,
                IsControllable = true
            }).ToList();

            monitorService.DetectMonitors().Returns(monitorStates.AsReadOnly());

            // Mock GetBrightness and GetGamma to return the known hardware values
            foreach (var monitor in input.Monitors)
            {
                monitorService.GetBrightness(monitor.MonitorIndex)
                    .Returns(Result<int>.Success(monitor.HardwareBrightness));
                monitorService.GetGamma(monitor.MonitorIndex)
                    .Returns(Result<int>.Success(monitor.HardwareGamma));
            }

            // Mock SetBrightness/SetGamma to succeed (needed for MonitorControlViewModel commit callbacks)
            monitorService.SetBrightness(Arg.Any<int>(), Arg.Any<int>())
                .Returns(Result<MbcUnit>.Success(MbcUnit.Value));
            monitorService.SetGamma(Arg.Any<int>(), Arg.Any<int>())
                .Returns(Result<MbcUnit>.Success(MbcUnit.Value));

            var settingsStore = Substitute.For<ISettingsStore>();
            settingsStore.Load().Returns(new AppSettings());
            settingsStore.Save(Arg.Any<AppSettings>()).Returns(Result<MbcUnit>.Success(MbcUnit.Value));

            var profileManager = Substitute.For<IProfileManager>();
            profileManager.GetAllProfiles().Returns(new List<Profile> { input.Profile });
            profileManager.GetProfile(input.Profile.Name)
                .Returns(Result<Profile>.Success(input.Profile));

            // Create ViewModel (manual launch, skipAutoApply=true so it reads hardware values)
            var vm = new MainWindowViewModel(
                monitorService,
                settingsStore,
                profileManager,
                skipAutoApply: true);

            // Act: Preview the profile (this changes slider values)
            vm.PreviewProfile(input.Profile.Name);

            // Act: Deselect profile (restore hardware values)
            vm.RestoreHardwareValues();

            // Assert: each monitor's sliders match the hardware-reported values
            foreach (var monitor in input.Monitors)
            {
                var monitorVm = vm.Monitors.First(m => m.MonitorIndex == monitor.MonitorIndex);
                monitorVm.Brightness.Should().Be(monitor.HardwareBrightness,
                    because: $"Monitor {monitor.MonitorIndex} brightness should be restored to hardware value {monitor.HardwareBrightness}");
                monitorVm.Gamma.Should().Be(monitor.HardwareGamma,
                    because: $"Monitor {monitor.MonitorIndex} gamma should be restored to hardware value {monitor.HardwareGamma}");
            }
        });
    }
}
