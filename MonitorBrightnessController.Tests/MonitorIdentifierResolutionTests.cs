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
/// Custom FsCheck generators for case-insensitive monitor identifier resolution.
/// Generates alphabetic (non-numeric, non-empty) monitor names together with a case
/// variant whose invariant-lowercase form is identical, so that resolution must succeed
/// by case-insensitive name matching rather than by numeric index.
/// </summary>
public static class MonitorIdentifierArbitraries
{
    // Letters only: ensures names are non-numeric (so they match by name, not index) and
    // that toggling case actually changes the string in a meaningful way.
    private static readonly char[] LetterChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();

    /// <summary>
    /// Generates a list of exactly <paramref name="n"/> elements using only core Gen
    /// combinators (version-agnostic across FsCheck releases).
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

    /// <summary>A non-empty alphabetic monitor name (length 1-20).</summary>
    private static Gen<string> GenMonitorName() =>
        from len in Gen.Choose(1, 20)
        from chars in GenListOfLength(len, Gen.Elements(LetterChars))
        select new string(chars.ToArray());

    /// <summary>
    /// Generates a (name, caseVariant) pair where caseVariant differs only by the case of
    /// individual letters: <c>variant.ToLowerInvariant() == name.ToLowerInvariant()</c>.
    /// </summary>
    private static Gen<NameAndVariant> GenNameAndVariant() =>
        from name in GenMonitorName()
        from toggles in GenListOfLength(name.Length, Arb.Generate<bool>())
        let variant = new string(
            name.Select((c, i) => toggles[i]
                ? (char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c))
                : c).ToArray())
        select new NameAndVariant(name, variant);

    /// <summary>Arbitrary used by the property test.</summary>
    public static Arbitrary<NameAndVariant> NameAndVariant() => Arb.From(GenNameAndVariant());
}

/// <summary>A monitor name together with a case-variant of that name.</summary>
public sealed record NameAndVariant(string Name, string Variant);

/// <summary>
/// Property and example tests for case-insensitive monitor identifier resolution
/// (<see cref="MonitorService.FindMonitor(IReadOnlyList{MonitorState}, string)"/>).
/// </summary>
public class MonitorIdentifierResolutionTests
{
    // Feature: monitor-brightness-controller, Property 6: Case-Insensitive Monitor Identifier Resolution
    // Validates: Requirements 3.2
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(MonitorIdentifierArbitraries) })]
    public void MonitorLookup_IsCaseInsensitive(NameAndVariant input)
    {
        // Single monitor identified by an alphabetic (non-numeric) name.
        var monitors = new List<MonitorState>
        {
            new()
            {
                MonitorIndex = 1,
                MonitorName = input.Name,
                DevicePath = @"\\?\DISPLAY#ABC123#5&deadbeef",
                IsControllable = true,
            },
        };

        MonitorState? byOriginal = MonitorService.FindMonitor(monitors, input.Name);
        MonitorState? byVariant = MonitorService.FindMonitor(monitors, input.Variant);

        // The case variant must resolve, and to the same monitor as the original name.
        byVariant.Should().NotBeNull();
        byVariant!.DevicePath.Should().Be(byOriginal!.DevicePath);
        byVariant.MonitorIndex.Should().Be(byOriginal.MonitorIndex);
    }

    [Fact]
    public void MonitorLookup_IsCaseInsensitive_ConcreteExample()
    {
        var monitors = new List<MonitorState>
        {
            new()
            {
                MonitorIndex = 1,
                MonitorName = "DELL U2723QE",
                DevicePath = @"\\?\DISPLAY#DEL41AB#5&abc",
                IsControllable = true,
            },
            new()
            {
                MonitorIndex = 2,
                MonitorName = "LG UltraFine",
                DevicePath = @"\\?\DISPLAY#GSM59AB#7&def",
                IsControllable = true,
            },
        };

        MonitorService.FindMonitor(monitors, "dell u2723qe")!.DevicePath
            .Should().Be(@"\\?\DISPLAY#DEL41AB#5&abc");
        MonitorService.FindMonitor(monitors, "lg ULTRAFINE")!.MonitorIndex
            .Should().Be(2);
    }
}
