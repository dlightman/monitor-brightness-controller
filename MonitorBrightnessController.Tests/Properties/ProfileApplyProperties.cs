using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using MonitorBrightnessController.Application;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;
using MbcUnit = MonitorBrightnessController.Models.Unit;
using NSubstitute;

namespace MonitorBrightnessController.Tests.Properties;

// Feature: gamma-control, Property 12: Profile apply targets only connected monitors with both settings
// Feature: gamma-control, Property 13: Profile apply partial failure reports all errors
// Feature: gamma-control, Property 14: Legacy profile apply sends no gamma commands
// Feature: gamma-control, Property 15: Brightness and gamma applied independently per monitor

/// <summary>
/// Property-based tests for profile application with gamma support.
/// Tests connected monitor targeting, partial failure reporting, legacy profile behavior,
/// and independent brightness/gamma application per monitor.
/// </summary>
public class ProfileApplyProperties
{
    /// <summary>
    /// Creates a configured ISettingsStore mock that returns settings with the given profile.
    /// Save always succeeds.
    /// </summary>
    private static ISettingsStore CreateSettingsStore(Profile profile)
    {
        var store = Substitute.For<ISettingsStore>();
        store.Load().Returns(new AppSettings
        {
            Profiles = new List<Profile> { profile }
        });
        store.Save(Arg.Any<AppSettings>()).Returns(Result<MbcUnit>.Success(MbcUnit.Value));
        return store;
    }

    /// <summary>
    /// Creates a mock IMonitorService that reports the given set of connected monitors.
    /// SetBrightness and SetGamma return success by default.
    /// </summary>
    private static IMonitorService CreateMonitorService(IReadOnlyList<MonitorState> connectedMonitors)
    {
        var service = Substitute.For<IMonitorService>();
        service.DetectMonitors().Returns(connectedMonitors);
        service.SetBrightness(Arg.Any<int>(), Arg.Any<int>()).Returns(Result<MbcUnit>.Success(MbcUnit.Value));
        service.SetGamma(Arg.Any<int>(), Arg.Any<int>()).Returns(Result<MbcUnit>.Success(MbcUnit.Value));
        return service;
    }

    /// <summary>
    /// Generates a device path for testing.
    /// </summary>
    private static string MakeDevicePath(int i) => $"\\\\?\\DISPLAY#MON{i}#path{i}";

    /// <summary>
    /// Property 12: For any Profile with both brightness and gamma mappings, and any set of currently
    /// connected monitors, applying the profile invokes SetBrightness and SetGamma only on monitors
    /// whose device paths appear in both the profile mapping and the connected set.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 6.5, 7.1, 7.2**
    /// </remarks>
    [Property(MaxTest = 100)]
    public Property ProfileApply_TargetsOnlyConnectedMonitors()
    {
        // Generate total monitor count (profile maps), and a subset that is connected
        var gen =
            from totalCount in Gen.Choose(1, 6)
            from connectedFlags in Gen.ArrayOf(totalCount, Gen.Elements(true, false))
            // Ensure at least one is connected (otherwise ApplyProfile returns failure for "no connected monitors")
            let ensureOneConnected = connectedFlags.Any(f => f)
                ? connectedFlags
                : connectedFlags.Select((f, i) => i == 0 || f).ToArray()
            from brightnessValues in Gen.ArrayOf(totalCount, Gen.Choose(0, 100))
            from gammaValues in Gen.ArrayOf(totalCount, Gen.Choose(0, 100))
            select new
            {
                TotalCount = totalCount,
                ConnectedFlags = ensureOneConnected,
                BrightnessValues = brightnessValues,
                GammaValues = gammaValues
            };

        return Prop.ForAll(Arb.From(gen), data =>
        {
            var paths = Enumerable.Range(0, data.TotalCount).Select(MakeDevicePath).ToArray();

            // Build profile with all monitors
            var profile = new Profile
            {
                Name = "test-profile",
                MonitorBrightnessMap = paths.Zip(data.BrightnessValues)
                    .ToDictionary(p => p.First, p => p.Second),
                MonitorGammaMap = paths.Zip(data.GammaValues)
                    .ToDictionary(p => p.First, p => p.Second)
            };

            // Build connected monitor list (only those flagged as connected)
            var connectedMonitors = paths
                .Select((path, i) => new { Path = path, Index = i, Connected = data.ConnectedFlags[i] })
                .Where(x => x.Connected)
                .Select((x, ordinal) => new MonitorState
                {
                    MonitorIndex = ordinal + 1,
                    MonitorName = $"Monitor {ordinal + 1}",
                    DevicePath = x.Path,
                    PhysicalHandle = new IntPtr(x.Index + 1),
                    IsControllable = true,
                    CurrentBrightness = 50,
                    CurrentGamma = 50
                })
                .ToList();

            var settingsStore = CreateSettingsStore(profile);
            var monitorService = CreateMonitorService(connectedMonitors);
            var profileManager = new ProfileManager(settingsStore);

            // Apply
            profileManager.ApplyProfile("test-profile", monitorService);

            // Verify: SetBrightness called only for connected monitors
            var connectedPaths = new HashSet<string>(connectedMonitors.Select(m => m.DevicePath));
            foreach (var path in paths)
            {
                var monitor = connectedMonitors.FirstOrDefault(m => m.DevicePath == path);
                if (monitor != null)
                {
                    // Connected: should have been called
                    monitorService.Received().SetBrightness(monitor.MonitorIndex, data.BrightnessValues[Array.IndexOf(paths, path)]);
                    monitorService.Received().SetGamma(monitor.MonitorIndex, data.GammaValues[Array.IndexOf(paths, path)]);
                }
            }

            // Verify: no calls to indices that don't correspond to connected monitors
            var connectedIndices = connectedMonitors.Select(m => m.MonitorIndex).ToHashSet();
            foreach (var call in monitorService.ReceivedCalls()
                .Where(c => c.GetMethodInfo().Name == "SetBrightness" || c.GetMethodInfo().Name == "SetGamma"))
            {
                var indexArg = (int)call.GetArguments()[0]!;
                connectedIndices.Should().Contain(indexArg,
                    "only connected monitor indices should receive set commands");
            }
        });
    }

    /// <summary>
    /// Property 13: For any profile application where SetBrightness or SetGamma fails on one or more
    /// monitors, the operation attempts all mapped connected monitors and returns a failure result
    /// containing error descriptions for each failed monitor and setting.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 7.3**
    /// </remarks>
    [Property(MaxTest = 100)]
    public Property ProfileApply_PartialFailure_ReportsAllErrors()
    {
        var gen =
            from monitorCount in Gen.Choose(2, 5)
            from failFlags in Gen.ArrayOf(monitorCount, Gen.Elements(true, false))
            // Ensure at least one failure
            let ensureOneFail = failFlags.Any(f => f)
                ? failFlags
                : failFlags.Select((f, i) => i == 0 || f).ToArray()
            from brightnessValues in Gen.ArrayOf(monitorCount, Gen.Choose(0, 100))
            from gammaValues in Gen.ArrayOf(monitorCount, Gen.Choose(0, 100))
            select new
            {
                MonitorCount = monitorCount,
                FailFlags = ensureOneFail,
                BrightnessValues = brightnessValues,
                GammaValues = gammaValues
            };

        return Prop.ForAll(Arb.From(gen), data =>
        {
            var paths = Enumerable.Range(0, data.MonitorCount).Select(MakeDevicePath).ToArray();

            var profile = new Profile
            {
                Name = "fail-profile",
                MonitorBrightnessMap = paths.Zip(data.BrightnessValues)
                    .ToDictionary(p => p.First, p => p.Second),
                MonitorGammaMap = paths.Zip(data.GammaValues)
                    .ToDictionary(p => p.First, p => p.Second)
            };

            // All monitors are connected
            var connectedMonitors = paths.Select((path, i) => new MonitorState
            {
                MonitorIndex = i + 1,
                MonitorName = $"Monitor {i + 1}",
                DevicePath = path,
                PhysicalHandle = new IntPtr(i + 1),
                IsControllable = true,
                CurrentBrightness = 50,
                CurrentGamma = 50
            }).ToList();

            var settingsStore = CreateSettingsStore(profile);
            var monitorService = Substitute.For<IMonitorService>();
            monitorService.DetectMonitors().Returns(connectedMonitors.AsReadOnly());

            // Configure failures: monitors marked as fail will have SetBrightness fail
            int expectedFailCount = 0;
            for (int i = 0; i < data.MonitorCount; i++)
            {
                if (data.FailFlags[i])
                {
                    monitorService.SetBrightness(i + 1, Arg.Any<int>())
                        .Returns(Result<MbcUnit>.Failure($"Brightness failed on monitor {i + 1}"));
                    expectedFailCount++;
                }
                else
                {
                    monitorService.SetBrightness(i + 1, Arg.Any<int>())
                        .Returns(Result<MbcUnit>.Success(MbcUnit.Value));
                }

                // SetGamma always succeeds in this test
                monitorService.SetGamma(i + 1, Arg.Any<int>())
                    .Returns(Result<MbcUnit>.Success(MbcUnit.Value));
            }

            var profileManager = new ProfileManager(settingsStore);
            var result = profileManager.ApplyProfile("fail-profile", monitorService);

            // Should return failure with accumulated errors
            result.IsSuccess.Should().BeFalse(
                "at least one SetBrightness failed so the overall result should be failure");
            result.Error.Should().NotBeNullOrWhiteSpace();

            // Verify all monitors were attempted (all received SetBrightness calls)
            for (int i = 0; i < data.MonitorCount; i++)
            {
                monitorService.Received().SetBrightness(i + 1, data.BrightnessValues[i]);
            }

            // Verify error message contains references to failed monitors
            for (int i = 0; i < data.MonitorCount; i++)
            {
                if (data.FailFlags[i])
                {
                    result.Error.Should().Contain($"monitor {i + 1}",
                        "error message should identify each failed monitor");
                }
            }
        });
    }

    /// <summary>
    /// Property 14: For any Profile where MonitorGammaMap is null, applying the profile invokes
    /// only SetBrightness on connected mapped monitors and does NOT invoke SetGamma on any monitor.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 8.2**
    /// </remarks>
    [Property(MaxTest = 100)]
    public Property LegacyProfileApply_SendsNoGammaCommands()
    {
        var gen =
            from monitorCount in Gen.Choose(1, 5)
            from brightnessValues in Gen.ArrayOf(monitorCount, Gen.Choose(0, 100))
            select new
            {
                MonitorCount = monitorCount,
                BrightnessValues = brightnessValues
            };

        return Prop.ForAll(Arb.From(gen), data =>
        {
            var paths = Enumerable.Range(0, data.MonitorCount).Select(MakeDevicePath).ToArray();

            // Legacy profile: gamma map is null
            var profile = new Profile
            {
                Name = "legacy-profile",
                MonitorBrightnessMap = paths.Zip(data.BrightnessValues)
                    .ToDictionary(p => p.First, p => p.Second),
                MonitorGammaMap = null
            };

            // All monitors are connected
            var connectedMonitors = paths.Select((path, i) => new MonitorState
            {
                MonitorIndex = i + 1,
                MonitorName = $"Monitor {i + 1}",
                DevicePath = path,
                PhysicalHandle = new IntPtr(i + 1),
                IsControllable = true,
                CurrentBrightness = 50,
                CurrentGamma = 50
            }).ToList();

            var settingsStore = CreateSettingsStore(profile);
            var monitorService = CreateMonitorService(connectedMonitors);
            var profileManager = new ProfileManager(settingsStore);

            // Apply
            var result = profileManager.ApplyProfile("legacy-profile", monitorService);

            result.IsSuccess.Should().BeTrue(
                "a legacy profile with all connected monitors should apply successfully");

            // Verify SetBrightness was called for each monitor
            for (int i = 0; i < data.MonitorCount; i++)
            {
                monitorService.Received().SetBrightness(i + 1, data.BrightnessValues[i]);
            }

            // Verify SetGamma was NEVER called (legacy profile has null gamma map)
            monitorService.DidNotReceive().SetGamma(Arg.Any<int>(), Arg.Any<int>());
        });
    }

    /// <summary>
    /// Property 15: For any monitor during profile application, a failure in SetBrightness does NOT
    /// prevent SetGamma from being attempted on that same monitor, and vice versa.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 7.5**
    /// </remarks>
    [Property(MaxTest = 100)]
    public Property BrightnessAndGamma_AppliedIndependentlyPerMonitor()
    {
        // Generate scenarios where brightness fails on some monitors, gamma fails on others
        var gen =
            from monitorCount in Gen.Choose(1, 5)
            from brightnessFailFlags in Gen.ArrayOf(monitorCount, Gen.Elements(true, false))
            from gammaFailFlags in Gen.ArrayOf(monitorCount, Gen.Elements(true, false))
            from brightnessValues in Gen.ArrayOf(monitorCount, Gen.Choose(0, 100))
            from gammaValues in Gen.ArrayOf(monitorCount, Gen.Choose(0, 100))
            select new
            {
                MonitorCount = monitorCount,
                BrightnessFailFlags = brightnessFailFlags,
                GammaFailFlags = gammaFailFlags,
                BrightnessValues = brightnessValues,
                GammaValues = gammaValues
            };

        return Prop.ForAll(Arb.From(gen), data =>
        {
            var paths = Enumerable.Range(0, data.MonitorCount).Select(MakeDevicePath).ToArray();

            var profile = new Profile
            {
                Name = "independent-profile",
                MonitorBrightnessMap = paths.Zip(data.BrightnessValues)
                    .ToDictionary(p => p.First, p => p.Second),
                MonitorGammaMap = paths.Zip(data.GammaValues)
                    .ToDictionary(p => p.First, p => p.Second)
            };

            var connectedMonitors = paths.Select((path, i) => new MonitorState
            {
                MonitorIndex = i + 1,
                MonitorName = $"Monitor {i + 1}",
                DevicePath = path,
                PhysicalHandle = new IntPtr(i + 1),
                IsControllable = true,
                CurrentBrightness = 50,
                CurrentGamma = 50
            }).ToList();

            var settingsStore = CreateSettingsStore(profile);
            var monitorService = Substitute.For<IMonitorService>();
            monitorService.DetectMonitors().Returns(connectedMonitors.AsReadOnly());

            // Configure per-monitor failures independently
            for (int i = 0; i < data.MonitorCount; i++)
            {
                if (data.BrightnessFailFlags[i])
                {
                    monitorService.SetBrightness(i + 1, Arg.Any<int>())
                        .Returns(Result<MbcUnit>.Failure($"Brightness DDC/CI error on monitor {i + 1}"));
                }
                else
                {
                    monitorService.SetBrightness(i + 1, Arg.Any<int>())
                        .Returns(Result<MbcUnit>.Success(MbcUnit.Value));
                }

                if (data.GammaFailFlags[i])
                {
                    monitorService.SetGamma(i + 1, Arg.Any<int>())
                        .Returns(Result<MbcUnit>.Failure($"Gamma DDC/CI error on monitor {i + 1}"));
                }
                else
                {
                    monitorService.SetGamma(i + 1, Arg.Any<int>())
                        .Returns(Result<MbcUnit>.Success(MbcUnit.Value));
                }
            }

            var profileManager = new ProfileManager(settingsStore);
            profileManager.ApplyProfile("independent-profile", monitorService);

            // KEY PROPERTY: Regardless of brightness failure, SetGamma is still called on each monitor.
            // And regardless of gamma failure, SetBrightness is still called on each monitor.
            for (int i = 0; i < data.MonitorCount; i++)
            {
                // SetBrightness should always be called (regardless of gamma failures)
                monitorService.Received().SetBrightness(i + 1, data.BrightnessValues[i]);

                // SetGamma should always be called (regardless of brightness failures)
                monitorService.Received().SetGamma(i + 1, data.GammaValues[i]);
            }
        });
    }
}
