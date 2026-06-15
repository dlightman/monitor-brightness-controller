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

// Feature: ui-consolidation, Property 11: Startup profile application correctness
// Feature: ui-consolidation, Property 13: Deleted startup profile resets to "Last Used"

/// <summary>
/// In-memory settings store for startup profile property tests.
/// </summary>
internal sealed class InMemorySettingsStore_StartupProfile : ISettingsStore
{
    public AppSettings Current { get; private set; }

    public InMemorySettingsStore_StartupProfile(AppSettings seed)
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
/// Property-based tests for startup profile application correctness and deletion reset behavior.
/// </summary>
public class StartupProfileProperties
{
    private static readonly char[] AllowedChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-".ToCharArray();

    private static Gen<string> ProfileNameGen =>
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

    private static IProfileManager ProfileManagerWith(params string[] profileNames)
    {
        var profiles = profileNames
            .Select(name => new Profile
            {
                Name = name,
                MonitorBrightnessMap = new Dictionary<string, int>(),
            })
            .ToList();

        var manager = Substitute.For<IProfileManager>();
        manager.GetAllProfiles().Returns(profiles);
        return manager;
    }

    /// <summary>
    /// Property 11: Startup profile application correctness (Case 1 - "Last Used")
    ///
    /// For any startup profile configuration where AutoApplyOnStartup is enabled,
    /// if "Last Used" is selected (DefaultStartupProfileName is null) and
    /// LastAppliedProfileName refers to an existing profile, that profile SHALL be applied.
    /// StartupCoordinator.Decide SHALL return ApplyLastProfile with the correct profile name.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 6.6, 6.7**
    /// </remarks>
    [Property(MaxTest = 100)]
    public Property LastUsed_WithExistingLastAppliedProfile_ShallApplyThatProfile()
    {
        // Generate a non-empty list of distinct profile names, then pick one as LastAppliedProfileName
        var inputGen =
            from profileNames in Gen.NonEmptyListOf(ProfileNameGen)
                .Select(l => l.Distinct(StringComparer.OrdinalIgnoreCase).ToList())
            where profileNames.Count > 0
            from lastAppliedIndex in Gen.Choose(0, profileNames.Count - 1)
            let lastApplied = profileNames[lastAppliedIndex]
            select new { ProfileNames = profileNames, LastApplied = lastApplied };

        return Prop.ForAll(Arb.From(inputGen), input =>
        {
            // Arrange: AutoApplyOnStartup=true, DefaultStartupProfileName=null ("Last Used"),
            // LastAppliedProfileName = an existing profile
            var settings = new AppSettings
            {
                AutoApplyOnStartup = true,
                DefaultStartupProfileName = null,
                LastAppliedProfileName = input.LastApplied
            };

            // Act
            var decision = StartupCoordinator.Decide(settings, input.ProfileNames);

            // Assert: should apply the last used profile
            decision.Action.Should().Be(StartupAction.ApplyLastProfile,
                "when 'Last Used' is selected and LastAppliedProfileName refers to an existing profile, " +
                "the action should be ApplyLastProfile");
            decision.ProfileName.Should().Be(input.LastApplied,
                "the profile name in the decision should match LastAppliedProfileName");
        });
    }

    /// <summary>
    /// Property 11: Startup profile application correctness (Case 2 - Specific profile)
    ///
    /// For any startup profile configuration where AutoApplyOnStartup is enabled and
    /// a specific profile name is selected (DefaultStartupProfileName is non-null) and
    /// that profile exists in the profile list, Decide SHALL return ApplyDefaultProfile
    /// with the correct profile name.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 6.6, 6.7**
    /// </remarks>
    [Property(MaxTest = 100)]
    public Property SpecificProfile_WhenExistsInList_ShallApplyThatProfile()
    {
        // Generate a non-empty list of distinct profile names, then pick one as DefaultStartupProfileName
        var inputGen =
            from profileNames in Gen.NonEmptyListOf(ProfileNameGen)
                .Select(l => l.Distinct(StringComparer.OrdinalIgnoreCase).ToList())
            where profileNames.Count > 0
            from defaultIndex in Gen.Choose(0, profileNames.Count - 1)
            let defaultProfile = profileNames[defaultIndex]
            select new { ProfileNames = profileNames, DefaultProfile = defaultProfile };

        return Prop.ForAll(Arb.From(inputGen), input =>
        {
            // Arrange: AutoApplyOnStartup=true, DefaultStartupProfileName = a specific existing name
            var settings = new AppSettings
            {
                AutoApplyOnStartup = true,
                DefaultStartupProfileName = input.DefaultProfile,
                LastAppliedProfileName = null // irrelevant for this case
            };

            // Act
            var decision = StartupCoordinator.Decide(settings, input.ProfileNames);

            // Assert: should apply the configured default profile
            decision.Action.Should().Be(StartupAction.ApplyDefaultProfile,
                "when a specific profile name is selected and exists, " +
                "the action should be ApplyDefaultProfile");
            decision.ProfileName.Should().Be(input.DefaultProfile,
                "the profile name in the decision should match DefaultStartupProfileName");
        });
    }

    /// <summary>
    /// Property 13: Deleted startup profile resets to "Last Used"
    ///
    /// For any profile that is currently selected as the startup profile, if that profile
    /// is deleted, the Startup Profile dropdown SHALL reset its selection to "Last Used"
    /// and persist DefaultStartupProfileName = null.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 6.9**
    /// </remarks>
    [Property(MaxTest = 100)]
    public Property DeletedStartupProfile_ResetsToLastUsed()
    {
        // Generate additional profile names that are NOT the startup profile
        var otherProfilesGen = Gen.Choose(0, 5)
            .SelectMany(count =>
                Gen.ListOf(count, ProfileNameGen)
                    .Select(names => names.ToList()));

        var inputGen =
            from startupProfileName in ProfileNameGen
            from otherProfiles in otherProfilesGen
            // Ensure the startup profile name is not duplicated in the other profiles
            let filteredOthers = otherProfiles
                .Where(n => !string.Equals(n, startupProfileName, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
            select new { StartupProfileName = startupProfileName, OtherProfiles = filteredOthers };

        return Prop.ForAll(Arb.From(inputGen), input =>
        {
            // Arrange: create all profile names including the startup profile
            var allProfileNames = new List<string> { input.StartupProfileName };
            allProfileNames.AddRange(input.OtherProfiles);

            var profiles = allProfileNames
                .Select(name => new Profile
                {
                    Name = name,
                    MonitorBrightnessMap = new Dictionary<string, int>(),
                })
                .ToList();

            var settings = new AppSettings
            {
                Profiles = profiles,
                DefaultStartupProfileName = input.StartupProfileName
            };

            var store = new InMemorySettingsStore_StartupProfile(settings);
            var profileManager = ProfileManagerWith(allProfileNames.ToArray());

            var vm = new MainWindowViewModel(EmptyMonitorService(), store, profileManager);

            // Verify initial state: SelectedStartupProfileName should be the profile name
            vm.SelectedStartupProfileName.Should().Be(input.StartupProfileName,
                "initially the selected startup profile name should match the configured default");

            // Act: simulate profile deletion — remove from store and notify ViewModel
            var remainingProfiles = profiles
                .Where(p => !string.Equals(p.Name, input.StartupProfileName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            store.Save(settings with { Profiles = remainingProfiles });
            vm.NotifyProfileDeleted(input.StartupProfileName);

            // Assert: SelectedStartupProfileName should be reset to "Last Used"
            vm.SelectedStartupProfileName.Should().Be("Last Used",
                "after deleting the startup profile, the dropdown should show 'Last Used'");

            // Assert: the persisted DefaultStartupProfileName should be null
            store.Current.DefaultStartupProfileName.Should().BeNull(
                "after deleting the startup profile, DefaultStartupProfileName should be null in the store");
        });
    }
}
