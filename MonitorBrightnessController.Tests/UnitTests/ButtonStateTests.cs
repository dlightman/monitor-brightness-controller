using System.Collections.Generic;
using FluentAssertions;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;
using MonitorBrightnessController.Presentation;
using NSubstitute;
using Xunit;
using MbcUnit = MonitorBrightnessController.Models.Unit;

namespace MonitorBrightnessController.Tests.UnitTests;

/// <summary>
/// Unit tests verifying button enabled/disabled states for the Profile Strip and
/// Create Shortcut section.
/// Requirements: 3.14 (Apply/Update/Delete disabled when no selection),
///               5.5 (Create Shortcut disabled when no selection),
///               3.9 (Save As New always enabled).
/// </summary>
public class ButtonStateTests
{
    private static IMonitorService EmptyMonitorService()
    {
        var service = Substitute.For<IMonitorService>();
        service.DetectMonitors().Returns(new List<MonitorState>());
        return service;
    }

    private static IProfileManager ProfileManagerWith(params string[] names)
    {
        var manager = Substitute.For<IProfileManager>();
        var profiles = new List<Profile>();
        foreach (string name in names)
        {
            profiles.Add(new Profile
            {
                Name = name,
                MonitorBrightnessMap = new Dictionary<string, int> { [@"\\?\DISPLAY#TEST"] = 50 },
            });
        }
        manager.GetAllProfiles().Returns(profiles);
        return manager;
    }

    private static ISettingsStore SettingsStoreWith(params string[] profileNames)
    {
        var profiles = new List<Profile>();
        foreach (string name in profileNames)
        {
            profiles.Add(new Profile
            {
                Name = name,
                MonitorBrightnessMap = new Dictionary<string, int> { [@"\\?\DISPLAY#TEST"] = 50 },
            });
        }

        var store = Substitute.For<ISettingsStore>();
        store.Load().Returns(new AppSettings { Profiles = profiles });
        store.Save(Arg.Any<AppSettings>()).Returns(Result<MbcUnit>.Success(MbcUnit.Value));
        return store;
    }

    #region ProfileStripViewModel - Apply, Update, Delete disabled when no selection (Req 3.14)

    [Fact]
    public void ProfileStrip_NoSelection_CanApplyIsFalse()
    {
        var vm = new ProfileStripViewModel(ProfileManagerWith("Profile1"), EmptyMonitorService());
        vm.SelectedProfileName = null;

        vm.CanApply.Should().BeFalse();
    }

    [Fact]
    public void ProfileStrip_NoSelection_CanUpdateIsFalse()
    {
        var vm = new ProfileStripViewModel(ProfileManagerWith("Profile1"), EmptyMonitorService());
        vm.SelectedProfileName = null;

        vm.CanUpdate.Should().BeFalse();
    }

    [Fact]
    public void ProfileStrip_NoSelection_CanDeleteIsFalse()
    {
        var vm = new ProfileStripViewModel(ProfileManagerWith("Profile1"), EmptyMonitorService());
        vm.SelectedProfileName = null;

        vm.CanDelete.Should().BeFalse();
    }

    [Fact]
    public void ProfileStrip_NoSelection_ApplyCommandCanExecuteIsFalse()
    {
        var vm = new ProfileStripViewModel(ProfileManagerWith("Profile1"), EmptyMonitorService());
        vm.SelectedProfileName = null;

        vm.ApplyCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void ProfileStrip_NoSelection_UpdateCommandCanExecuteIsFalse()
    {
        var vm = new ProfileStripViewModel(ProfileManagerWith("Profile1"), EmptyMonitorService());
        vm.SelectedProfileName = null;

        vm.UpdateCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void ProfileStrip_NoSelection_DeleteCommandCanExecuteIsFalse()
    {
        var vm = new ProfileStripViewModel(ProfileManagerWith("Profile1"), EmptyMonitorService());
        vm.SelectedProfileName = null;

        vm.DeleteCommand.CanExecute(null).Should().BeFalse();
    }

    #endregion

    #region ProfileStripViewModel - Apply, Update, Delete enabled when profile selected (Req 3.14)

    [Fact]
    public void ProfileStrip_WithSelection_CanApplyIsTrue()
    {
        var vm = new ProfileStripViewModel(ProfileManagerWith("Profile1"), EmptyMonitorService());
        vm.SelectedProfileName = "Profile1";

        vm.CanApply.Should().BeTrue();
    }

    [Fact]
    public void ProfileStrip_WithSelection_CanUpdateIsTrue()
    {
        var vm = new ProfileStripViewModel(ProfileManagerWith("Profile1"), EmptyMonitorService());
        vm.SelectedProfileName = "Profile1";

        vm.CanUpdate.Should().BeTrue();
    }

    [Fact]
    public void ProfileStrip_WithSelection_CanDeleteIsTrue()
    {
        var vm = new ProfileStripViewModel(ProfileManagerWith("Profile1"), EmptyMonitorService());
        vm.SelectedProfileName = "Profile1";

        vm.CanDelete.Should().BeTrue();
    }

    #endregion

    #region MainWindowViewModel - Create Shortcut disabled when no profile selected (Req 5.5)

    [Fact]
    public void MainVM_NoShortcutProfileSelected_CanCreateShortcutIsFalse()
    {
        var vm = new MainWindowViewModel(
            EmptyMonitorService(),
            SettingsStoreWith("Profile1"),
            ProfileManagerWith("Profile1"));

        vm.SelectedShortcutProfile.Should().BeNull();
        vm.CanCreateShortcut.Should().BeFalse();
    }

    [Fact]
    public void MainVM_ShortcutProfileSelected_CanCreateShortcutIsTrue()
    {
        var vm = new MainWindowViewModel(
            EmptyMonitorService(),
            SettingsStoreWith("Profile1"),
            ProfileManagerWith("Profile1"));

        vm.SelectedShortcutProfile = "Profile1";

        vm.CanCreateShortcut.Should().BeTrue();
    }

    [Fact]
    public void MainVM_CreateShortcutCommand_DisabledWhenNoSelection()
    {
        var vm = new MainWindowViewModel(
            EmptyMonitorService(),
            SettingsStoreWith("Profile1"),
            ProfileManagerWith("Profile1"));

        vm.CreateShortcutCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void MainVM_CreateShortcutCommand_EnabledWhenProfileSelected()
    {
        var vm = new MainWindowViewModel(
            EmptyMonitorService(),
            SettingsStoreWith("Profile1"),
            ProfileManagerWith("Profile1"));

        vm.SelectedShortcutProfile = "Profile1";

        vm.CreateShortcutCommand.CanExecute(null).Should().BeTrue();
    }

    #endregion

    #region ProfileStripViewModel - Save As New always enabled (Req 3.9)

    [Fact]
    public void ProfileStrip_SaveAsNewCommand_AlwaysCanExecute_WhenNoSelection()
    {
        var vm = new ProfileStripViewModel(ProfileManagerWith("Profile1"), EmptyMonitorService());
        vm.SelectedProfileName = null;

        vm.SaveAsNewCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void ProfileStrip_SaveAsNewCommand_AlwaysCanExecute_WhenProfileSelected()
    {
        var vm = new ProfileStripViewModel(ProfileManagerWith("Profile1"), EmptyMonitorService());
        vm.SelectedProfileName = "Profile1";

        vm.SaveAsNewCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void ProfileStrip_SaveAsNewCommand_AlwaysCanExecute_WhenNoProfilesExist()
    {
        var vm = new ProfileStripViewModel(ProfileManagerWith(), EmptyMonitorService());

        vm.SaveAsNewCommand.CanExecute(null).Should().BeTrue();
    }

    #endregion
}
