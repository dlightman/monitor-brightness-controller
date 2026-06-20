using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using MonitorBrightnessController.Models;
using MonitorBrightnessController.Presentation;

namespace MonitorBrightnessController.Tests.Properties;

// Feature: enhancements-v1-5, Property 2: Startup decision prioritizes DefaultStartupProfileName
// Feature: enhancements-v1-5, Property 4: Startup decision skips apply when disabled or unresolvable

/// <summary>
/// Property-based tests for StartupCoordinator.Decide covering the enhancements-v1-5 spec.
/// </summary>
public class StartupDecisionTests
{
    private static readonly char[] AllowedChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-".ToCharArray();

    /// <summary>
    /// Generates a non-empty profile name (1-20 chars, alphanumeric + underscore/dash).
    /// </summary>
    private static Gen<string> ProfileNameGen =>
        Gen.Choose(1, 20)
            .SelectMany(len =>
                Gen.ArrayOf(len, Gen.Elements(AllowedChars))
                    .Select(chars => new string(chars)));

    /// <summary>
    /// Generates a list of valid profile names (0-10 items).
    /// </summary>
    private static Gen<List<string>> ProfileNameListGen =>
        Gen.Choose(0, 10)
            .SelectMany(count => Gen.ListOf(count, ProfileNameGen).Select(l => l.ToList()));

    /// <summary>
    /// Generates a nullable/empty string (null or empty).
    /// </summary>
    private static Gen<string?> NullOrEmptyStringGen =>
        Gen.Elements<string?>(null, string.Empty);

    // -------------------------------------------------------------------------
    // Property 2: Startup decision prioritizes DefaultStartupProfileName
    // -------------------------------------------------------------------------

    /// <summary>
    /// Property 2: For any AppSettings with AutoApplyOnStartup=true and a non-null, non-empty
    /// DefaultStartupProfileName that exists in the provided profile name list,
    /// StartupCoordinator.Decide shall return StartupAction.ApplyDefaultProfile with that profile name.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 2.2**
    /// </remarks>
    [Property(MaxTest = 100)]
    public Property Decide_WithExistingDefaultProfile_ReturnsApplyDefaultProfile()
    {
        var lastAppliedGen = Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Constant<string?>(string.Empty),
            ProfileNameGen.Select<string, string?>(n => n));

        var inputGen =
            from defaultName in ProfileNameGen
            from extraNames in Gen.Choose(0, 9).SelectMany(count => Gen.ListOf(count, ProfileNameGen).Select(l => l.ToList()))
            from lastApplied in lastAppliedGen
            select new { DefaultName = defaultName, ExtraNames = extraNames, LastApplied = lastApplied };

        return Prop.ForAll(Arb.From(inputGen), input =>
        {
            // Build profile list that includes the default profile name
            var profileList = new List<string>(input.ExtraNames) { input.DefaultName };

            var settings = new AppSettings
            {
                AutoApplyOnStartup = true,
                DefaultStartupProfileName = input.DefaultName,
                LastAppliedProfileName = input.LastApplied
            };

            var decision = StartupCoordinator.Decide(settings, profileList, isCliOverride: false);

            decision.Action.Should().Be(StartupAction.ApplyDefaultProfile,
                "when AutoApplyOnStartup is true and DefaultStartupProfileName exists in profile list, " +
                "Decide should return ApplyDefaultProfile");

            decision.ProfileName.Should().Be(input.DefaultName,
                "the returned profile name should match the configured DefaultStartupProfileName");

            decision.Notice.Should().BeNull(
                "there should be no notice when the default profile is found and will be applied");
        });
    }

    // -------------------------------------------------------------------------
    // Property 4: Startup decision skips apply when disabled or unresolvable
    // -------------------------------------------------------------------------

    /// <summary>
    /// Property 4: Startup decision skips apply when disabled or unresolvable.
    ///
    /// For any AppSettings where AutoApplyOnStartup=false, OR where AutoApplyOnStartup=true
    /// but both DefaultStartupProfileName and LastAppliedProfileName are null or empty,
    /// StartupCoordinator.Decide shall return StartupAction.AutoApplyDisabled with no error notice.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 2.4, 2.5**
    /// </remarks>
    [Property(MaxTest = 100)]
    public Property Decide_ReturnsAutoApplyDisabled_WhenDisabledOrBothNamesUnresolvable()
    {
        // Sub-case 1: AutoApplyOnStartup=false (with any profile names)
        var disabledCaseGen =
            from defaultProfile in Gen.OneOf(
                NullOrEmptyStringGen,
                ProfileNameGen.Select<string, string?>(n => n))
            from lastApplied in Gen.OneOf(
                NullOrEmptyStringGen,
                ProfileNameGen.Select<string, string?>(n => n))
            from profileList in ProfileNameListGen
            select new
            {
                Settings = new AppSettings
                {
                    AutoApplyOnStartup = false,
                    DefaultStartupProfileName = defaultProfile,
                    LastAppliedProfileName = lastApplied
                },
                ProfileList = profileList
            };

        // Sub-case 2: AutoApplyOnStartup=true but both names are null or empty
        var unresolvableCaseGen =
            from defaultProfile in NullOrEmptyStringGen
            from lastApplied in NullOrEmptyStringGen
            from profileList in ProfileNameListGen
            select new
            {
                Settings = new AppSettings
                {
                    AutoApplyOnStartup = true,
                    DefaultStartupProfileName = defaultProfile,
                    LastAppliedProfileName = lastApplied
                },
                ProfileList = profileList
            };

        // Combine both sub-cases with equal probability
        var combinedGen = Gen.OneOf(disabledCaseGen, unresolvableCaseGen);

        return Prop.ForAll(Arb.From(combinedGen), input =>
        {
            var decision = StartupCoordinator.Decide(
                input.Settings,
                input.ProfileList,
                isCliOverride: false);

            decision.Action.Should().Be(StartupAction.AutoApplyDisabled,
                "Decide should return AutoApplyDisabled when auto-apply is disabled " +
                "or when both profile names are null/empty");

            decision.Notice.Should().BeNull(
                "no error notice should be produced when auto-apply is disabled or unresolvable");
        });
    }
}
