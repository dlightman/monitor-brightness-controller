using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;
using MonitorBrightnessController.Presentation;
using NSubstitute;
using Xunit;

namespace MonitorBrightnessController.Tests.Unit;

/// <summary>
/// Unit tests for About tab content: version format, build date format, and hyperlink URL.
/// Requirements: 7.2, 7.3, 7.4
/// </summary>
public class AboutTabTests
{
    private static readonly XNamespace PresentationNs =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    /// <summary>
    /// The main application assembly containing the ViewModel and metadata attributes.
    /// </summary>
    private static readonly Assembly MainAssembly = typeof(MainWindowViewModel).Assembly;

    #region ViewModel property tests

    /// <summary>
    /// Requirement 7.3: The About_Tab SHALL display the current build version
    /// in the format "Major.Minor.Patch" (e.g., "1.2.0").
    /// </summary>
    [Fact]
    public void AppVersion_MatchesMajorMinorPatchFormat()
    {
        var monitorService = Substitute.For<IMonitorService>();
        monitorService.DetectMonitors().Returns(new List<MonitorState>());

        var vm = new MainWindowViewModel(monitorService);

        vm.AppVersion.Should().MatchRegex(@"^\d+\.\d+\.\d+$",
            "AppVersion should be in Major.Minor.Patch format");
    }

    /// <summary>
    /// Requirement 7.4: The About_Tab SHALL display the build date
    /// in the format "yyyy-MM-dd" (e.g., "2025-01-15"), pulled from
    /// the assembly's metadata at compile time.
    /// Verifies the format of the BuildDate embedded in the main application assembly.
    /// </summary>
    [Fact]
    public void BuildDate_MatchesIsoDateFormat()
    {
        var buildDate = MainAssembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "BuildDate")?.Value;

        buildDate.Should().NotBeNull("BuildDate metadata should be embedded in the assembly");
        buildDate.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}$",
            "BuildDate should be in yyyy-MM-dd format");
    }

    /// <summary>
    /// Requirement 7.4: The build date should be a valid parseable date.
    /// </summary>
    [Fact]
    public void BuildDate_IsValidDate()
    {
        var buildDate = MainAssembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "BuildDate")?.Value;

        buildDate.Should().NotBeNull();

        var parsed = DateTime.TryParseExact(buildDate, "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _);

        parsed.Should().BeTrue("BuildDate should be a valid date in yyyy-MM-dd format");
    }

    #endregion

    #region XAML Hyperlink tests

    /// <summary>
    /// Requirement 7.2: The hyperlink on the About_Tab SHALL navigate to
    /// https://github.com/dlightman/monitor-brightness-controller
    /// </summary>
    [Fact]
    public void Hyperlink_NavigateUri_IsCorrectGitHubUrl()
    {
        var xamlPath = FindMainWindowXaml();
        var doc = XDocument.Load(xamlPath);

        var tabItems = doc.Descendants(PresentationNs + "TabControl")
            .First()
            .Elements(PresentationNs + "TabItem")
            .ToList();

        // About tab is the fourth tab (after Monitors, Settings, Help)
        var aboutTab = tabItems[3];
        aboutTab.Attribute("Header")?.Value.Should().Be("About");

        var hyperlink = aboutTab.Descendants(PresentationNs + "Hyperlink").First();
        var navigateUri = hyperlink.Attribute("NavigateUri")?.Value;

        navigateUri.Should().Be("https://github.com/dlightman/monitor-brightness-controller",
            "the About tab hyperlink should point to the project's GitHub repository");
    }

    #endregion

    private static string FindMainWindowXaml()
    {
        // Walk up from the test assembly output directory to find the XAML file
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "MonitorBrightnessController",
                "Presentation", "MainWindow.xaml");
            if (File.Exists(candidate))
                return candidate;

            // Also check sibling directory pattern (test project next to main project)
            var parent = Directory.GetParent(dir)?.FullName;
            if (parent != null)
            {
                candidate = Path.Combine(parent, "MonitorBrightnessController",
                    "Presentation", "MainWindow.xaml");
                if (File.Exists(candidate))
                    return candidate;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new FileNotFoundException(
            "Could not locate MainWindow.xaml. Ensure the source tree is accessible from the test output directory.");
    }
}
