using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using MonitorBrightnessController.Application;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;
using MonitorBrightnessController.Presentation;
using NSubstitute;
using MbcUnit = MonitorBrightnessController.Models.Unit;

namespace MonitorBrightnessController.Tests.Properties;

// Feature: ui-consolidation, Property 7: Profile apply sends correct values to mapped monitors
// Feature: ui-consolidation, Property 9: Profile deletion removes from store and dropdown
// Feature: ui-consolidation, Property 15: Profile update overwrites with current values

/// <summary>
/// Property-based tests for profile CRUD operations in the UI consolidation feature.
/// </summary>
public class ProfileCrudProperties
{
    /// <summary>
    /// Generates a device path for testing.
    /// </summary>
    private static string MakeDevicePath(int i) => $"\\\\?\\DISPLAY#MON{i}#path{i}";

    /// <summary>
    /// Creates a configured ISettingsStore that holds the given profiles and tracks saves.
    /// The store updates its internal state on Save so that subsequent Load calls return
    /// the latest persisted state.
    /// </summary>
    private static ISettingsStore CreateTrackingSettingsStore(List<Profile> profiles)
    {
        var settings = new AppSettings { Profiles = profiles };
        var store = Substitute.For<ISettingsStore>();
        store.Load().Returns(_ => settings);
        store.Save(Arg.Any<AppSettings>()).Returns(callInfo =>
        {
            settings = callInfo.Arg<AppSettings>();
            return Result<MbcUnit>.Success(MbcUnit.Value);
        });
        return store;
    }

    /// <summary>
    /// Property 7: For any saved profile and set of connected monitors, applying the profile SHALL
    /// call SetBrightness with the profile's brightness value for each connected monitor in the
    /// brightness map, and SetGamma with the profile's gamma value for each connected monitor in the
    /// gamma map. Monitors not in the connected set SHALL be skipped.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 3.4**
    /// </remarks>
    [Property(MaxTest = 100)]
    public Property ProfileApply_SendsCorrectValues_ToMappedConnectedMonitors()
    {
        // Generate monitors mapped in the profile, a subset that is connected,
        // and some extra connected monitors not in the profile.
        var gen =
            from mappedCount in Gen.Choose(1, 6)
            from connectedFlags in Gen.ArrayOf(mappedCount, Gen.Elements(true, false))
            // Ensure at least one mapped monitor is connected so apply doesn't fail with "no connected mapped monitors"
            let ensuredFlags = connectedFlags.Any(f => f)
                ? connectedFlags
                : connectedFlags.Select((f, i) => i == 0 || f).ToArray()
            from extraConnectedCount in Gen.Choose(0, 3)
            from brightnessValues in Gen.ArrayOf(mappedCount, Gen.Choose(0, 100))
            from gammaValues in Gen.ArrayOf(mappedCount, Gen.Choose(0, 100))
            select new
            {
                MappedCount = mappedCount,
                ConnectedFlags = ensuredFlags,
                ExtraConnectedCount = extraConnectedCount,
                BrightnessValues = brightnessValues,
                GammaValues = gammaValues
            };

        return Prop.ForAll(Arb.From(gen), data =>
        {
            var mappedPaths = Enumerable.Range(0, data.MappedCount).Select(MakeDevicePath).ToArray();
            var extraPaths = Enumerable.Range(100, data.ExtraConnectedCount)
                .Select(MakeDevicePath).ToArray();

            // Build profile with mappings for mapped monitors only
            var profile = new Profile
            {
                Name = "apply-test",
                MonitorBrightnessMap = mappedPaths.Zip(data.BrightnessValues)
                    .ToDictionary(p => p.First, p => p.Second),
                MonitorGammaMap = mappedPaths.Zip(data.GammaValues)
                    .ToDictionary(p => p.First, p => p.Second)
            };

            // Build list of connected monitors: connected mapped monitors + extra connected monitors
            int monitorIndex = 1;
            var connectedMonitors = new List<MonitorState>();

            for (int i = 0; i < data.MappedCount; i++)
            {
                if (data.ConnectedFlags[i])
                {
                    connectedMonitors.Add(new MonitorState
                    {
                        MonitorIndex = monitorIndex++,
                        MonitorName = $"Monitor {monitorIndex - 1}",
                        DevicePath = mappedPaths[i],
                        PhysicalHandle = new IntPtr(monitorIndex - 1),
                        IsControllable = true,
                        CurrentBrightness = 50,
                        CurrentGamma = 50
                    });
                }
            }

            // Add extra connected monitors that are NOT in the profile's maps
            for (int i = 0; i < data.ExtraConnectedCount; i++)
            {
                connectedMonitors.Add(new MonitorState
                {
                    MonitorIndex = monitorIndex++,
                    MonitorName = $"Extra Monitor {monitorIndex - 1}",
                    DevicePath = extraPaths[i],
                    PhysicalHandle = new IntPtr(monitorIndex - 1),
                    IsControllable = true,
                    CurrentBrightness = 50,
                    CurrentGamma = 50
                });
            }

            var settingsStore = CreateTrackingSettingsStore(new List<Profile> { profile });
            var monitorService = Substitute.For<IMonitorService>();
            monitorService.DetectMonitors().Returns(connectedMonitors.AsReadOnly());
            monitorService.SetBrightness(Arg.Any<int>(), Arg.Any<int>())
                .Returns(Result<MbcUnit>.Success(MbcUnit.Value));
            monitorService.SetGamma(Arg.Any<int>(), Arg.Any<int>())
                .Returns(Result<MbcUnit>.Success(MbcUnit.Value));

            var profileManager = new ProfileManager(settingsStore);

            // Act: apply the profile
            profileManager.ApplyProfile("apply-test", monitorService);

            // Assert: SetBrightness called with correct value for each connected mapped monitor
            for (int i = 0; i < data.MappedCount; i++)
            {
                var monitor = connectedMonitors.FirstOrDefault(m => m.DevicePath == mappedPaths[i]);
                if (data.ConnectedFlags[i])
                {
                    monitor.Should().NotBeNull(
                        $"mapped monitor {i} should be in the connected list");
                    monitorService.Received().SetBrightness(
                        monitor!.MonitorIndex, data.BrightnessValues[i]);
                }
            }

            // Assert: SetGamma called with correct value for each connected mapped monitor
            for (int i = 0; i < data.MappedCount; i++)
            {
                var monitor = connectedMonitors.FirstOrDefault(m => m.DevicePath == mappedPaths[i]);
                if (data.ConnectedFlags[i])
                {
                    monitorService.Received().SetGamma(
                        monitor!.MonitorIndex, data.GammaValues[i]);
                }
            }

            // Assert: monitors NOT in the connected set (disconnected mapped monitors) were skipped
            var connectedDevicePaths = connectedMonitors.Select(m => m.DevicePath).ToHashSet();
            for (int i = 0; i < data.MappedCount; i++)
            {
                if (!data.ConnectedFlags[i])
                {
                    connectedDevicePaths.Should().NotContain(mappedPaths[i],
                        $"mapped monitor {i} is disconnected and should not be in connected list");
                }
            }

            // Assert: extra connected monitors (not in profile maps) received NO calls
            var extraMonitorIndices = connectedMonitors
                .Where(m => extraPaths.Contains(m.DevicePath))
                .Select(m => m.MonitorIndex)
                .ToHashSet();

            foreach (var extraIndex in extraMonitorIndices)
            {
                monitorService.DidNotReceive().SetBrightness(extraIndex, Arg.Any<int>());
                monitorService.DidNotReceive().SetGamma(extraIndex, Arg.Any<int>());
            }
        });
    }

    /// <summary>
    /// Property 15: For any existing profile, updating it SHALL overwrite its MonitorBrightnessMap
    /// and MonitorGammaMap with the current slider values for all connected monitors, and the
    /// updated profile SHALL be persisted to the store.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 3.6**
    /// </remarks>
    [Property(MaxTest = 100)]
    public Property ProfileUpdate_OverwritesWithCurrentSliderValues()
    {
        var gen =
            from monitorCount in Gen.Choose(1, 5)
            from oldBrightness in Gen.ArrayOf(monitorCount, Gen.Choose(0, 100))
            from oldGamma in Gen.ArrayOf(monitorCount, Gen.Choose(0, 100))
            from newBrightness in Gen.ArrayOf(monitorCount, Gen.Choose(0, 100))
            from newGamma in Gen.ArrayOf(monitorCount, Gen.Choose(0, 100))
            select new
            {
                MonitorCount = monitorCount,
                OldBrightness = oldBrightness,
                OldGamma = oldGamma,
                NewBrightness = newBrightness,
                NewGamma = newGamma
            };

        return Prop.ForAll(Arb.From(gen), data =>
        {
            var paths = Enumerable.Range(0, data.MonitorCount).Select(MakeDevicePath).ToArray();

            // Existing profile with old values
            var existingProfile = new Profile
            {
                Name = "my-profile",
                MonitorBrightnessMap = paths.Zip(data.OldBrightness)
                    .ToDictionary(p => p.First, p => p.Second),
                MonitorGammaMap = paths.Zip(data.OldGamma)
                    .ToDictionary(p => p.First, p => p.Second)
            };

            var profileList = new List<Profile> { existingProfile };
            var settingsStore = CreateTrackingSettingsStore(profileList);
            var profileManager = new ProfileManager(settingsStore);

            // MonitorService mock (needed for ProfileStripViewModel constructor)
            var monitorService = Substitute.For<IMonitorService>();
            monitorService.DetectMonitors().Returns(new List<MonitorState>());

            // Build the new brightness/gamma maps that "current sliders" would produce
            var newBrightnessMap = paths.Zip(data.NewBrightness)
                .ToDictionary(p => p.First, p => p.Second);
            var newGammaMap = paths.Zip(data.NewGamma)
                .ToDictionary(p => p.First, p => p.Second);

            // Create the ViewModel and wire capture callbacks
            var vm = new ProfileStripViewModel(profileManager, monitorService);
            vm.CaptureBrightnessMap = () => new Dictionary<string, int>(newBrightnessMap);
            vm.CaptureGammaMap = () => new Dictionary<string, int>(newGammaMap);

            // Select the profile (this fires OnProfileSelected but that's fine)
            vm.SelectedProfileName = "my-profile";

            // Execute the update command
            vm.UpdateCommand.Execute(null);

            // Verify: the profile in the store now has the NEW values
            var updatedSettings = settingsStore.Load();
            var updatedProfile = updatedSettings.Profiles.First(
                p => string.Equals(p.Name, "my-profile", StringComparison.OrdinalIgnoreCase));

            // Brightness map should match new values
            foreach (var path in paths)
            {
                updatedProfile.MonitorBrightnessMap.Should().ContainKey(path);
                updatedProfile.MonitorBrightnessMap[path].Should().Be(
                    newBrightnessMap[path],
                    $"brightness for {path} should be overwritten with new slider value");
            }

            // Gamma map should match new values
            updatedProfile.MonitorGammaMap.Should().NotBeNull(
                "gamma map should be persisted when CaptureGammaMap returns a map");
            var gammaMap = updatedProfile.MonitorGammaMap!;
            foreach (var path in paths)
            {
                gammaMap.Should().ContainKey(path);
                gammaMap[path].Should().Be(
                    newGammaMap[path],
                    $"gamma for {path} should be overwritten with new slider value");
            }

            // Verify the old values are gone (only new values present)
            updatedProfile.MonitorBrightnessMap.Count.Should().Be(data.MonitorCount);
            gammaMap.Count.Should().Be(data.MonitorCount);
        });
    }
}


/// <summary>
/// In-memory settings store for property tests exercising profile deletion operations.
/// </summary>
internal sealed class InMemorySettingsStore_ProfileDeletion : ISettingsStore
{
    public AppSettings Current { get; private set; }

    public InMemorySettingsStore_ProfileDeletion(AppSettings seed)
    {
        Current = seed;
    }

    public AppSettings Load() => Current;

    public Result<MbcUnit> Save(AppSettings settings)
    {
        Current = settings;
        return Result<MbcUnit>.Success(MbcUnit.Value);
    }
}

/// <summary>
/// Property-based tests for profile deletion behavior via ProfileStripViewModel.
/// </summary>
public class ProfileDeletionProperties
{
    private static readonly char[] AllowedChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-".ToCharArray();

    private static Gen<string> ValidProfileNameGen =>
        Gen.Choose(1, 20)
            .SelectMany(len =>
                Gen.ArrayOf(len, Gen.Elements(AllowedChars))
                    .Select(chars => new string(chars)));

    private static IMonitorService EmptyMonitorService()
    {
        var service = Substitute.For<IMonitorService>();
        service.DetectMonitors().Returns(new List<MonitorState>());
        return service;
    }

    /// <summary>
    /// Property 9: Profile deletion removes from store and dropdown
    ///
    /// For any saved profile, deleting it SHALL remove it from the settings store's profile list.
    /// After deletion, the profile name SHALL no longer appear in any profile dropdown.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 3.8**
    /// </remarks>
    [Property(MaxTest = 100)]
    public Property Deletion_RemovesProfile_FromStoreAndDropdown()
    {
        // Generate a non-empty list of distinct profile names, then pick one to delete.
        var distinctNamesGen = ValidProfileNameGen
            .ListOf()
            .Where(names => names.Count > 0)
            .Select(names => names
                .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Take(10)
                .ToList())
            .Where(names => names.Count > 0);

        var inputGen =
            from names in distinctNamesGen
            from deleteIndex in Gen.Choose(0, names.Count - 1)
            select new { ProfileNames = names, DeleteIndex = deleteIndex };

        return Prop.ForAll(Arb.From(inputGen), input =>
        {
            string nameToDelete = input.ProfileNames[input.DeleteIndex];

            // Arrange: build profiles with brightness maps
            var profiles = input.ProfileNames
                .Select(name => new Profile
                {
                    Name = name,
                    MonitorBrightnessMap = new Dictionary<string, int>
                    {
                        { $"\\\\?\\DISPLAY#path_{name}", 50 }
                    },
                })
                .ToList();

            var settings = new AppSettings { Profiles = profiles };
            var store = new InMemorySettingsStore_ProfileDeletion(settings);
            var profileManager = new ProfileManager(store);
            var monitorService = EmptyMonitorService();

            var vm = new ProfileStripViewModel(profileManager, monitorService);

            // Precondition: the profile to delete is in the dropdown
            vm.ProfileNames.Should().Contain(nameToDelete);

            // Act: select the profile and confirm deletion
            vm.SelectedProfileName = nameToDelete;
            Result<MbcUnit> result = vm.ConfirmDeleteSelectedProfile();

            // Assert: deletion succeeded
            result.IsSuccess.Should().BeTrue(
                $"deleting profile '{nameToDelete}' should succeed");

            // Assert: profile no longer in the dropdown (ProfileNames collection)
            vm.ProfileNames.Should().NotContain(
                n => string.Equals(n, nameToDelete, StringComparison.OrdinalIgnoreCase),
                $"after deletion, '{nameToDelete}' should not appear in ProfileNames");

            // Assert: profile no longer in the underlying store via IProfileManager
            profileManager.GetAllProfiles()
                .Should().NotContain(
                    p => string.Equals(p.Name, nameToDelete, StringComparison.OrdinalIgnoreCase),
                    $"after deletion, '{nameToDelete}' should not be in GetAllProfiles()");

            // Assert: remaining profiles are still intact
            var expectedRemaining = input.ProfileNames
                .Where(n => !string.Equals(n, nameToDelete, StringComparison.OrdinalIgnoreCase))
                .ToList();

            profileManager.GetAllProfiles().Select(p => p.Name)
                .Should().BeEquivalentTo(expectedRemaining,
                    "all other profiles should remain after deleting one");
        });
    }
}
