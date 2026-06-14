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
/// Custom FsCheck generators/arbitraries for Property 3 (DDC/CI Support Filtering).
/// Generates lists of (devicePath, supportsDdc) tuples where device paths are
/// guaranteed distinct within each list, so the mapping from device path to
/// controllability is unambiguous.
/// </summary>
public static class DdcSupportArbitraries
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
    /// Generates a list of (devicePath, supportsDdc) tuples with distinct device paths.
    /// We over-generate candidate paths, de-duplicate them, then pair each surviving path
    /// with an independently generated DDC/CI support flag.
    /// </summary>
    private static Gen<List<(string DevicePath, bool SupportsDdc)>> GenMonitorTuples() =>
        from count in Gen.Choose(0, 8)
        from paths in GenListOfLength(count, GenDevicePath())
        from flags in GenListOfLength(count, Arb.Generate<bool>())
        let distinctPaths = paths.Distinct(StringComparer.Ordinal).ToList()
        select distinctPaths
            .Select((p, i) => (DevicePath: p, SupportsDdc: flags[i]))
            .ToList();

    /// <summary>Arbitrary producing distinct-path (devicePath, supportsDdc) tuple lists.</summary>
    public static Arbitrary<List<(string DevicePath, bool SupportsDdc)>> MonitorTuples() =>
        Arb.From(GenMonitorTuples());
}

/// <summary>
/// Property test for Property 3: DDC/CI Support Filtering. Verifies that the controllable
/// monitor list contains exactly those monitors whose DDC/CI support flag is true.
/// </summary>
public class DdcSupportFilteringTests
{
    // Feature: monitor-brightness-controller, Property 3: DDC/CI Support Filtering
    // Validates: Requirements 1.3
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(DdcSupportArbitraries) })]
    public void DdcFilter_ExcludesUnsupported(List<(string DevicePath, bool SupportsDdc)> tuples)
    {
        var infos = tuples
            .Select(t => new PhysicalMonitorInfo
            {
                DevicePath = t.DevicePath,
                MonitorName = null,
                PhysicalHandle = IntPtr.Zero,
                SupportsDdcCi = t.SupportsDdc,
            })
            .ToList();

        IReadOnlyList<MonitorState> states = MonitorService.BuildMonitorStates(infos);

        // Expected partition of device paths based on the input support flags.
        var expectedControllable = tuples
            .Where(t => t.SupportsDdc)
            .Select(t => t.DevicePath)
            .ToHashSet(StringComparer.Ordinal);

        var expectedUncontrollable = tuples
            .Where(t => !t.SupportsDdc)
            .Select(t => t.DevicePath)
            .ToHashSet(StringComparer.Ordinal);

        var actualControllable = states
            .Where(s => s.IsControllable)
            .Select(s => s.DevicePath)
            .ToHashSet(StringComparer.Ordinal);

        var actualUncontrollable = states
            .Where(s => !s.IsControllable)
            .Select(s => s.DevicePath)
            .ToHashSet(StringComparer.Ordinal);

        // The controllable set must match exactly the supported inputs, and the
        // uncontrollable set must match exactly the unsupported inputs.
        actualControllable.Should().BeEquivalentTo(expectedControllable);
        actualUncontrollable.Should().BeEquivalentTo(expectedUncontrollable);

        // Sanity: every input monitor is represented exactly once.
        states.Should().HaveCount(tuples.Count);
    }

    [Fact]
    public void DdcFilter_ExcludesUnsupported_ConcreteExample()
    {
        var infos = new List<PhysicalMonitorInfo>
        {
            new() { DevicePath = "\\\\?\\DISPLAY#DEL41AB#5&a", SupportsDdcCi = true },
            new() { DevicePath = "\\\\?\\DISPLAY#GSM59AB#7&b", SupportsDdcCi = false },
            new() { DevicePath = "\\\\?\\DISPLAY#ACR12CD#3&c", SupportsDdcCi = true },
        };

        IReadOnlyList<MonitorState> states = MonitorService.BuildMonitorStates(infos);

        states.Where(s => s.IsControllable).Select(s => s.DevicePath)
            .Should().BeEquivalentTo(new[]
            {
                "\\\\?\\DISPLAY#DEL41AB#5&a",
                "\\\\?\\DISPLAY#ACR12CD#3&c",
            });

        states.Where(s => !s.IsControllable).Select(s => s.DevicePath)
            .Should().BeEquivalentTo(new[] { "\\\\?\\DISPLAY#GSM59AB#7&b" });
    }
}
