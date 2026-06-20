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

// Feature: enhancements-v1-5, Property 8: Legacy profile preview preserves gamma sliders

/// <summary>
/// Property-based tests verifying that for any profile with a null MonitorGammaMap (legacy profile)
/// and for any initial gamma slider values on the ViewModel's monitors, calling PreviewProfile
/// shall leave all gamma slider values unchanged from their initial state.
/// </summary>
public class LegacyProfileGammaPreservationTests
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
    /// Generates a unique device path for a monitor.
    /// </summary>
    private static Gen<string> DevicePathGen =>
        Gen.Choose(1, 100)
            .Select(i => $@"\\?\DISPLAY#MON{i}#prop8_test_{Guid.NewGuid():N}");

    /// <summary>
    /// Generates a monitor with a specific device path, random brightness and gamma values.
    /// </summary>
    private static Gen<MonitorState> MonitorStateWithPathGen(string devicePath) =>
        from index in Gen.Choose(1, 20)
        from brightness in Gen.Choose(0, 100)
        from gamma in Gen.Choose(0, 100)
        select new MonitorState
        {
            MonitorIndex = index,
            MonitorName = $"Monitor {index}",
            DevicePath = devicePath,
            CurrentBrightness = brightness,
            CurrentGamma = gamma,
            IsControllable = true
        };

    /// <summary>
    /// Generates a list of 1-5 monitors with unique device paths and random gamma values.
    /// Returns the monitors along with their device paths.
    /// </summary>
    private static Gen<List<(string DevicePath, MonitorState State)>> MonitorListGen =>
        Gen.Choose(1, 5)
            .SelectMany(count =>
                Gen.Sequence(Enumerable.Range(0, count).Select(i =>
                {
                    var path = $@"\\?\DISPLAY#MON{i}#prop8_{Guid.NewGuid():N}";
                    return MonitorStateWithPathGen(path)
                        .Select(state => (path, state with { MonitorIndex = i + 1 }));
                }))
                .Select(items => items.ToList()));

    /// <summary>
    /// Generates a legacy profile (null MonitorGammaMap) with brightness values for the given device paths.
    /// </summary>
    private static Gen<Profile> LegacyProfileGen(IReadOnlyList<string> devicePaths) =>
        from name in ProfileNameGen
        from brightnessValues in Gen.ArrayOf(devicePaths.Count, Gen.Choose(0, 100))
        select new Profile
        {
            Name = name,
            MonitorBrightnessMap = devicePaths
                .Zip(brightnessValues, (path, brightness) => new { path, brightness })
                .ToDictionary(x => x.path, x => x.brightness),
            MonitorGammaMap = null  // Legacy profile: no gamma map
        };

    /// <summary>
    /// Property 8: Legacy profile preview preserves gamma sliders.
    ///
    /// For any profile with a null MonitorGammaMap (legacy profile) and for any initial gamma
    /// slider values on the ViewModel's monitors, calling PreviewProfile shall leave all gamma
    /// slider values unchanged from their initial state.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 4.3**
    /// </remarks>
    [Property(MaxTest = 100)]
    public Property LegacyProfile_PreviewProfile_PreservesGammaSliders()
    {
        var inputGen =
            from monitors in MonitorListGen
            from profile in LegacyProfileGen(monitors.Select(m => m.DevicePath).ToList())
            select new { Monitors = monitors, Profile = profile };

        return Prop.ForAll(Arb.From(inputGen), input =>
        {
            // Arrange
            var monitorService = Substitute.For<IMonitorService>();
            monitorService.DetectMonitors().Returns(
                input.Monitors.Select(m => m.State).ToList().AsReadOnly());

            var settingsStore = Substitute.For<ISettingsStore>();
            settingsStore.Load().Returns(new AppSettings());
            settingsStore.Save(Arg.Any<AppSettings>()).Returns(Result<MbcUnit>.Success(MbcUnit.Value));

            var profileManager = Substitute.For<IProfileManager>();
            profileManager.GetAllProfiles().Returns(new List<Profile> { input.Profile });
            profileManager.GetProfile(input.Profile.Name)
                .Returns(Result<Profile>.Success(input.Profile));

            // Create the ViewModel with skipAutoApply (manual launch) to load hardware values
            var vm = new MainWindowViewModel(
                monitorService,
                settingsStore,
                profileManager,
                skipAutoApply: true);

            // Record the initial gamma values before preview
            var initialGammaValues = vm.Monitors
                .ToDictionary(m => m.DevicePath, m => m.Gamma);

            // Act: preview the legacy profile
            vm.PreviewProfile(input.Profile.Name);

            // Assert: all gamma sliders remain at their initial values
            foreach (var monitor in vm.Monitors)
            {
                monitor.Gamma.Should().Be(
                    initialGammaValues[monitor.DevicePath],
                    because: $"legacy profile (null MonitorGammaMap) should not change gamma for monitor '{monitor.DevicePath}'");
            }
        });
    }
}
