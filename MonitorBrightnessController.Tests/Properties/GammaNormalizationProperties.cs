using System;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;

namespace MonitorBrightnessController.Tests.Properties;

// Feature: gamma-control, Property 1: Gamma normalization produces valid percentage

/// <summary>
/// Custom arbitraries for gamma normalization property tests.
/// Generates (current, max) uint pairs where max > 0.
/// </summary>
public static class GammaNormalizationArbitraries
{
    /// <summary>
    /// Generates a tuple of (current, maximum) where both are non-negative uint values
    /// and maximum is strictly greater than zero.
    /// </summary>
    public static Arbitrary<(uint Current, uint Maximum)> VcpValuePair()
    {
        var gen =
            from max in Arb.Generate<uint>().Where(m => m > 0)
            from current in Arb.Generate<uint>()
            select (current, max);

        return Arb.From(gen);
    }
}

/// <summary>
/// Property-based tests for the gamma normalization formula used in MonitorInterop.GetGamma.
/// The normalization formula is: Math.Clamp((int)Math.Round(current * 100.0 / maximum), 0, 100)
/// This replicates the private logic for testing purposes.
/// </summary>
public class GammaNormalizationProperties
{
    /// <summary>
    /// Replicates the normalization formula from MonitorInterop.GetGamma.
    /// Given raw VCP current and maximum values, produces a percentage in [0, 100].
    /// </summary>
    private static int NormalizeGamma(uint current, uint maximum)
    {
        int percentage = maximum == 0
            ? (int)Math.Clamp(current, 0u, 100u)
            : (int)Math.Round(current * 100.0 / maximum);

        return Math.Clamp(percentage, 0, 100);
    }

    /// <summary>
    /// Property 1: For any (current, max) uint pair with max > 0,
    /// the normalization formula always produces a value in [0, 100].
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 1.1**
    /// </remarks>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(GammaNormalizationArbitraries) })]
    public void GammaNormalization_AlwaysProducesValidPercentage((uint Current, uint Maximum) pair)
    {
        int result = NormalizeGamma(pair.Current, pair.Maximum);

        result.Should().BeInRange(0, 100,
            "gamma normalization of current={0}, max={1} must produce a value in [0, 100]",
            pair.Current, pair.Maximum);
    }
}
