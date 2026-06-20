using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using MonitorBrightnessController.Models;
using MonitorBrightnessController.Presentation;

namespace MonitorBrightnessController.Tests.Properties;

// Feature: enhancements-v1-5, Property 6: CLI override always skips startup apply

/// <summary>
/// Property-based tests verifying that CLI override always causes StartupCoordinator.Decide
/// to return StartupAction.CliOverride, regardless of any other settings or profile state.
/// </summary>
public class StartupDecisionCliOverrideTests
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
    /// Generates a nullable/empty string or a valid profile name.
    /// </summary>
    private static Gen<string?> NullableProfileNameGen =>
        Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Constant<string?>(string.Empty),
            ProfileNameGen.Select<string, string?>(n => n));

    /// <summary>
    /// Generates random AppSettings with any combination of values.
    /// </summary>
    private static Gen<AppSettings> AppSettingsGen =>
        from autoApply in Arb.Generate<bool>()
        from defaultProfile in NullableProfileNameGen
        from lastApplied in NullableProfileNameGen
        from startWithWindows in Arb.Generate<bool>()
        from minimizeToTray in Arb.Generate<bool>()
        from smoothTransition in Arb.Generate<bool>()
        from refreshOnFocus in Arb.Generate<bool>()
        from transitionMs in Gen.Choose(100, 2000)
        select new AppSettings
        {
            AutoApplyOnStartup = autoApply,
            DefaultStartupProfileName = defaultProfile,
            LastAppliedProfileName = lastApplied,
            StartWithWindows = startWithWindows,
            MinimizeToTray = minimizeToTray,
            SmoothTransition = smoothTransition,
            RefreshOnFocus = refreshOnFocus,
            TransitionDurationMs = transitionMs
        };

    // -------------------------------------------------------------------------
    // Property 6: CLI override always skips startup apply
    // -------------------------------------------------------------------------

    /// <summary>
    /// Property 6: For any AppSettings and for any profile name list, when isCliOverride=true,
    /// StartupCoordinator.Decide shall return StartupAction.CliOverride regardless of other settings.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 2.9**
    /// </remarks>
    [Property(MaxTest = 100)]
    public Property Decide_WithCliOverride_AlwaysReturnsCliOverride()
    {
        var inputGen =
            from settings in AppSettingsGen
            from profileList in ProfileNameListGen
            select new { Settings = settings, ProfileList = profileList };

        return Prop.ForAll(Arb.From(inputGen), input =>
        {
            var decision = StartupCoordinator.Decide(
                input.Settings,
                input.ProfileList,
                isCliOverride: true);

            decision.Action.Should().Be(StartupAction.CliOverride,
                "when isCliOverride=true, Decide should always return CliOverride " +
                "regardless of AutoApplyOnStartup, profile names, or any other settings");

            decision.ProfileName.Should().BeNull(
                "no profile name should be returned when CLI override is active");

            decision.Notice.Should().BeNull(
                "no notice should be produced when CLI override is active");
        });
    }
}
