using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;
using MonitorBrightnessController.Presentation;
using NSubstitute;

namespace MonitorBrightnessController.Tests.Properties;

// Feature: ui-consolidation, Property 6: Profile dropdown alphabetical ordering

/// <summary>
/// Property-based tests verifying that the profile dropdown lists saved profile names
/// in case-insensitive alphabetical order.
/// </summary>
public class ProfileDropdownProperties
{
    /// <summary>
    /// Creates a mock IMonitorService that returns an empty list of monitors.
    /// </summary>
    private static IMonitorService EmptyMonitorService()
    {
        var service = Substitute.For<IMonitorService>();
        service.DetectMonitors().Returns(new List<MonitorState>());
        return service;
    }

    /// <summary>
    /// Creates a mock IProfileManager that returns profiles with the given names.
    /// </summary>
    private static IProfileManager ProfileManagerWith(IEnumerable<string> profileNames)
    {
        var profiles = profileNames
            .Select(name => new Profile
            {
                Name = name,
                MonitorBrightnessMap = new Dictionary<string, int>()
            })
            .ToList();

        var manager = Substitute.For<IProfileManager>();
        manager.GetAllProfiles().Returns(profiles);
        return manager;
    }

    /// <summary>
    /// Property 6: Profile dropdown alphabetical ordering
    ///
    /// For any set of saved profile names, the profile dropdown SHALL list them
    /// in case-insensitive alphabetical order.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 3.2, 5.2**
    /// </remarks>
    [Property(MaxTest = 100)]
    public Property ProfileNames_AreSorted_CaseInsensitiveAlphabetical()
    {
        // Generator for valid profile names (1-20 chars from allowed characters)
        var allowedChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-".ToCharArray();

        var profileNameGen = Gen.Choose(1, 20)
            .SelectMany(len =>
                Gen.ArrayOf(len, Gen.Elements(allowedChars))
                    .Select(chars => new string(chars)));

        // Generate lists of 0-15 distinct profile names (case-insensitive distinct)
        var profileListGen = Gen.Choose(0, 15)
            .SelectMany(count => Gen.ListOf(count, profileNameGen))
            .Select(names => names
                .GroupBy(n => n.ToUpperInvariant())
                .Select(g => g.First())
                .ToList());

        return Prop.ForAll(Arb.From(profileListGen), profileNames =>
        {
            // Arrange
            var profileManager = ProfileManagerWith(profileNames);
            var monitorService = EmptyMonitorService();

            var vm = new ProfileStripViewModel(profileManager, monitorService);

            // Act - ProfileNames is populated in the constructor via RefreshProfiles()
            var actualNames = vm.ProfileNames.ToList();

            // Assert: the list should be sorted case-insensitive alphabetically
            var expectedSorted = profileNames
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            actualNames.Should().Equal(expectedSorted,
                "ProfileNames should be in case-insensitive alphabetical order");
        });
    }
}
