using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;
using MbcUnit = MonitorBrightnessController.Models.Unit;
using MonitorBrightnessController.Presentation;
using NSubstitute;

namespace MonitorBrightnessController.Tests.Properties;

// Feature: ui-consolidation, Property 2: Startup slider sync with profile application
// Feature: ui-consolidation, Property 4: Profile selection updates mapped monitors and retains unmapped

/// <summary>
/// Property-based tests for slider synchronization on startup with profile application
/// and on profile selection.
/// </summary>
public class SliderSyncProperties
{
    /// <summary>
    /// Generates a device path for testing.
    /// </summary>
    private static string MakeDevicePath(int i) => $"\\\\?\\DISPLAY#MON{i}#path{i}";

    /// <summary>
    /// Creates a MainWindowViewModel with full dependencies, configured so that startup
    /// does NOT apply any profile (AutoApplyOnStartup = false, no DefaultStartupProfileName).
    /// The monitors will be seeded with the provided initial brightness/gamma values.
    /// </summary>
    private static MainWindowViewModel CreateViewModelNoStartupProfile(
        IReadOnlyList<MonitorState> monitors,
        IProfileManager profileManager)
    {
        var monitorService = Substitute.For<IMonitorService>();
        monitorService.DetectMonitors().Returns(monitors);
        monitorService.SetBrightness(Arg.Any<int>(), Arg.Any<int>()).Returns(Result<MbcUnit>.Success(MbcUnit.Value));
        monitorService.SetGamma(Arg.Any<int>(), Arg.Any<int>()).Returns(Result<MbcUnit>.Success(MbcUnit.Value));

        var settingsStore = Substitute.For<ISettingsStore>();
        settingsStore.Load().Returns(new AppSettings
        {
            AutoApplyOnStartup = false,
            DefaultStartupProfileName = null,
            Profiles = new List<Profile>()
        });
        settingsStore.Save(Arg.Any<AppSettings>()).Returns(Result<MbcUnit>.Success(MbcUnit.Value));

        var vm = new MainWindowViewModel(monitorService, settingsStore, profileManager);
        return vm;
    }

    /// <summary>
    /// Property 2: Startup slider sync with profile application
    ///
    /// For any startup profile and set of detected monitors, when the application starts
    /// with a valid startup profile, each monitor that appears in the profile's brightness/gamma
    /// maps SHALL have its slider set to the profile-defined value, and each monitor NOT in the
    /// profile's maps SHALL have its slider set to the hardware-reported value.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 1.2**
    /// </remarks>
    [Property(MaxTest = 100)]
    public Property Startup_WithValidProfile_MappedMonitorsShowProfileValues_UnmappedShowHardware()
    {
        var scenarioGen =
            from monitorCount in Gen.Choose(2, 5)
            from hwBrightness in Gen.ArrayOf(monitorCount, Gen.Choose(0, 100))
            from hwGamma in Gen.ArrayOf(monitorCount, Gen.Choose(0, 100))
            from mappedCount in Gen.Choose(1, Math.Max(1, monitorCount - 1))
            from profileBrightness in Gen.ArrayOf(mappedCount, Gen.Choose(0, 100))
            from profileGamma in Gen.ArrayOf(mappedCount, Gen.Choose(0, 100))
            select new
            {
                MonitorCount = monitorCount,
                HwBrightness = hwBrightness,
                HwGamma = hwGamma,
                MappedCount = mappedCount,
                ProfileBrightness = profileBrightness,
                ProfileGamma = profileGamma
            };

        return Prop.ForAll(Arb.From(scenarioGen), scenario =>
        {
            // Create device paths for all monitors
            var devicePaths = Enumerable.Range(0, scenario.MonitorCount)
                .Select(i => MakeDevicePath(i))
                .ToArray();

            // Build the profile mapping only the first N monitors (mapped)
            var brightnessMap = new Dictionary<string, int>();
            var gammaMap = new Dictionary<string, int>();
            for (int i = 0; i < scenario.MappedCount; i++)
            {
                brightnessMap[devicePaths[i]] = scenario.ProfileBrightness[i];
                gammaMap[devicePaths[i]] = scenario.ProfileGamma[i];
            }

            var profile = new Profile
            {
                Name = "startup-profile",
                MonitorBrightnessMap = brightnessMap,
                MonitorGammaMap = gammaMap
            };

            // Build MonitorState list with hardware-reported values
            var monitorStates = devicePaths.Select((path, i) => new MonitorState
            {
                MonitorIndex = i + 1,
                MonitorName = $"Monitor {i + 1}",
                DevicePath = path,
                PhysicalHandle = new IntPtr(i + 1),
                CurrentBrightness = scenario.HwBrightness[i],
                CurrentGamma = scenario.HwGamma[i],
                IsControllable = true
            }).ToList();

            // Mock MonitorService
            var monitorService = Substitute.For<IMonitorService>();
            monitorService.DetectMonitors().Returns(monitorStates.AsReadOnly());
            monitorService.SetBrightness(Arg.Any<int>(), Arg.Any<int>())
                .Returns(Result<MbcUnit>.Success(MbcUnit.Value));
            monitorService.SetGamma(Arg.Any<int>(), Arg.Any<int>())
                .Returns(Result<MbcUnit>.Success(MbcUnit.Value));

            // Mock SettingsStore with DefaultStartupProfileName configured
            var settings = new AppSettings
            {
                DefaultStartupProfileName = "startup-profile",
                AutoApplyOnStartup = true,
                Profiles = new List<Profile> { profile }
            };
            var settingsStore = Substitute.For<ISettingsStore>();
            settingsStore.Load().Returns(settings);
            settingsStore.Save(Arg.Any<AppSettings>()).Returns(Result<MbcUnit>.Success(MbcUnit.Value));

            // Mock ProfileManager
            var profileManager = Substitute.For<IProfileManager>();
            profileManager.GetAllProfiles().Returns(new List<Profile> { profile });
            profileManager.GetProfile("startup-profile").Returns(Result<Profile>.Success(profile));
            profileManager.ApplyProfile("startup-profile", Arg.Any<IMonitorService>())
                .Returns(Result<MbcUnit>.Success(MbcUnit.Value));

            // Act: construct the ViewModel (triggers startup sync)
            var vm = new MainWindowViewModel(monitorService, settingsStore, profileManager);

            // Assert: monitors in the profile should have profile values
            for (int i = 0; i < scenario.MappedCount; i++)
            {
                var monitorVm = vm.Monitors[i];
                monitorVm.Brightness.Should().Be(scenario.ProfileBrightness[i],
                    $"monitor {i + 1} is mapped in the profile and should show profile brightness {scenario.ProfileBrightness[i]}");
                monitorVm.Gamma.Should().Be(scenario.ProfileGamma[i],
                    $"monitor {i + 1} is mapped in the profile and should show profile gamma {scenario.ProfileGamma[i]}");
            }

            // Assert: monitors NOT in the profile should retain hardware values
            for (int i = scenario.MappedCount; i < scenario.MonitorCount; i++)
            {
                var monitorVm = vm.Monitors[i];
                monitorVm.Brightness.Should().Be(scenario.HwBrightness[i],
                    $"monitor {i + 1} is NOT in the profile and should show hardware brightness {scenario.HwBrightness[i]}");
                monitorVm.Gamma.Should().Be(scenario.HwGamma[i],
                    $"monitor {i + 1} is NOT in the profile and should show hardware gamma {scenario.HwGamma[i]}");
            }
        });
    }

    /// <summary>
    /// Property 4: Profile selection updates mapped monitors and retains unmapped.
    /// For any profile selection and set of connected monitors, each monitor present in the
    /// profile's brightness map SHALL have its brightness slider updated to the profile value,
    /// each monitor present in the gamma map SHALL have its gamma slider updated, each monitor
    /// NOT in the brightness map SHALL retain its current brightness slider value, and each
    /// monitor NOT in the gamma map (including legacy profiles with null gamma map) SHALL retain
    /// its current gamma slider value.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 2.1, 2.2, 2.3**
    /// </remarks>
    [Property(MaxTest = 100)]
    public Property ProfileSelection_UpdatesMappedMonitors_RetainsUnmapped()
    {
        var gen =
            from totalMonitors in Gen.Choose(2, 6)
            from mappedCount in Gen.Choose(1, totalMonitors - 1)
            from initialBrightness in Gen.ArrayOf(totalMonitors, Gen.Choose(0, 100))
            from initialGamma in Gen.ArrayOf(totalMonitors, Gen.Choose(0, 100))
            from profileBrightness in Gen.ArrayOf(mappedCount, Gen.Choose(0, 100))
            from profileGamma in Gen.ArrayOf(mappedCount, Gen.Choose(0, 100))
            select new
            {
                TotalMonitors = totalMonitors,
                MappedCount = mappedCount,
                InitialBrightness = initialBrightness,
                InitialGamma = initialGamma,
                ProfileBrightness = profileBrightness,
                ProfileGamma = profileGamma
            };

        return Prop.ForAll(Arb.From(gen), data =>
        {
            // Create monitor states with initial values
            var monitors = Enumerable.Range(0, data.TotalMonitors).Select(i => new MonitorState
            {
                MonitorIndex = i + 1,
                MonitorName = $"Monitor {i + 1}",
                DevicePath = MakeDevicePath(i),
                PhysicalHandle = new IntPtr(i + 1),
                IsControllable = true,
                CurrentBrightness = data.InitialBrightness[i],
                CurrentGamma = data.InitialGamma[i]
            }).ToList();

            // Build profile that maps only the first 'mappedCount' monitors
            var brightnessMap = new Dictionary<string, int>();
            var gammaMap = new Dictionary<string, int>();
            for (int i = 0; i < data.MappedCount; i++)
            {
                brightnessMap[MakeDevicePath(i)] = data.ProfileBrightness[i];
                gammaMap[MakeDevicePath(i)] = data.ProfileGamma[i];
            }

            var profile = new Profile
            {
                Name = "test-profile",
                MonitorBrightnessMap = brightnessMap,
                MonitorGammaMap = gammaMap
            };

            var profileManager = Substitute.For<IProfileManager>();
            profileManager.GetProfile("test-profile").Returns(Result<Profile>.Success(profile));
            profileManager.GetAllProfiles().Returns(new List<Profile> { profile });

            // Create ViewModel (monitors will be populated via DetectMonitors in constructor)
            var vm = CreateViewModelNoStartupProfile(monitors, profileManager);

            // Verify monitors were loaded with initial values before preview
            vm.Monitors.Should().HaveCount(data.TotalMonitors);

            // Call PreviewProfile
            vm.PreviewProfile("test-profile");

            // Assert: mapped monitors have profile brightness/gamma values
            for (int i = 0; i < data.MappedCount; i++)
            {
                vm.Monitors[i].Brightness.Should().Be(data.ProfileBrightness[i],
                    $"mapped monitor {i} brightness should be updated to profile value");
                vm.Monitors[i].Gamma.Should().Be(data.ProfileGamma[i],
                    $"mapped monitor {i} gamma should be updated to profile value");
            }

            // Assert: unmapped monitors retain their initial brightness/gamma values
            for (int i = data.MappedCount; i < data.TotalMonitors; i++)
            {
                vm.Monitors[i].Brightness.Should().Be(data.InitialBrightness[i],
                    $"unmapped monitor {i} brightness should retain its initial value");
                vm.Monitors[i].Gamma.Should().Be(data.InitialGamma[i],
                    $"unmapped monitor {i} gamma should retain its initial value");
            }
        });
    }

    /// <summary>
    /// Property 4 (legacy profile case): For legacy profiles where MonitorGammaMap is null,
    /// all gamma sliders SHALL retain their current values regardless of brightness mapping.
    /// Brightness-mapped monitors still get their brightness updated.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 2.1, 2.2, 2.3**
    /// </remarks>
    [Property(MaxTest = 100)]
    public Property ProfileSelection_LegacyProfile_RetainsAllGammaValues()
    {
        var gen =
            from totalMonitors in Gen.Choose(1, 6)
            from mappedCount in Gen.Choose(1, totalMonitors)
            from initialBrightness in Gen.ArrayOf(totalMonitors, Gen.Choose(0, 100))
            from initialGamma in Gen.ArrayOf(totalMonitors, Gen.Choose(0, 100))
            from profileBrightness in Gen.ArrayOf(mappedCount, Gen.Choose(0, 100))
            select new
            {
                TotalMonitors = totalMonitors,
                MappedCount = mappedCount,
                InitialBrightness = initialBrightness,
                InitialGamma = initialGamma,
                ProfileBrightness = profileBrightness
            };

        return Prop.ForAll(Arb.From(gen), data =>
        {
            // Create monitor states with initial values
            var monitors = Enumerable.Range(0, data.TotalMonitors).Select(i => new MonitorState
            {
                MonitorIndex = i + 1,
                MonitorName = $"Monitor {i + 1}",
                DevicePath = MakeDevicePath(i),
                PhysicalHandle = new IntPtr(i + 1),
                IsControllable = true,
                CurrentBrightness = data.InitialBrightness[i],
                CurrentGamma = data.InitialGamma[i]
            }).ToList();

            // Build legacy profile: brightness map for first 'mappedCount' monitors, null gamma map
            var brightnessMap = new Dictionary<string, int>();
            for (int i = 0; i < data.MappedCount; i++)
            {
                brightnessMap[MakeDevicePath(i)] = data.ProfileBrightness[i];
            }

            var profile = new Profile
            {
                Name = "legacy-profile",
                MonitorBrightnessMap = brightnessMap,
                MonitorGammaMap = null // Legacy profile — no gamma map
            };

            var profileManager = Substitute.For<IProfileManager>();
            profileManager.GetProfile("legacy-profile").Returns(Result<Profile>.Success(profile));
            profileManager.GetAllProfiles().Returns(new List<Profile> { profile });

            // Create ViewModel
            var vm = CreateViewModelNoStartupProfile(monitors, profileManager);

            // Call PreviewProfile
            vm.PreviewProfile("legacy-profile");

            // Assert: mapped monitors have profile brightness values
            for (int i = 0; i < data.MappedCount; i++)
            {
                vm.Monitors[i].Brightness.Should().Be(data.ProfileBrightness[i],
                    $"mapped monitor {i} brightness should be updated to profile value");
            }

            // Assert: unmapped monitors retain their initial brightness values
            for (int i = data.MappedCount; i < data.TotalMonitors; i++)
            {
                vm.Monitors[i].Brightness.Should().Be(data.InitialBrightness[i],
                    $"unmapped monitor {i} brightness should retain its initial value");
            }

            // Assert: ALL monitors retain their initial gamma values (legacy profile, null gamma map)
            for (int i = 0; i < data.TotalMonitors; i++)
            {
                vm.Monitors[i].Gamma.Should().Be(data.InitialGamma[i],
                    $"monitor {i} gamma should retain its initial value for legacy profile (null gamma map)");
            }
        });
    }
}
