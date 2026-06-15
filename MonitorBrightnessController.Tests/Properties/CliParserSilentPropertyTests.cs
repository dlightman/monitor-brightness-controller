using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using MonitorBrightnessController.Application;

namespace MonitorBrightnessController.Tests.Properties;

// Feature: enhancements-v1-4, Property 1: Silent flag parsing preserves coexisting arguments

/// <summary>
/// Generators for producing valid CLI argument arrays that can coexist with --silent.
/// The generator inserts --silent only at group boundaries (before/between/after complete
/// argument groups), never inside a --monitor group where it would interrupt parsing.
/// </summary>
public static class SilentFlagArbitraries
{
    /// <summary>
    /// Generates a non-empty alphanumeric identifier that does not start with "--".
    /// </summary>
    private static Gen<string> GenIdentifier()
    {
        return Gen.Choose(1, 8).SelectMany(len =>
            Gen.ArrayOf(len, Gen.Elements(
                "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray()))
            .Select(chars => new string(chars)));
    }

    /// <summary>
    /// Generates a valid brightness/gamma value string (integer 0-100).
    /// </summary>
    private static Gen<string> GenValueStr()
    {
        return Gen.Choose(0, 100).Select(v => v.ToString());
    }

    /// <summary>
    /// Generates a single --monitor group as a string array.
    /// The group has --monitor &lt;id&gt; followed by --brightness and/or --gamma in random order.
    /// </summary>
    private static Gen<string[]> GenMonitorGroup()
    {
        return from id in GenIdentifier()
               from hasBrightness in Arb.Generate<bool>()
               from hasGamma in Arb.Generate<bool>()
               from brightness in GenValueStr()
               from gamma in GenValueStr()
               from brightnessFirst in Arb.Generate<bool>()
               let actualHasBrightness = hasBrightness || !hasGamma // at least one must be true
               let parts = BuildMonitorGroupParts(id, actualHasBrightness, hasGamma, brightness, gamma, brightnessFirst)
               select parts;
    }

    private static string[] BuildMonitorGroupParts(
        string id, bool hasBrightness, bool hasGamma,
        string brightness, string gamma, bool brightnessFirst)
    {
        var parts = new List<string> { "--monitor", id };

        var settings = new List<string[]>();
        if (hasBrightness)
            settings.Add(new[] { "--brightness", brightness });
        if (hasGamma)
            settings.Add(new[] { "--gamma", gamma });

        if (!brightnessFirst && settings.Count == 2)
            settings.Reverse();

        foreach (var s in settings)
            parts.AddRange(s);

        return parts.ToArray();
    }

    /// <summary>
    /// Generates a valid argument set as a list of complete argument groups (each group is a
    /// string array). --silent can be inserted between any groups (at group boundaries).
    /// Returns a tuple of (list of groups, index at which to insert --silent among those groups).
    /// </summary>
    private static Gen<Tuple<List<string[]>, int>> GenGroupsWithInsertIndex()
    {
        var genMonitorGroups = Gen.Choose(1, 3).SelectMany(count =>
            Gen.ArrayOf(count, GenMonitorGroup())
                .Select(groups => groups.ToList()));

        var genProfileGroup = GenIdentifier().Select(name =>
            new List<string[]> { new[] { "--profile", name } });

        // Choose between monitor groups or profile
        var genGroups = Gen.OneOf(genMonitorGroups, genProfileGroup);

        return from groups in genGroups
               from insertIdx in Gen.Choose(0, groups.Count) // 0 = before all, Count = after all
               select Tuple.Create(groups, insertIdx);
    }

    /// <summary>
    /// Generates a tuple of (base args without --silent as flat array, args with --silent inserted at a group boundary).
    /// This ensures --silent is only placed at valid positions (between complete argument groups).
    /// </summary>
    public static Arbitrary<Tuple<string[], string[]>> ValidArgsWithAndWithoutSilent()
    {
        var gen = from groupsAndIdx in GenGroupsWithInsertIndex()
                  let groups = groupsAndIdx.Item1
                  let insertIdx = groupsAndIdx.Item2
                  let baseArgs = groups.SelectMany(g => g).ToArray()
                  let withSilentGroups = groups.Take(insertIdx)
                      .Append(new[] { "--silent" })
                      .Concat(groups.Skip(insertIdx))
                      .SelectMany(g => g).ToArray()
                  select Tuple.Create(baseArgs, withSilentGroups);

        return Arb.From(gen);
    }
}

/// <summary>
/// Property-based tests verifying that inserting --silent at group boundaries in a valid
/// argument array produces Silent=true and preserves all other parsed fields.
/// --silent is inserted at valid positions: before, between, or after complete argument
/// groups (not inside a --monitor group where it would interrupt the group's settings).
/// </summary>
public class CliParserSilentPropertyTests
{
    // -------------------------------------------------------------------------
    // Property 1: Silent flag parsing preserves coexisting arguments
    // -------------------------------------------------------------------------

    /// <summary>
    /// Property 1: For any valid combination of CLI arguments (one or more --monitor groups
    /// or an optional --profile) with --silent inserted at any group boundary, ParseArguments
    /// shall produce Silent == true AND all other commands/profile are parsed identically
    /// to the same arguments without --silent.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 1.7**
    /// </remarks>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(SilentFlagArbitraries) })]
    public void SilentFlag_AtAnyPosition_PreservesCoexistingArguments(Tuple<string[], string[]> input)
    {
        string[] baseArgs = input.Item1;
        string[] argsWithSilent = input.Item2;

        // Parse without --silent
        ParsedCliArguments withoutSilent = CliHandler.ParseArguments(baseArgs);

        // Skip if the base args themselves produce a parse error (shouldn't happen with our generator but be safe)
        if (withoutSilent.HasError)
            return;

        // Parse with --silent
        ParsedCliArguments withSilent = CliHandler.ParseArguments(argsWithSilent);

        // Verify: no parse error
        withSilent.HasError.Should().BeFalse(
            "inserting --silent at a group boundary in [{0}] should not cause a parse error, but got: {1}",
            string.Join(" ", baseArgs), withSilent.ParseError ?? "");

        // Verify: Silent flag is set
        withSilent.Silent.Should().BeTrue(
            "the Silent flag should be true when --silent is present");

        // Verify: without-silent parse does NOT have Silent set
        withoutSilent.Silent.Should().BeFalse(
            "the base args without --silent should have Silent = false");

        // Verify: MonitorCommands are identical
        withSilent.MonitorCommands.Should().HaveCount(withoutSilent.MonitorCommands.Count,
            "the number of monitor commands should be preserved");

        for (int i = 0; i < withoutSilent.MonitorCommands.Count; i++)
        {
            var expected = withoutSilent.MonitorCommands[i];
            var actual = withSilent.MonitorCommands[i];

            actual.Identifier.Should().Be(expected.Identifier,
                "monitor command {0} identifier should be preserved", i);
            actual.BrightnessRaw.Should().Be(expected.BrightnessRaw,
                "monitor command {0} brightness should be preserved", i);
            actual.GammaRaw.Should().Be(expected.GammaRaw,
                "monitor command {0} gamma should be preserved", i);
        }

        // Verify: ProfileName is identical
        withSilent.ProfileName.Should().Be(withoutSilent.ProfileName,
            "the profile name should be preserved when --silent is added");
    }

    /// <summary>
    /// Property 1 (solo variant): When --silent is the only argument, ParseArguments
    /// shall produce Silent == true with no commands and no profile (valid parse, no error).
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 1.7**
    /// </remarks>
    [Property(MaxTest = 100)]
    public void SilentFlag_Alone_ProducesValidParseWithNoCommandsOrProfile()
    {
        var args = new[] { "--silent" };

        ParsedCliArguments result = CliHandler.ParseArguments(args);

        result.HasError.Should().BeFalse("--silent alone should be a valid invocation");
        result.Silent.Should().BeTrue("Silent should be true");
        result.MonitorCommands.Should().BeEmpty("no monitor commands expected");
        result.ProfileName.Should().BeNull("no profile expected");
    }
}
