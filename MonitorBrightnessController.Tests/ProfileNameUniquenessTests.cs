using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using MonitorBrightnessController.Application;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;
using Xunit;
using MbcUnit = MonitorBrightnessController.Models.Unit;

namespace MonitorBrightnessController.Tests;

/// <summary>
/// Minimal in-memory <see cref="ISettingsStore"/> fake for the profile name uniqueness
/// property test. Holds <see cref="AppSettings"/> entirely in memory so that
/// <see cref="ProfileManager"/> can be exercised without touching disk. The unusual class
/// name avoids collisions with any other in-memory fakes in the test project.
/// </summary>
public sealed class InMemorySettingsStore_Uniqueness : ISettingsStore
{
    private AppSettings _settings;

    public InMemorySettingsStore_Uniqueness(AppSettings initial)
    {
        _settings = initial ?? throw new ArgumentNullException(nameof(initial));
    }

    public AppSettings Load() => _settings;

    public Result<MbcUnit> Save(AppSettings settings)
    {
        _settings = settings;
        return Result<MbcUnit>.Success(MbcUnit.Value);
    }
}

/// <summary>
/// Custom FsCheck generators for case-insensitive profile name uniqueness. Generates an
/// existing valid profile name P (containing at least one letter so that toggling case is
/// meaningful) together with a case variant Q where
/// <c>Q.ToLowerInvariant() == P.ToLowerInvariant()</c>.
/// </summary>
public static class ProfileNameUniquenessArbitraries
{
    // Letters only — guarantees at least one case-toggleable character and keeps the name
    // within the valid [a-zA-Z0-9_-] charset.
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

    /// <summary>A valid, non-empty, all-letter profile name (length 1-64).</summary>
    private static Gen<string> GenProfileName() =>
        from len in Gen.Choose(1, 64)
        from chars in GenListOfLength(len, Gen.Elements(LetterChars))
        select new string(chars.ToArray());

    /// <summary>
    /// Generates a (name, caseVariant) pair where the variant differs only by the case of
    /// individual letters: <c>variant.ToLowerInvariant() == name.ToLowerInvariant()</c>.
    /// </summary>
    private static Gen<ProfileNameAndVariant> GenNameAndVariant() =>
        from name in GenProfileName()
        from toggles in GenListOfLength(name.Length, Arb.Generate<bool>())
        let variant = new string(
            name.Select((c, i) => toggles[i]
                ? (char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c))
                : c).ToArray())
        select new ProfileNameAndVariant(name, variant);

    /// <summary>Arbitrary used by the property test.</summary>
    public static Arbitrary<ProfileNameAndVariant> NameAndVariant() => Arb.From(GenNameAndVariant());
}

/// <summary>An existing profile name together with a case-variant of that name.</summary>
public sealed record ProfileNameAndVariant(string Name, string Variant);

/// <summary>
/// Property and example tests for case-insensitive profile name uniqueness in
/// <see cref="ProfileManager.CreateProfile(string, IReadOnlyDictionary{string, int}, IReadOnlyDictionary{string, int}?)"/>.
/// </summary>
public class ProfileNameUniquenessTests
{
    private static readonly IReadOnlyDictionary<string, int> SampleMap =
        new Dictionary<string, int> { [@"\\?\DISPLAY#ABC123#5&deadbeef"] = 50 };

    // Feature: monitor-brightness-controller, Property 12: Case-Insensitive Profile Name Uniqueness
    // Validates: Requirements 4.7
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ProfileNameUniquenessArbitraries) })]
    public void ProfileName_UniqueCaseInsensitive(ProfileNameAndVariant input)
    {
        // Seed the store with a single existing profile named P.
        var store = new InMemorySettingsStore_Uniqueness(new AppSettings
        {
            Profiles = new List<Profile>
            {
                new()
                {
                    Name = input.Name,
                    MonitorBrightnessMap = new Dictionary<string, int>(SampleMap),
                },
            },
        });

        var manager = new ProfileManager(store);

        // Attempt to create a new profile whose name is a case variant Q of P.
        Result<MbcUnit> result = manager.CreateProfile(input.Variant, SampleMap, null);

        // Creation must be rejected as a duplicate, and the profile count must stay at 1.
        result.IsSuccess.Should().BeFalse(
            "a name that case-insensitively matches an existing profile must be rejected");
        manager.GetAllProfiles().Should().HaveCount(1);
    }

    [Fact]
    public void ProfileName_UniqueCaseInsensitive_ConcreteExample()
    {
        var store = new InMemorySettingsStore_Uniqueness(new AppSettings
        {
            Profiles = new List<Profile>
            {
                new() { Name = "Focus", MonitorBrightnessMap = new Dictionary<string, int>(SampleMap) },
            },
        });

        var manager = new ProfileManager(store);

        manager.CreateProfile("focus", SampleMap, null).IsSuccess.Should().BeFalse();
        manager.CreateProfile("FOCUS", SampleMap, null).IsSuccess.Should().BeFalse();
        manager.CreateProfile("FoCuS", SampleMap, null).IsSuccess.Should().BeFalse();
        manager.GetAllProfiles().Should().HaveCount(1);
    }
}
