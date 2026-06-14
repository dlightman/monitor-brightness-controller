using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using MonitorBrightnessController.Application;
using Xunit;

namespace MonitorBrightnessController.Tests;

/// <summary>
/// A single generated monitor-brightness pair: a raw identifier and a raw brightness value
/// string, exactly as they would appear on the command line.
/// </summary>
public sealed record CliPair(string Identifier, string BrightnessRaw);

/// <summary>
/// A generated sequence of N (1..10) monitor-brightness pairs used to exercise Property 7.
/// </summary>
public sealed record CliPairSequence(IReadOnlyList<CliPair> Pairs);

/// <summary>
/// Custom FsCheck generators/arbitraries for Property 7 (Multi-Pair CLI Argument Parsing).
/// Produces sequences of 1..10 pairs where every identifier and brightness token is a
/// non-empty alphanumeric string. Alphanumeric tokens can never start with "--" and can
/// never equal the option keywords (--monitor/--brightness/--profile), so they are always
/// treated by the parser as values rather than options.
/// </summary>
public static class CliPairSequenceArbitraries
{
    private static readonly char[] TokenChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray();

    /// <summary>
    /// Generates a list of exactly <paramref name="n"/> elements using only core Gen
    /// combinators (so it works across FsCheck versions).
    /// </summary>
    private static Gen<List<T>> GenListOfLength<T>(int n, Gen<T> elementGen)
    {
        if (n <= 0)
        {
            return Gen.Constant(new List<T>());
        }

        return elementGen.SelectMany(head =>
            GenListOfLength(n - 1, elementGen).Select(tail =>
            {
                tail.Insert(0, head);
                return tail;
            }));
    }

    /// <summary>Generates a non-empty alphanumeric token (length 1..16).</summary>
    private static Gen<string> GenToken() =>
        from len in Gen.Choose(1, 16)
        from chars in GenListOfLength(len, Gen.Elements(TokenChars))
        select new string(chars.ToArray());

    private static Gen<CliPair> GenPair() =>
        from id in GenToken()
        from value in GenToken()
        select new CliPair(id, value);

    private static Gen<CliPairSequence> GenSequence() =>
        from count in Gen.Choose(1, 10)
        from pairs in GenListOfLength(count, GenPair())
        select new CliPairSequence(pairs);

    /// <summary>Arbitrary used by the property test.</summary>
    public static Arbitrary<CliPairSequence> PairSequence() => Arb.From(GenSequence());
}

/// <summary>
/// Property and example tests for multi-pair CLI argument parsing (Property 7).
/// </summary>
public class CliArgumentParsingTests
{
    /// <summary>
    /// Flattens a sequence of pairs into a CLI argument array of the form
    /// <c>--monitor id1 --brightness val1 --monitor id2 --brightness val2 ...</c>.
    /// </summary>
    private static string[] BuildArgs(IReadOnlyList<CliPair> pairs)
    {
        var args = new List<string>(pairs.Count * 4);
        foreach (CliPair pair in pairs)
        {
            args.Add("--monitor");
            args.Add(pair.Identifier);
            args.Add("--brightness");
            args.Add(pair.BrightnessRaw);
        }

        return args.ToArray();
    }

    // Feature: monitor-brightness-controller, Property 7: Multi-Pair CLI Argument Parsing
    // Validates: Requirements 3.3
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(CliPairSequenceArbitraries) })]
    public void CliParsing_PreservesAllPairs(CliPairSequence sequence)
    {
        string[] args = BuildArgs(sequence.Pairs);

        ParsedCliArguments parsed = CliHandler.ParseArguments(args);

        // Structurally valid input must parse without error.
        parsed.HasError.Should().BeFalse(
            "the generated arguments form well-structured --monitor/--brightness pairs");

        // Exactly N commands must be produced.
        parsed.MonitorCommands.Count.Should().Be(sequence.Pairs.Count);

        // Each command must preserve the original identifier and brightness value, in order.
        for (int i = 0; i < sequence.Pairs.Count; i++)
        {
            parsed.MonitorCommands[i].Identifier.Should().Be(sequence.Pairs[i].Identifier);
            parsed.MonitorCommands[i].BrightnessRaw.Should().Be(sequence.Pairs[i].BrightnessRaw);
        }
    }

    [Fact]
    public void CliParsing_ConcreteThreePairs_PreservesOrderAndValues()
    {
        string[] args =
        {
            "--monitor", "1", "--brightness", "40",
            "--monitor", "DELL U2723QE", "--brightness", "60",
            "--monitor", "2", "--brightness", "75",
        };

        ParsedCliArguments parsed = CliHandler.ParseArguments(args);

        parsed.HasError.Should().BeFalse();
        parsed.MonitorCommands.Should().HaveCount(3);

        parsed.MonitorCommands[0].Identifier.Should().Be("1");
        parsed.MonitorCommands[0].BrightnessRaw.Should().Be("40");
        parsed.MonitorCommands[1].Identifier.Should().Be("DELL U2723QE");
        parsed.MonitorCommands[1].BrightnessRaw.Should().Be("60");
        parsed.MonitorCommands[2].Identifier.Should().Be("2");
        parsed.MonitorCommands[2].BrightnessRaw.Should().Be("75");
    }
}
