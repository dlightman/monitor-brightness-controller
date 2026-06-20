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
/// In-memory <see cref="ISettingsStore"/> for startup-behavior tests. Holds the current
/// <see cref="AppSettings"/>, returns it from <see cref="Load"/>, and records the most
/// recently saved settings so tests can assert what was persisted (Requirements 5.1, 5.2).
/// </summary>
internal sealed class InMemorySettingsStore_Startup : ISettingsStore
{
    public AppSettings Current { get; private set; }

    public int SaveCount { get; private set; }

    public InMemorySettingsStore_Startup(AppSettings seed)
    {
        Current = seed;
    }

    public AppSettings Load() => Current;

    public Result<MbcUnit> Save(AppSettings settings)
    {
        Current = settings;
        SaveCount++;
        return Result<MbcUnit>.Success(MbcUnit.Value);
    }
}

/// <summary>
/// Example-based tests for GUI startup auto-apply behavior coordinated by
/// <see cref="StartupCoordinator"/> and exposed through <see cref="MainWindowViewModel"/>
/// (Requirements 5.2, 5.3, 5.4, 5.6, 5.7).
/// </summary>
public class StartupBehaviorTests
{
    private const string DevicePath = @"\\?\DISPLAY#DEL41AB#5&startup";

    private static IProfileManager ProfileManagerWith(params string[] profileNames)
    {
        var profiles = new List<Profile>();
        foreach (string name in profileNames)
        {
            profiles.Add(new Profile
            {
                Name = name,
                MonitorBrightnessMap = new Dictionary<string, int> { [DevicePath] = 50 },
            });
        }

        var manager = Substitute.For<IProfileManager>();
        manager.GetAllProfiles().Returns(profiles);
        return manager;
    }

    private static IMonitorService EmptyMonitorService()
    {
        var service = Substitute.For<IMonitorService>();
        service.DetectMonitors().Returns(new List<MonitorState>());
        return service;
    }

    // --- Pure decision logic -------------------------------------------------

    [Fact]
    public void Decide_AutoApplyDisabled_ReadsWithoutApplying()
    {
        // Requirement 5.4: disabled -> read current values without changing them.
        var settings = new AppSettings { AutoApplyOnStartup = false, LastAppliedProfileName = "focus" };

        StartupDecision decision = StartupCoordinator.Decide(settings, new List<string> { "focus" });

        decision.Action.Should().Be(StartupAction.AutoApplyDisabled);
        decision.Notice.Should().BeNull();
    }

    [Fact]
    public void Decide_EnabledAndProfileExists_AppliesProfile()
    {
        // Requirement 5.3: enabled and last profile present -> apply it.
        var settings = new AppSettings { AutoApplyOnStartup = true, LastAppliedProfileName = "focus" };

        StartupDecision decision = StartupCoordinator.Decide(settings, new List<string> { "focus" });

        decision.Action.Should().Be(StartupAction.ApplyLastProfile);
        decision.ProfileName.Should().Be("focus");
        decision.Notice.Should().BeNull();
    }

    [Fact]
    public void Decide_EnabledAndProfileExists_MatchesCaseInsensitively()
    {
        var settings = new AppSettings { AutoApplyOnStartup = true, LastAppliedProfileName = "Focus" };

        StartupDecision decision = StartupCoordinator.Decide(settings, new List<string> { "focus" });

        decision.Action.Should().Be(StartupAction.ApplyLastProfile);
    }

    [Fact]
    public void Decide_EnabledButProfileMissing_ShowsNotice()
    {
        // Requirement 5.6: enabled but last profile missing -> skip, notice, read current values.
        var settings = new AppSettings { AutoApplyOnStartup = true, LastAppliedProfileName = "gone" };

        StartupDecision decision = StartupCoordinator.Decide(settings, new List<string> { "focus" });

        decision.Action.Should().Be(StartupAction.LastProfileMissing);
        decision.Notice.Should().Contain("gone");
    }

    [Fact]
    public void Decide_EnabledButNoLastProfileRecorded_SkipsWithNoError()
    {
        // Requirement 6.10: "Last Used" selected but LastAppliedProfileName is null → skip, no error.
        var settings = new AppSettings { AutoApplyOnStartup = true, LastAppliedProfileName = null };

        StartupDecision decision = StartupCoordinator.Decide(settings, new List<string>());

        decision.Action.Should().Be(StartupAction.AutoApplyDisabled);
        decision.Notice.Should().BeNull();
    }

    // --- Run: side effects ---------------------------------------------------

    [Fact]
    public void Run_Enabled_AppliesLastProfile()
    {
        // Requirement 5.3
        var store = new InMemorySettingsStore_Startup(new AppSettings
        {
            AutoApplyOnStartup = true,
            LastAppliedProfileName = "focus",
        });
        IProfileManager profileManager = ProfileManagerWith("focus");
        profileManager.ApplyProfile("focus", Arg.Any<IMonitorService>())
            .Returns(Result<MbcUnit>.Success(MbcUnit.Value));

        var coordinator = new StartupCoordinator(store, profileManager, EmptyMonitorService());
        string? notice = coordinator.Run();

        notice.Should().BeNull();
        profileManager.Received(1).ApplyProfile("focus", Arg.Any<IMonitorService>());
    }

    [Fact]
    public void Run_Disabled_DoesNotApplyProfile()
    {
        // Requirement 5.4: read current values without modifying.
        var store = new InMemorySettingsStore_Startup(new AppSettings
        {
            AutoApplyOnStartup = false,
            LastAppliedProfileName = "focus",
        });
        IProfileManager profileManager = ProfileManagerWith("focus");

        var coordinator = new StartupCoordinator(store, profileManager, EmptyMonitorService());
        string? notice = coordinator.Run();

        notice.Should().BeNull();
        profileManager.DidNotReceive().ApplyProfile(Arg.Any<string>(), Arg.Any<IMonitorService>());
    }

    [Fact]
    public void Run_EnabledButProfileMissing_ReturnsNoticeAndDoesNotApply()
    {
        // Requirement 5.6
        var store = new InMemorySettingsStore_Startup(new AppSettings
        {
            AutoApplyOnStartup = true,
            LastAppliedProfileName = "gone",
        });
        IProfileManager profileManager = ProfileManagerWith("focus");

        var coordinator = new StartupCoordinator(store, profileManager, EmptyMonitorService());
        string? notice = coordinator.Run();

        notice.Should().Contain("gone");
        profileManager.DidNotReceive().ApplyProfile(Arg.Any<string>(), Arg.Any<IMonitorService>());
    }

    [Fact]
    public void Run_ApplyFailure_ReturnsNotice()
    {
        var store = new InMemorySettingsStore_Startup(new AppSettings
        {
            AutoApplyOnStartup = true,
            LastAppliedProfileName = "focus",
        });
        IProfileManager profileManager = ProfileManagerWith("focus");
        profileManager.ApplyProfile("focus", Arg.Any<IMonitorService>())
            .Returns(Result<MbcUnit>.Failure("no mapped monitors available"));

        var coordinator = new StartupCoordinator(store, profileManager, EmptyMonitorService());
        string? notice = coordinator.Run();

        notice.Should().Contain("focus");
        notice.Should().Contain("no mapped monitors available");
    }

    // --- View model integration ---------------------------------------------

    [Fact]
    public void ViewModel_SeedsToggleFromSettings()
    {
        // Requirement 5.2: toggle reflects persisted preference.
        var store = new InMemorySettingsStore_Startup(new AppSettings { AutoApplyOnStartup = true });
        IProfileManager profileManager = ProfileManagerWith();

        var vm = new MainWindowViewModel(EmptyMonitorService(), store, profileManager);

        vm.AutoApplyOnStartup.Should().BeTrue();
    }

    [Fact]
    public void ViewModel_DefaultsToggleToFalseOnFirstUse()
    {
        // Requirement 5.2: defaults to disabled on first use.
        var store = new InMemorySettingsStore_Startup(new AppSettings());
        IProfileManager profileManager = ProfileManagerWith();

        var vm = new MainWindowViewModel(EmptyMonitorService(), store, profileManager);

        vm.AutoApplyOnStartup.Should().BeFalse();
    }

    [Fact]
    public void ViewModel_TogglingAutoApply_PersistsToStore()
    {
        // Requirement 5.1 / 5.2: changing the preference persists it.
        var store = new InMemorySettingsStore_Startup(new AppSettings { AutoApplyOnStartup = false });
        IProfileManager profileManager = ProfileManagerWith();

        var vm = new MainWindowViewModel(EmptyMonitorService(), store, profileManager);
        vm.AutoApplyOnStartup = true;

        store.Current.AutoApplyOnStartup.Should().BeTrue();
    }

    [Fact]
    public void ViewModel_MissingProfile_SurfacesStartupNotice()
    {
        // Requirement 5.6: notice surfaced to the GUI.
        var store = new InMemorySettingsStore_Startup(new AppSettings
        {
            AutoApplyOnStartup = true,
            LastAppliedProfileName = "gone",
        });
        IProfileManager profileManager = ProfileManagerWith("focus");

        var vm = new MainWindowViewModel(EmptyMonitorService(), store, profileManager);

        vm.HasStartupNotice.Should().BeTrue();
        vm.StartupNotice.Should().Contain("gone");
    }

    // --- Run: Default Startup Profile ----------------------------------------

    [Fact]
    public void Run_WithCliOverride_SkipsProfileApplication()
    {
        // Requirement 2.5: CLI arguments override all startup profile application.
        var store = new InMemorySettingsStore_Startup(new AppSettings
        {
            DefaultStartupProfileName = "nightMode",
        });
        IProfileManager profileManager = ProfileManagerWith("nightMode");

        var coordinator = new StartupCoordinator(store, profileManager, EmptyMonitorService());
        string? notice = coordinator.Run(isCliOverride: true);

        notice.Should().BeNull();
        profileManager.DidNotReceive().ApplyProfile(Arg.Any<string>(), Arg.Any<IMonitorService>());
    }

    [Fact]
    public void Run_DefaultProfileExists_AppliesAndUpdatesLastApplied()
    {
        // Requirements 2.4, 2.8, 6.7: apply default startup profile and update LastAppliedProfileName.
        var store = new InMemorySettingsStore_Startup(new AppSettings
        {
            AutoApplyOnStartup = true,
            DefaultStartupProfileName = "nightMode",
        });
        IProfileManager profileManager = ProfileManagerWith("nightMode");
        profileManager.ApplyProfile("nightMode", Arg.Any<IMonitorService>())
            .Returns(Result<MbcUnit>.Success(MbcUnit.Value));

        var coordinator = new StartupCoordinator(store, profileManager, EmptyMonitorService());
        string? notice = coordinator.Run();

        notice.Should().BeNull();
        profileManager.Received(1).ApplyProfile("nightMode", Arg.Any<IMonitorService>());
        store.Current.LastAppliedProfileName.Should().Be("nightMode");
        store.SaveCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Run_DefaultProfileApplyFails_ReturnsNoticeButContinues()
    {
        // Requirement 2.7: disconnected monitors produce notice but don't crash.
        var store = new InMemorySettingsStore_Startup(new AppSettings
        {
            AutoApplyOnStartup = true,
            DefaultStartupProfileName = "dayMode",
        });
        IProfileManager profileManager = ProfileManagerWith("dayMode");
        profileManager.ApplyProfile("dayMode", Arg.Any<IMonitorService>())
            .Returns(Result<MbcUnit>.Failure("monitors unavailable"));

        var coordinator = new StartupCoordinator(store, profileManager, EmptyMonitorService());
        string? notice = coordinator.Run();

        notice.Should().NotBeNull();
        notice.Should().Contain("dayMode");
        notice.Should().Contain("monitors unavailable");
    }

    [Fact]
    public void Run_DefaultProfileMissing_ReturnsNoticeAndResetsToLastUsed()
    {
        // Requirements 2.6, 6.11: missing profile produces notice and resets to "Last Used" (null).
        var store = new InMemorySettingsStore_Startup(new AppSettings
        {
            AutoApplyOnStartup = true,
            DefaultStartupProfileName = "deleted",
        });
        IProfileManager profileManager = ProfileManagerWith("otherProfile");

        var coordinator = new StartupCoordinator(store, profileManager, EmptyMonitorService());
        string? notice = coordinator.Run();

        notice.Should().NotBeNull();
        notice.Should().Contain("deleted");
        store.Current.DefaultStartupProfileName.Should().BeNull("DefaultStartupProfileName should be reset to null (Last Used)");
        store.SaveCount.Should().BeGreaterThan(0, "settings should be persisted after reset");
    }

    [Fact]
    public void Run_StartWithWindowsEnabled_CallsEnsureRegistration()
    {
        // Requirement 1.4: EnsureRegistration called when StartWithWindows is enabled.
        var store = new InMemorySettingsStore_Startup(new AppSettings
        {
            StartWithWindows = true,
        });
        IProfileManager profileManager = ProfileManagerWith();
        IStartupRegistration startupReg = Substitute.For<IStartupRegistration>();
        startupReg.EnsureRegistration(Arg.Any<bool>())
            .Returns(Result<RegistrySyncResult>.Success(new RegistrySyncResult(SettingsNeedSync: false, PathWasUpdated: false)));

        var coordinator = new StartupCoordinator(store, profileManager, EmptyMonitorService(), startupReg);
        coordinator.Run();

        startupReg.Received(1).EnsureRegistration(true);
    }
}
