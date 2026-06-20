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

// Feature: enhancements-v1-5, Property 7: Profile preview loads values without hardware commands

/// <summary>
/// Property-based tests verifying that calling PreviewProfile updates the ViewModel's
/// brightness/gamma slider values for mapped monitors without invoking any SetBrightness
/// or SetGamma calls on the MonitorService.
/// </summary>
public class ProfilePreviewNoHardwareTests
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
            .Select(i => $@"\\?\DISPLAY#MON{i}#prop7_{Guid.NewGuid():N}");

    /// <summary>
    /// Generates a brightness or gamma value in the valid range [0, 100].
    /// </summary>
    private static Gen<int> BrightnessGammaValueGen => Gen.Choose(0, 100);

    /// <summary>
    /// Generates test input: a profile with both brightness and gamma maps, plus matching monitors.
    /// The monitors' device paths match the profile map keys so that PreviewProfile can update them.
    /// </summary>
    private static Gen<ProfilePreviewInput> InputGen =>
        from monitorCount in Gen.Choose(1, 5)
        from devicePaths in Gen.ArrayOf(monitorCount, DevicePathGen)
            .Select(paths => paths.Distinct().ToArray())
            .Where(paths => paths.Length > 0)
        from profileName in ProfileNameGen
        from brightnessValues in Gen.ArrayOf(devicePaths.Length, BrightnessGammaValueGen)
        from gammaValues in Gen.ArrayOf(devicePaths.Length, BrightnessGammaValueGen)
        from initialBrightnessValues in Gen.ArrayOf(devicePaths.Length, BrightnessGammaValueGen)
        from initialGammaValues in Gen.ArrayOf(devicePaths.Length, BrightnessGammaValueGen)
        let brightnessMap = devicePaths.Zip(brightnessValues)
            .ToDictionary(x => x.First, x => x.Second)
        let gammaMap = devicePaths.Zip(gammaValues)
            .ToDictionary(x => x.First, x => x.Second)
        let monitors = devicePaths.Select((path, idx) => new MonitorState
        {
            MonitorIndex = idx + 1,
            MonitorName = $"Monitor {idx + 1}",
            DevicePath = path,
            CurrentBrightness = initialBrightnessValues[idx],
            CurrentGamma = initialGammaValues[idx],
            IsControllable = true
        }).ToList()
        let profile = new Profile
        {
            Name = profileName,
            MonitorBrightnessMap = brightnessMap,
            MonitorGammaMap = gammaMap
        }
        select new ProfilePreviewInput(profile, monitors, initialBrightnessValues, initialGammaValues);

    /// <summary>
    /// Property 7: Profile preview loads values without hardware commands.
    ///
    /// For any valid profile containing brightness and gamma maps, and for any set of connected
    /// monitors (with matching device paths), calling PreviewProfile shall update the ViewModel's
    /// brightness/gamma slider values for mapped monitors without invoking any SetBrightness or
    /// SetGamma calls on the MonitorService.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 4.1, 4.2**
    /// </remarks>
    [Property(MaxTest = 100)]
    public Property PreviewProfile_UpdatesSlidersWithoutHardwareCommands()
    {
        return Prop.ForAll(Arb.From(InputGen), input =>
        {
            // Arrange
            var monitorService = Substitute.For<IMonitorService>();
            monitorService.DetectMonitors().Returns(input.Monitors.AsReadOnly());

            var settingsStore = Substitute.For<ISettingsStore>();
            settingsStore.Load().Returns(new AppSettings
            {
                AutoApplyOnStartup = false,
                CheckForUpdatesOnStartup = false
            });
            settingsStore.Save(Arg.Any<AppSettings>()).Returns(Result<MbcUnit>.Success(MbcUnit.Value));

            var profileManager = Substitute.For<IProfileManager>();
            profileManager.GetAllProfiles().Returns(new List<Profile> { input.Profile });
            profileManager.GetProfile(input.Profile.Name)
                .Returns(Result<Profile>.Success(input.Profile));

            // Act: create ViewModel with skipAutoApply=true (manual launch), then call PreviewProfile
            var vm = new MainWindowViewModel(
                monitorService,
                settingsStore,
                profileManager,
                skipAutoApply: true);

            // Clear any calls from construction/Load
            monitorService.ClearReceivedCalls();

            vm.PreviewProfile(input.Profile.Name);

            // Assert 1: Slider values updated to match profile values
            foreach (var monitor in vm.Monitors)
            {
                if (input.Profile.MonitorBrightnessMap.TryGetValue(monitor.DevicePath, out int expectedBrightness))
                {
                    monitor.Brightness.Should().Be(expectedBrightness,
                        $"monitor '{monitor.DevicePath}' brightness should match profile value");
                }

                if (input.Profile.MonitorGammaMap!.TryGetValue(monitor.DevicePath, out int expectedGamma))
                {
                    monitor.Gamma.Should().Be(expectedGamma,
                        $"monitor '{monitor.DevicePath}' gamma should match profile value");
                }
            }

            // Assert 2: No hardware write commands were sent
            monitorService.DidNotReceive().SetBrightness(Arg.Any<int>(), Arg.Any<int>());
            monitorService.DidNotReceive().SetGamma(Arg.Any<int>(), Arg.Any<int>());
        });
    }

    /// <summary>
    /// Test input data containing a profile with valid brightness/gamma maps and matching monitors.
    /// </summary>
    private record ProfilePreviewInput(
        Profile Profile,
        List<MonitorState> Monitors,
        int[] InitialBrightness,
        int[] InitialGamma);
}
