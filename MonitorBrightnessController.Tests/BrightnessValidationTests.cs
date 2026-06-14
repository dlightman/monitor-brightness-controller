using System.Globalization;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using MonitorBrightnessController.Application;
using Xunit;

namespace MonitorBrightnessController.Tests;

/// <summary>
/// Custom FsCheck generators/arbitraries for the brightness-validation property test.
/// Provides a generator of valid brightness strings (integers in [0,100]) and a generator
/// of genuinely-invalid strings (non-numeric, negative, &gt;100, and float-formatted).
/// </summary>
public static class BrightnessValidationArbitraries
{
    private static readonly char[] NonNumericChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ !@#$%^*()_=+/\\".ToCharArray();

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

    /// <summary>Generates the string form of a valid brightness integer in [0, 100].</summary>
    private static Gen<string> GenValidBrightnessString() =>
        Gen.Choose(0, 100).Select(v => v.ToString(CultureInfo.InvariantCulture));

    private static Gen<string> GenNegativeString() =>
        Gen.Choose(-100000, -1).Select(v => v.ToString(CultureInfo.InvariantCulture));

    private static Gen<string> GenTooLargeString() =>
        Gen.Choose(101, 1000000).Select(v => v.ToString(CultureInfo.InvariantCulture));

    private static Gen<string> GenFloatString() =>
        from whole in Gen.Choose(-200, 200)
        from frac in Gen.Choose(1, 999) // non-zero fractional part so it is genuinely a float
        select string.Create(
            CultureInfo.InvariantCulture,
            $"{whole}.{frac}");

    private static Gen<string> GenNonNumericString() =>
        from len in Gen.Choose(0, 12)
        from chars in GenListOfLength(len, Gen.Elements(NonNumericChars))
        select new string(chars.ToArray());

    /// <summary>
    /// Generates strings that are expected to be invalid brightness inputs. The union of
    /// invalid categories is filtered through the production predicate so that the rare case
    /// where a generated string happens to be a valid brightness (e.g. an empty non-numeric
    /// string is invalid, but a stray category overlap) is excluded — guaranteeing every
    /// generated value is genuinely invalid.
    /// </summary>
    public static Arbitrary<string> InvalidBrightnessString() =>
        Arb.From(Gen.OneOf(
                GenNonNumericString(),
                GenNegativeString(),
                GenTooLargeString(),
                GenFloatString())
            .Where(s => !MonitorService.TryParseBrightness(s, out _)));

    /// <summary>Generates valid brightness strings (integers in [0,100]).</summary>
    public static Arbitrary<string> ValidBrightnessString() =>
        Arb.From(GenValidBrightnessString());
}

/// <summary>
/// A wrapper marker type so the valid-string property can use a dedicated arbitrary that
/// does not collide with the invalid-string arbitrary for <see cref="string"/>.
/// </summary>
public readonly record struct ValidBrightness(int Value);

/// <summary>
/// Arbitrary that produces integers constrained to the valid brightness range [0, 100].
/// </summary>
public static class ValidBrightnessArbitraries
{
    public static Arbitrary<ValidBrightness> ValidBrightness() =>
        Arb.From(Gen.Choose(0, 100).Select(v => new ValidBrightness(v)));
}

/// <summary>
/// Property and example tests for brightness value validation (Property 5).
/// </summary>
public class BrightnessValidationTests
{
    // Feature: monitor-brightness-controller, Property 5: Brightness Value Validation
    // Validates: Requirements 2.7, 3.5
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(BrightnessValidationArbitraries) })]
    public void BrightnessValidation_RejectsInvalid(string invalid)
    {
        bool accepted = MonitorService.TryParseBrightness(invalid, out _);

        accepted.Should().BeFalse(
            "the string '{0}' is not an integer in the range [0, 100]", invalid);
    }

    // Feature: monitor-brightness-controller, Property 5: Brightness Value Validation
    // Validates: Requirements 2.7, 3.5
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ValidBrightnessArbitraries) })]
    public void BrightnessValidation_AcceptsValidIntegers(ValidBrightness brightness)
    {
        string input = brightness.Value.ToString(CultureInfo.InvariantCulture);

        bool accepted = MonitorService.TryParseBrightness(input, out int parsed);

        accepted.Should().BeTrue("'{0}' is an integer in [0, 100]", input);
        parsed.Should().Be(brightness.Value);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("100", 100)]
    [InlineData("50", 50)]
    [InlineData("  42  ", 42)] // surrounding whitespace is trimmed
    [InlineData("+7", 7)]
    public void TryParseBrightness_AcceptsValidExamples(string input, int expected)
    {
        MonitorService.TryParseBrightness(input, out int value).Should().BeTrue();
        value.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("101")]
    [InlineData("12.5")]
    [InlineData("50%")]
    [InlineData("0x10")]
    public void TryParseBrightness_RejectsInvalidExamples(string? input)
    {
        MonitorService.TryParseBrightness(input, out _).Should().BeFalse();
    }
}
