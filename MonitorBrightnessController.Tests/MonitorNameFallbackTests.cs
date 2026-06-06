using System.Linq;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using MonitorBrightnessController.Application;
using Xunit;

namespace MonitorBrightnessController.Tests;

/// <summary>
/// Custom FsCheck generators/arbitraries for the Monitor Name Fallback property.
/// Generates monitor indices in a realistic range plus name strings that are either
/// "blank" (null, empty, or whitespace-only) or genuinely non-whitespace.
/// </summary>
public static class MonitorNameFallbackArbitraries
{
    private static readonly char[] WhitespaceChars = { ' ', '\t', '\n', '\r', '\f', '\v' };

    private static readonly char[] NonWhitespaceChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 -_#".ToCharArray();

    /// <summary>Generates a list of exactly <paramref name="n"/> elements (version-agnostic).</summary>
    private static Gen<System.Collections.Generic.List<T>> GenListOfLength<T>(int n, Gen<T> elementGen)
    {
        if (n <= 0)
        {
            return Gen.Constant(new System.Collections.Generic.List<T>());
        }

        return elementGen.SelectMany(head =>
            GenListOfLength(n - 1, elementGen).Select(tail =>
            {
                tail.Insert(0, head);
                return tail;
            }));
    }

    /// <summary>Monitor indices: realistic 1-100 range.</summary>
    public static Arbitrary<int> MonitorIndex() => Arb.From(Gen.Choose(1, 100));

    /// <summary>
    /// A "blank" name: null, empty, or a non-empty string consisting solely of whitespace.
    /// </summary>
    private static Gen<string?> GenBlankName() =>
        Gen.OneOf(
            Gen.Constant((string?)null),
            Gen.Constant((string?)string.Empty),
            from len in Gen.Choose(1, 6)
            from chars in GenListOfLength(len, Gen.Elements(WhitespaceChars))
            select (string?)new string(chars.ToArray()));

    /// <summary>
    /// A genuine name containing at least one non-whitespace character. We build a name with
    /// arbitrary characters (which may include spaces) but guarantee a non-whitespace anchor.
    /// </summary>
    private static Gen<string> GenNonBlankName() =>
        from prefixLen in Gen.Choose(0, 5)
        from prefix in GenListOfLength(prefixLen, Gen.Elements(NonWhitespaceChars))
        from anchor in Gen.Elements("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray())
        from suffixLen in Gen.Choose(0, 5)
        from suffix in GenListOfLength(suffixLen, Gen.Elements(NonWhitespaceChars))
        select new string(prefix.Append(anchor).Concat(suffix).ToArray());

    public static Arbitrary<BlankName> BlankName() =>
        Arb.From(GenBlankName().Select(n => new BlankName(n)));

    public static Arbitrary<NonBlankName> NonBlankName() =>
        Arb.From(GenNonBlankName().Select(n => new NonBlankName(n)));
}

/// <summary>Wrapper marking a name that is null/empty/whitespace-only.</summary>
public sealed record BlankName(string? Value);

/// <summary>Wrapper marking a name with at least one non-whitespace character.</summary>
public sealed record NonBlankName(string Value);

/// <summary>
/// Property tests for <see cref="MonitorService.ResolveMonitorName"/>.
/// </summary>
public class MonitorNameFallbackTests
{
    // Feature: monitor-brightness-controller, Property 2: Monitor Name Fallback
    // Validates: Requirements 1.2
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(MonitorNameFallbackArbitraries) })]
    public void MonitorName_FallsBackToMonitorN_ForBlankNames(int index, BlankName name)
    {
        var resolved = MonitorService.ResolveMonitorName(name.Value, index);

        resolved.Should().Be($"Monitor {index}");
    }

    // Feature: monitor-brightness-controller, Property 2: Monitor Name Fallback
    // Validates: Requirements 1.2
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(MonitorNameFallbackArbitraries) })]
    public void MonitorName_PreservesNonBlankNames(int index, NonBlankName name)
    {
        var resolved = MonitorService.ResolveMonitorName(name.Value, index);

        resolved.Should().Be(name.Value);
    }

    [Theory]
    [InlineData(1, null)]
    [InlineData(2, "")]
    [InlineData(3, "   ")]
    [InlineData(42, "\t\n")]
    public void MonitorName_FallsBack_ConcreteExamples(int index, string? rawName)
    {
        MonitorService.ResolveMonitorName(rawName, index).Should().Be($"Monitor {index}");
    }

    [Theory]
    [InlineData(1, "DELL U2723QE")]
    [InlineData(7, "LG-32UN880")]
    public void MonitorName_PreservesName_ConcreteExamples(int index, string rawName)
    {
        MonitorService.ResolveMonitorName(rawName, index).Should().Be(rawName);
    }
}
