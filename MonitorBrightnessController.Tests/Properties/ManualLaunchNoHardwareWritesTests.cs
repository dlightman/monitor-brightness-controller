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

// Feature: enhancements-v1-5, Property 1: Manual launch performs no hardware writes

/// <summary>
/// Property-based tests verifying that manual launches (skipAutoApply=true) never send
/// SetBrightness or SetGamma commands to the MonitorService, regardless of AppSettings
/// configuration or detected monitor state.
/// </summary>
public class ManualLaunchNoHardwareWritesTests
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
    /// Generates a nullable string that is either null, empty, or a valid profile name.
    /// </summary>
    private static Gen<string?> NullableProfileNameGen =>
        Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Constant<string?>(string.Empty),
            ProfileNameGen.Select<string, string?>(n => n));

    /// <summary>
    /// Generates a device path string for a monitor.
    /// </summary>
    private static Gen<string> DevicePathGen =>
        Gen.Choose(1, 10)
            .Select(i => $@"\\?\DISPLAY#MON{i}#prop_test_{Guid.NewGuid():N}");

    /// <summary>
    /// Generates a random MonitorState with random brightness/gamma values in [0, 100].
    /// </summary>
    private static Gen<MonitorState> MonitorStateGen =>
        from index in Gen.Choose(1, 20)
        from brightness in Gen.OneOf(
            Gen.Constant<int?>(null),
            Gen.Choose(0, 100).Select<int, int?>(v => v))
        from gamma in Gen.OneOf(
            Gen.Constant<int?>(null),
            Gen.Choose(0, 100).Select<int, int?>(v => v))
        from devicePath in DevicePathGen
        from isControllable in Arb.Generate<bool>()
        select new MonitorState
        {
            MonitorIndex = index,
            MonitorName = $"Monitor {index}",
            DevicePath = devicePath,
            CurrentBrightness = brightness,
            CurrentGamma = gamma,
            IsControllable = isControllable
        };

    /// <summary>
    /// Generates a list of 0-5 random MonitorState instances.
    /// </summary>
    private static Gen<List<MonitorState>> MonitorListGen =>
        Gen.Choose(0, 5)
            .SelectMany(count => Gen.ListOf(count, MonitorStateGen).Select(l => l.ToList()));

    /// <summary>
    /// Generates a random AppSettings with various combinations of AutoApplyOnStartup,
    /// DefaultStartupProfileName, and LastAppliedProfileName.
    /// </summary>
    private static Gen<AppSettings> AppSettingsGen =>
        from autoApply in Arb.Generate<bool>()
        from defaultProfile in NullableProfileNameGen
        from lastApplied in NullableProfileNameGen
        from startWithWindows in Arb.Generate<bool>()
        from minimizeToTray in Arb.Generate<bool>()
        from smoothTransition in Arb.Generate<bool>()
        from transitionMs in Gen.Choose(100, 2000)
        from refreshOnFocus in Arb.Generate<bool>()
        from checkUpdates in Arb.Generate<bool>()
        select new AppSettings
        {
            AutoApplyOnStartup = autoApply,
            DefaultStartupProfileName = defaultProfile,
            LastAppliedProfileName = lastApplied,
            StartWithWindows = startWithWindows,
            MinimizeToTray = minimizeToTray,
            SmoothTransition = smoothTransition,
            TransitionDurationMs = transitionMs,
            RefreshOnFocus = refreshOnFocus,
            CheckForUpdatesOnStartup = checkUpdates
        };

    /// <summary>
    /// Property 1: Manual launch performs no hardware writes.
    ///
    /// For any set of detected monitors and for any AppSettings configuration (regardless of
    /// AutoApplyOnStartup, DefaultStartupProfileName, or LastAppliedProfileName values),
    /// when the application performs a manual launch (no --silent flag), no SetBrightness or
    /// SetGamma commands shall be sent to the MonitorService.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 1.2, 1.3**
    /// </remarks>
    [Property(MaxTest = 100)]
    public Property ManualLaunch_NeverCallsSetBrightnessOrSetGamma()
    {
        var inputGen =
            from settings in AppSettingsGen
            from monitors in MonitorListGen
            select new { Settings = settings, Monitors = monitors };

        return Prop.ForAll(Arb.From(inputGen), input =>
        {
            // Arrange
            var monitorService = Substitute.For<IMonitorService>();
            monitorService.DetectMonitors().Returns(input.Monitors.AsReadOnly());

            var settingsStore = Substitute.For<ISettingsStore>();
            settingsStore.Load().Returns(input.Settings);
            settingsStore.Save(Arg.Any<AppSettings>()).Returns(Result<MbcUnit>.Success(MbcUnit.Value));

            var profileManager = Substitute.For<IProfileManager>();
            profileManager.GetAllProfiles().Returns(new List<Profile>());

            // Act: manual launch (skipAutoApply = true)
            _ = new MainWindowViewModel(
                monitorService,
                settingsStore,
                profileManager,
                skipAutoApply: true);

            // Assert: no hardware write calls whatsoever
            monitorService.DidNotReceive().SetBrightness(Arg.Any<int>(), Arg.Any<int>());
            monitorService.DidNotReceive().SetGamma(Arg.Any<int>(), Arg.Any<int>());
        });
    }
}
