using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using MonitorBrightnessController.Application;
using MonitorBrightnessController.Models;
using Xunit;

namespace MonitorBrightnessController.Tests;

/// <summary>
/// Custom FsCheck generators/arbitraries for Property 1 (Deterministic Monitor Index
/// Assignment). Produces lists of <em>distinct</em> device path strings together with a
/// permutation of those paths, so the property can compare index assignment across two
/// enumeration orderings of the same underlying set.
/// </summary>
public static class DistinctDevicePathArbitraries
{
    // Device-path-like characters: letters, digits and the punctuation that appears in
    // Windows device interface paths (e.g. \\?\DISPLAY#DEL41AB#5&...).
    private static readonly char[] PathChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789\\#&?.{}-_".ToCharArray();

    /// <summary>
    /// Generates a list of exactly <paramref name="n"/> elements from <paramref name="elementGen"/>,
    /// using only the core Gen combinators (so it works across FsCheck versions).
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

    private static Gen<string> GenDevicePath() =>
        from len in Gen.Choose(1, 40)
        from chars in GenListOfLength(len, Gen.Elements(PathChars))
        select new string(chars.ToArray());

    /// <summary>
    /// Generates a set of distinct device paths (ordinal-distinct) of size in [0, 8].
    /// </summary>
    private static Gen<List<string>> GenDistinctDevicePaths() =>
        from count in Gen.Choose(0, 8)
        from raw in GenListOfLength(count, GenDevicePath())
        // De-duplicate using ordinal comparison, which matches BuildMonitorStates' ordering.
        select raw.Distinct(StringComparer.Ordinal).ToList();

    /// <summary>
    /// A Fisher-Yates shuffle driven by FsCheck's generator so the permutation is reproducible
    /// and shrinkable as part of the generated case.
    /// </summary>
    private static Gen<List<T>> GenPermutation<T>(IReadOnlyList<T> source)
    {
        if (source.Count <= 1)
        {
            return Gen.Constant(source.ToList());
        }

        // Generate a swap index for each position from i..n-1, then apply the swaps.
        return GenSwapIndices(source.Count).Select(swaps =>
        {
            var items = source.ToList();
            for (int i = 0; i < items.Count - 1; i++)
            {
                int j = swaps[i];
                (items[i], items[j]) = (items[j], items[i]);
            }

            return items;
        });
    }

    private static Gen<List<int>> GenSwapIndices(int n)
    {
        var generators = new List<Gen<int>>();
        for (int i = 0; i < n - 1; i++)
        {
            generators.Add(Gen.Choose(i, n - 1));
        }

        return GenSequence(generators);
    }

    private static Gen<List<int>> GenSequence(IReadOnlyList<Gen<int>> generators)
    {
        if (generators.Count == 0)
        {
            return Gen.Constant(new List<int>());
        }

        return generators[0].SelectMany(head =>
            GenSequence(generators.Skip(1).ToList()).Select(tail =>
            {
                tail.Insert(0, head);
                return tail;
            }));
    }

    /// <summary>
    /// Generates a pair: the set of distinct device paths and a permutation of that set.
    /// </summary>
    private static Gen<DistinctPathOrderings> GenOrderings() =>
        from paths in GenDistinctDevicePaths()
        from shuffled in GenPermutation(paths)
        select new DistinctPathOrderings(paths, shuffled);

    /// <summary>Arbitrary used by the property test.</summary>
    public static Arbitrary<DistinctPathOrderings> Orderings() => Arb.From(GenOrderings());
}

/// <summary>
/// A set of distinct device paths together with one permutation of that same set. Both
/// orderings describe the identical underlying set of monitors enumerated in a different order.
/// </summary>
public sealed record DistinctPathOrderings(IReadOnlyList<string> Original, IReadOnlyList<string> Shuffled);

/// <summary>
/// Property and example tests for deterministic monitor index assignment.
/// </summary>
public class MonitorIndexAssignmentTests
{
    private static IReadOnlyList<PhysicalMonitorInfo> ToPhysicalMonitors(IEnumerable<string> devicePaths) =>
        devicePaths
            .Select(path => new PhysicalMonitorInfo
            {
                DevicePath = path,
                MonitorName = null,
                PhysicalHandle = IntPtr.Zero,
                SupportsDdcCi = true,
            })
            .ToList();

    private static Dictionary<string, int> IndexMap(IReadOnlyList<PhysicalMonitorInfo> monitors) =>
        MonitorService.BuildMonitorStates(monitors)
            .ToDictionary(s => s.DevicePath, s => s.MonitorIndex, StringComparer.Ordinal);

    // Feature: monitor-brightness-controller, Property 1: Deterministic Monitor Index Assignment
    // Validates: Requirements 1.1
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(DistinctDevicePathArbitraries) })]
    public void IndexAssignment_IsDeterministic(DistinctPathOrderings orderings)
    {
        Dictionary<string, int> originalMap = IndexMap(ToPhysicalMonitors(orderings.Original));
        Dictionary<string, int> shuffledMap = IndexMap(ToPhysicalMonitors(orderings.Shuffled));

        // The (device path -> index) mapping must be identical regardless of enumeration order.
        shuffledMap.Should().BeEquivalentTo(originalMap);
    }

    [Fact]
    public void IndexAssignment_ConcreteExample_IsOrderIndependent()
    {
        var ascending = new[]
        {
            "\\\\?\\DISPLAY#AAA111#1&abc",
            "\\\\?\\DISPLAY#BBB222#2&def",
            "\\\\?\\DISPLAY#CCC333#3&ghi",
        };
        var reversed = ascending.Reverse().ToArray();

        Dictionary<string, int> ascendingMap = IndexMap(ToPhysicalMonitors(ascending));
        Dictionary<string, int> reversedMap = IndexMap(ToPhysicalMonitors(reversed));

        // Sorted ordinal => indices assigned by sorted device path, starting at 1.
        ascendingMap["\\\\?\\DISPLAY#AAA111#1&abc"].Should().Be(1);
        ascendingMap["\\\\?\\DISPLAY#BBB222#2&def"].Should().Be(2);
        ascendingMap["\\\\?\\DISPLAY#CCC333#3&ghi"].Should().Be(3);

        reversedMap.Should().BeEquivalentTo(ascendingMap);
    }
}
