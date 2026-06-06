using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using MonitorBrightnessController.Application;
using Xunit;

namespace MonitorBrightnessController.Tests;

/// <summary>
/// Custom FsCheck generators/arbitraries for the profile-name-validation property test
/// (Property 10). Produces strings that exercise the full input space: varying lengths
/// (including the boundaries 0, 1, 64, 65, and longer) drawn from a character set that mixes
/// allowed characters (<c>[a-zA-Z0-9_-]</c>) with disallowed ones (spaces, punctuation,
/// symbols), so that both accepted and rejected names are generated frequently.
/// </summary>
public static class ProfileNameValidationArbitraries
{
    // Characters that the production predicate must accept.
    private static readonly char[] AllowedChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-".ToCharArray();

    // Characters that the production predicate must reject (chosen to be representative
    // of disallowed input: whitespace, punctuation, symbols, and non-ASCII letters).
    private static readonly char[] DisallowedChars =
        " .!@#$%^&*()+=/\\|{}[]:;\"'<>,?~`\t\nàç日".ToCharArray();

    private static Gen<char> GenAllowedChar() => Gen.Elements(AllowedChars);

    private static Gen<char> GenAnyChar() =>
        // Bias towards a roughly even split between allowed and disallowed characters so
        // that, across many lengths, we generate both valid and invalid names regularly.
        Gen.OneOf(GenAllowedChar(), Gen.Elements(DisallowedChars));

    private static Gen<string> GenStringOfLength(int n, Gen<char> charGen) =>
        n <= 0
            ? Gen.Constant(string.Empty)
            : Gen.ArrayOf(n, charGen).Select(chars => new string(chars));

    private static Gen<string> GenAtLength(int length) =>
        // For a fixed length, sometimes use only allowed chars (more likely valid when the
        // length is in range) and sometimes use the mixed alphabet (more likely invalid).
        Gen.OneOf(GenStringOfLength(length, GenAllowedChar()), GenStringOfLength(length, GenAnyChar()));

    /// <summary>
    /// Generates candidate profile names across a wide range of lengths, deliberately
    /// oversampling the validation boundaries (0, 1, 64, 65) plus some longer strings,
    /// and mixing allowed/disallowed character sets.
    /// </summary>
    public static Arbitrary<string> ProfileName()
    {
        Gen<string> boundaryLengths = Gen.Elements(0, 1, 2, 63, 64, 65, 80, 200)
            .SelectMany(GenAtLength);

        Gen<string> randomLengths = Gen.Choose(0, 70).SelectMany(GenAtLength);

        return Arb.From(Gen.OneOf(boundaryLengths, randomLengths));
    }
}

/// <summary>
/// Property and example tests for profile name validation (Property 10).
/// </summary>
public class ProfileNameValidationTests
{
    // Independent reference predicate: a name is valid iff its length is in [1, 64] and it
    // matches [a-zA-Z0-9_-] for every character. Implemented via Regex to be independent of
    // the production implementation under test.
    //
    // NOTE: the anchors are \A and \z (absolute start/end of string), NOT ^ and $. In .NET,
    // $ also matches immediately before a trailing newline, so "abc\n" would be (incorrectly)
    // accepted by ^[a-zA-Z0-9_-]+$. Using \z requires the entire string to consist solely of
    // the allowed characters, matching the production rule that a literal newline is invalid.
    private static readonly Regex CharsetRegex =
        new(@"\A[a-zA-Z0-9_-]+\z", RegexOptions.Compiled);

    private static bool ExpectedValid(string name) =>
        name.Length >= 1 && name.Length <= 64 && CharsetRegex.IsMatch(name);

    // Feature: monitor-brightness-controller, Property 10: Profile Name Validation
    // Validates: Requirements 4.3
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ProfileNameValidationArbitraries) })]
    public void ProfileName_ValidatesCorrectly(string name)
    {
        bool actual = ProfileManager.IsValidProfileName(name);
        bool expected = ExpectedValid(name);

        actual.Should().Be(
            expected,
            "name '{0}' (length {1}) should be {2}",
            name,
            name.Length,
            expected ? "accepted" : "rejected");
    }

    [Theory]
    [InlineData("focus")]
    [InlineData("movie-mode")]
    [InlineData("night_2")]
    [InlineData("A")]
    [InlineData("0")]
    [InlineData("_")]
    [InlineData("-")]
    public void IsValidProfileName_AcceptsValidExamples(string name)
    {
        ProfileManager.IsValidProfileName(name).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("dot.name")]
    [InlineData("emoji😀")]
    [InlineData("slash/name")]
    public void IsValidProfileName_RejectsInvalidExamples(string? name)
    {
        ProfileManager.IsValidProfileName(name).Should().BeFalse();
    }

    [Fact]
    public void IsValidProfileName_EnforcesLengthBoundaries()
    {
        // Length 1 and 64 are accepted; 0 and 65 are rejected (charset all valid).
        ProfileManager.IsValidProfileName(new string('a', 1)).Should().BeTrue();
        ProfileManager.IsValidProfileName(new string('a', 64)).Should().BeTrue();
        ProfileManager.IsValidProfileName(new string('a', 65)).Should().BeFalse();
        ProfileManager.IsValidProfileName(string.Empty).Should().BeFalse();
    }
}
