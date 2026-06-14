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

namespace MonitorBrightnessController.Tests.Properties;

// Feature: startup-and-install-enhancements, Property 6: Startup profile dropdown selection resolution
// Feature: startup-and-install-enhancements, Property 7: Default profile cleanup on deletion

/// <summary>
/// In-memory settings store for property tests that need to exercise ViewModel
/// startup profile dropdown behavior.
/// </summary>
internal sealed class InMemorySettingsStore_Dropdown : ISettingsStore
{
    public AppSettings Current { get; private set; }

    public InMemorySettingsStore_Dropdown(AppSettings seed)
    {
        Current = seed;
    }

    public AppSettings Load() => Current;

    public Result<Unit> Save(AppSettings settings)
    {
        Current = settings;
        return Result<Unit>.Success(Unit.Value);
    }
}

/// <summary>
/// Property-based tests for the default startup profile dropdown behavior in the ViewModel.
/// </summary>
public class StartupProfileDropdownProperties
{
    private static IMonitorService EmptyMonitorService()
    {
        var service = Substitute.For<IMonitorService>();
        service.DetectMonitors().Returns(new List<MonitorState>());
        return service;
    }

    private static IProfileManager ProfileManagerWith(params string[] profileNames)
    {
        var profiles = new List<Profile>();
        foreach (string name in profileNames)
        {
            profiles.Add(new Profile
            {
                Name = name,
                MonitorBrightnessMap = new Dictionary<string, int>(),
            });
        }

        var manager = Substitute.For<IProfileManager>();
        manager.GetAllProfiles().Returns(profiles);
        return manager;
    }

    /// <summary>
    /// Property 7: Default profile cleanup on deletion
    ///
    /// For any profile that is currently set as the DefaultStartupProfileName, when that
    /// profile is deleted via NotifyProfileDeleted, the DefaultStartupProfileName setting
    /// becomes null and the SelectedStartupProfile resolves to "None".
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 3.5**
    /// </remarks>
    [Property(MaxTest = 100)]
    public Property DefaultProfile_WhenDeleted_BecomesNull()
    {
        var allowedChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray();

        var profileNameGen = Gen.Choose(1, 20)
            .SelectMany(len =>
                Gen.ArrayOf(len, Gen.Elements(allowedChars))
                    .Select(chars => new string(chars)));

        return Prop.ForAll(Arb.From(profileNameGen), profileName =>
        {
            // Arrange: settings with the profile in the list AND set as default
            var settings = new AppSettings
            {
                Profiles = new List<Profile>
                {
                    new Profile
                    {
                        Name = profileName,
                        MonitorBrightnessMap = new Dictionary<string, int>(),
                    }
                },
                DefaultStartupProfileName = profileName
            };

            var store = new InMemorySettingsStore_Dropdown(settings);
            var profileManager = ProfileManagerWith(profileName);

            var vm = new MainWindowViewModel(EmptyMonitorService(), store, profileManager);

            // Verify initial state: SelectedStartupProfile should be the profile name
            vm.SelectedStartupProfile.Should().Be(profileName,
                "initially the selected startup profile should match the configured default");

            // Act: simulate profile deletion — remove from store and notify ViewModel
            store.Save(settings with
            {
                Profiles = new List<Profile>()
            });
            vm.NotifyProfileDeleted(profileName);

            // Assert: SelectedStartupProfile should be "None"
            vm.SelectedStartupProfile.Should().Be("None",
                "after deleting the default profile, the dropdown should show 'None'");

            // Assert: the persisted DefaultStartupProfileName should be null
            store.Current.DefaultStartupProfileName.Should().BeNull(
                "after deleting the default profile, DefaultStartupProfileName should be null in the store");
        });
    }
}


/// <summary>
/// Property-based tests for startup profile dropdown selection resolution.
/// </summary>
public class StartupProfileDropdownSelectionProperties
{
    private static IMonitorService EmptyMonitorService()
    {
        var service = Substitute.For<IMonitorService>();
        service.DetectMonitors().Returns(new List<MonitorState>());
        return service;
    }

    /// <summary>
    /// Property 6: Startup profile dropdown selection resolution
    ///
    /// For any DefaultStartupProfileName that is null or does not match any name in the
    /// profile list (case-insensitive), the effective selected value resolves to "None".
    ///
    /// This property tests that after construction and a subsequent RefreshStartupProfileDropdown()
    /// call, the SelectedStartupProfile is "None" when the configured default is invalid/null.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 3.2**
    /// </remarks>
    [Property(MaxTest = 100)]
    public Property SelectedStartupProfile_ResolvesToNone_WhenDefaultIsNullOrNonMatching()
    {
        var allowedChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_".ToCharArray();

        // Generate actual profile names that will be in the list
        var profileNameGen = Gen.Choose(1, 20)
            .SelectMany(len =>
                Gen.ArrayOf(len, Gen.Elements(allowedChars))
                    .Select(chars => new string(chars)));

        var profileListGen = Gen.Choose(0, 10)
            .SelectMany(count => Gen.ListOf(count, profileNameGen)
                .Select(names => names.ToList()));

        // Generate a DefaultStartupProfileName that is null, empty, or non-matching
        var invalidDefaultGen = Gen.OneOf(
            // null
            Gen.Constant<string?>(null),
            // empty string
            Gen.Constant<string?>(string.Empty),
            // a name guaranteed not to match any profile in the list
            profileNameGen.Select<string, string?>(name => "INVALID_PREFIX_" + name));

        var inputGen =
            from profileNames in profileListGen
            from invalidDefault in invalidDefaultGen
            // Ensure INVALID_PREFIX_ name doesn't accidentally match a profile name
            where invalidDefault is null
                  || invalidDefault == string.Empty
                  || !profileNames.Any(p => string.Equals(p, invalidDefault, StringComparison.OrdinalIgnoreCase))
            select new { ProfileNames = profileNames, DefaultProfileName = invalidDefault };

        return Prop.ForAll(Arb.From(inputGen), input =>
        {
            // Arrange: create settings with the generated profiles and the invalid default
            var profiles = input.ProfileNames
                .Select(name => new Profile
                {
                    Name = name,
                    MonitorBrightnessMap = new Dictionary<string, int>()
                })
                .ToList();

            var settings = new AppSettings
            {
                Profiles = profiles,
                DefaultStartupProfileName = input.DefaultProfileName
            };

            var store = new InMemorySettingsStore_Dropdown(settings);
            var monitorService = EmptyMonitorService();
            var profileManager = Substitute.For<IProfileManager>();
            profileManager.GetAllProfiles().Returns(profiles);

            // Act: construct the ViewModel, then call RefreshStartupProfileDropdown
            // to trigger the selection resolution logic
            var vm = new MainWindowViewModel(monitorService, store, profileManager);
            vm.RefreshStartupProfileDropdown();

            // Assert: SelectedStartupProfile should resolve to "None"
            vm.SelectedStartupProfile.Should().Be("None",
                $"when DefaultStartupProfileName is '{input.DefaultProfileName}' " +
                $"(which doesn't match any profile in [{string.Join(", ", input.ProfileNames)}]), " +
                "the effective selected value should resolve to 'None'");
        });
    }
}
