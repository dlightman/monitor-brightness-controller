using System.IO;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace MonitorBrightnessController.Tests.UnitTests;

/// <summary>
/// Verifies the tab structure of MainWindow.xaml by parsing the XAML as XML.
/// Requirements: 4.1, 4.2, 4.3, 8.1, 8.2, 8.3
/// </summary>
public class TabStructureTests
{
    private static readonly XNamespace PresentationNs =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private readonly List<XElement> _tabItems;
    private readonly XElement _tabControl;

    public TabStructureTests()
    {
        var xamlPath = FindMainWindowXaml();
        var doc = XDocument.Load(xamlPath);
        _tabControl = doc.Descendants(PresentationNs + "TabControl").First();
        _tabItems = _tabControl.Elements(PresentationNs + "TabItem").ToList();
    }

    /// <summary>
    /// Requirement 4.2, 8.1: The Application SHALL display exactly 3 tabs with headers
    /// "Monitors", "Settings", "About" in left-to-right order.
    /// </summary>
    [Fact]
    public void TabControl_HasExactlyThreeTabs()
    {
        _tabItems.Should().HaveCount(3);
    }

    /// <summary>
    /// Requirement 4.2, 8.1: Tabs are in the correct order with correct headers.
    /// </summary>
    [Fact]
    public void TabHeaders_AreInCorrectOrder()
    {
        var headers = _tabItems
            .Select(ti => ti.Attribute("Header")?.Value)
            .ToList();

        headers.Should().ContainInOrder("Monitors", "Settings", "About");
    }

    /// <summary>
    /// Requirement 4.1, 8.2: The Application SHALL NOT display a "Profiles" tab.
    /// </summary>
    [Fact]
    public void TabControl_DoesNotContainProfilesTab()
    {
        var headers = _tabItems
            .Select(ti => ti.Attribute("Header")?.Value)
            .ToList();

        headers.Should().NotContain("Profiles");
    }

    /// <summary>
    /// Requirement 8.2: The Application SHALL NOT display a "Help" tab.
    /// </summary>
    [Fact]
    public void TabControl_DoesNotContainHelpTab()
    {
        var headers = _tabItems
            .Select(ti => ti.Attribute("Header")?.Value)
            .ToList();

        headers.Should().NotContain("Help");
    }

    /// <summary>
    /// Requirement 4.3, 8.3: WHEN the Application launches, the "Monitors" tab
    /// SHALL be selected by default (SelectedIndex="0").
    /// </summary>
    [Fact]
    public void TabControl_MonitorsTabIsSelectedByDefault()
    {
        var selectedIndex = _tabControl.Attribute("SelectedIndex")?.Value;
        selectedIndex.Should().Be("0", "Monitors tab should be selected by default");
    }

    /// <summary>
    /// Requirement 4.2, 8.1: First tab header is "Monitors".
    /// </summary>
    [Fact]
    public void FirstTab_IsMonitors()
    {
        _tabItems[0].Attribute("Header")?.Value.Should().Be("Monitors");
    }

    /// <summary>
    /// Requirement 4.2, 8.1: Second tab header is "Settings".
    /// </summary>
    [Fact]
    public void SecondTab_IsSettings()
    {
        _tabItems[1].Attribute("Header")?.Value.Should().Be("Settings");
    }

    /// <summary>
    /// Requirement 7.1, 8.1: Third tab header is "About".
    /// </summary>
    [Fact]
    public void ThirdTab_IsAbout()
    {
        _tabItems[2].Attribute("Header")?.Value.Should().Be("About");
    }

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
