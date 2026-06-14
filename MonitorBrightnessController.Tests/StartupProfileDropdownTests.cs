using System.Collections.Generic;
using FluentAssertions;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;
using MonitorBrightnessController.Presentation;
using NSubstitute;
using Xunit;

namespace MonitorBrightnessController.Tests;

/// <summary>
/// In-memory <see cref="ISettingsStore"/> for startup-profile-dropdown tests.
/// Supports simulated save failures to verify revert behavior (Requirement 3.6).
/// </summary>
internal sealed class InMemorySettingsStore_Dropdown : ISettingsStore
{
    public AppSettings Current { get; private set; }
    public bool FailOnNextSave { get; set; }

    public InMemorySettingsStore_Dropdown(AppSettings seed)
    {
        Current = seed;
    }

    public AppSettings Load() => Current;

    public Result<Unit> Save(AppSettings settings)
    {
        if (FailOnNextSave)
        {
            FailOnNextSave = false;
            return Result<Unit>.Failure("Simulated save failure");
        }

        Current = settings;
        return Result<Unit>.Success(Unit.Value);
    }
}

/// <summary>
/// Unit tests for the startup profile dropdown behavior in <see cref="MainWindowViewModel"/>
/// (Requirements 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7).
/// </summary>
public class StartupProfileDropdownTests
{
    private static IMonitorService EmptyMonitorService()
    {
        var service = Substitute.For<IMonitorService>();
        service.DetectMonitors().Returns(new List<MonitorState>());
        return service;
    }

    private static IProfileManager EmptyProfileManager()
    {
        var manager = Substitute.For<IProfileManager>();
        manager.GetAllProfiles().Returns(new List<Profile>());
        return manager;
    }

    private static List<Profile> MakeProfiles(params string[] names)
    {
        var profiles = new List<Profile>();
        foreach (string name in names)
        {
            profiles.Add(new Profile
            {
                Name = name,
                MonitorBrightnessMap = new Dictionary<string, int> { [@"\\?\DISPLAY#TEST"] = 50 },
            });
        }

        return profiles;
    }

    /// <summary>
    /// Requirement 3.1: Dropdown contains "None" followed by all profile names in store order.
    /// </summary>
    [Fact]
    public void Dropdown_ContainsNonePlusAllProfileNames()
    {
        var store = new InMemorySettingsStore_Dropdown(new AppSettings
        {
            Profiles = MakeProfiles("DayMode", "NightMode", "Gaming"),
        });

        var vm = new MainWindowViewModel(EmptyMonitorService(), store, EmptyProfileManager());

        vm.AvailableProfilesForStartup.Should().HaveCount(4);
        vm.AvailableProfilesForStartup[0].Should().Be("None");
        vm.AvailableProfilesForStartup[1].Should().Be("DayMode");
        vm.AvailableProfilesForStartup[2].Should().Be("NightMode");
        vm.AvailableProfilesForStartup[3].Should().Be("Gaming");
    }

    /// <summary>
    /// Requirement 3.3: Selecting a profile persists DefaultStartupProfileName immediately.
    /// </summary>
    [Fact]
    public void SelectingProfile_PersistsDefaultStartupProfileName()
    {
        var store = new InMemorySettingsStore_Dropdown(new AppSettings
        {
            Profiles = MakeProfiles("DayMode", "NightMode"),
        });

        var vm = new MainWindowViewModel(EmptyMonitorService(), store, EmptyProfileManager());
        vm.SelectedStartupProfile = "NightMode";

        store.Current.DefaultStartupProfileName.Should().Be("NightMode");
    }

    /// <summary>
    /// Requirement 3.4: Selecting "None" sets DefaultStartupProfileName to null.
    /// </summary>
    [Fact]
    public void SelectingNone_SetsDefaultStartupProfileNameToNull()
    {
        var store = new InMemorySettingsStore_Dropdown(new AppSettings
        {
            Profiles = MakeProfiles("DayMode"),
            DefaultStartupProfileName = "DayMode",
        });

        var vm = new MainWindowViewModel(EmptyMonitorService(), store, EmptyProfileManager());
        vm.SelectedStartupProfile = "None";

        store.Current.DefaultStartupProfileName.Should().BeNull();
    }

    /// <summary>
    /// Requirement 3.6: On persist failure, selection reverts to previous value and error is shown.
    /// </summary>
    [Fact]
    public void PersistFailure_RevertsSelectionToPreviousValue()
    {
        var store = new InMemorySettingsStore_Dropdown(new AppSettings
        {
            Profiles = MakeProfiles("DayMode", "NightMode"),
            DefaultStartupProfileName = "DayMode",
        });

        var vm = new MainWindowViewModel(EmptyMonitorService(), store, EmptyProfileManager());
        vm.SelectedStartupProfile.Should().Be("DayMode");

        store.FailOnNextSave = true;
        vm.SelectedStartupProfile = "NightMode";

        vm.SelectedStartupProfile.Should().Be("DayMode");
        vm.HasStartupProfileError.Should().BeTrue();
        vm.StartupProfileError.Should().Contain("Simulated save failure");
    }

    /// <summary>
    /// Requirement 3.5: When the default profile is deleted, selection resets to "None".
    /// </summary>
    [Fact]
    public void ProfileDeletion_WhenDefault_ResetsSelectionToNone()
    {
        var store = new InMemorySettingsStore_Dropdown(new AppSettings
        {
            Profiles = MakeProfiles("DayMode", "NightMode"),
            DefaultStartupProfileName = "DayMode",
        });

        var vm = new MainWindowViewModel(EmptyMonitorService(), store, EmptyProfileManager());
        vm.SelectedStartupProfile.Should().Be("DayMode");

        // Simulate the profile being deleted from the store
        store.Save(new AppSettings
        {
            Profiles = MakeProfiles("NightMode"),
            DefaultStartupProfileName = "DayMode",
        });

        vm.NotifyProfileDeleted("DayMode");

        vm.SelectedStartupProfile.Should().Be("None");
        store.Current.DefaultStartupProfileName.Should().BeNull();
    }

    /// <summary>
    /// Requirement 3.7: NotifyProfileCreated refreshes the dropdown with the updated profile list.
    /// </summary>
    [Fact]
    public void NotifyProfileCreated_RefreshesDropdownList()
    {
        var store = new InMemorySettingsStore_Dropdown(new AppSettings
        {
            Profiles = MakeProfiles("DayMode"),
        });

        var vm = new MainWindowViewModel(EmptyMonitorService(), store, EmptyProfileManager());
        vm.AvailableProfilesForStartup.Should().HaveCount(2); // "None" + "DayMode"

        // Simulate a new profile being created (added to the store)
        store.Save(new AppSettings
        {
            Profiles = MakeProfiles("DayMode", "NewProfile"),
        });

        vm.NotifyProfileCreated();

        vm.AvailableProfilesForStartup.Should().HaveCount(3);
        vm.AvailableProfilesForStartup.Should().Contain("NewProfile");
    }

    /// <summary>
    /// Requirement 3.2: When DefaultStartupProfileName references a non-existent profile,
    /// selection resolves to "None".
    /// </summary>
    [Fact]
    public void NonExistentDefaultProfile_ResolvesToNone()
    {
        var store = new InMemorySettingsStore_Dropdown(new AppSettings
        {
            Profiles = MakeProfiles("DayMode", "NightMode"),
            DefaultStartupProfileName = "DeletedProfile",
        });

        var vm = new MainWindowViewModel(EmptyMonitorService(), store, EmptyProfileManager());

        vm.SelectedStartupProfile.Should().Be("None");
    }
}
