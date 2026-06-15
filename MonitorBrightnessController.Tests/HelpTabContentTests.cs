using System.IO;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace MonitorBrightnessController.Tests;

/// <summary>
/// Unit tests verifying that the HelpTab UserControl contains all required
/// documentation sections with proper heading and description structure.
/// Validates: Requirements 4.2, 4.4
/// </summary>
public class HelpTabContentTests
{
    private static readonly string[] RequiredSectionHeadings =
    [
        "Monitor Brightness & Gamma Control",
        "Profiles",
        "Smooth Transitions",
        "System Tray Behavior",
        "Startup Settings",
        "CLI Usage",
        "Silent Startup Mode",
        "Auto-Update Notifications",
        "Shortcut Creation",
        "Proper Install"
    ];

    private static readonly XNamespace PresentationNs =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private readonly XDocument _xamlDoc;
    private readonly List<XElement> _stackPanelChildren;

    public HelpTabContentTests()
    {
        // Locate the HelpTab.xaml file relative to the test assembly
        var projectRoot = FindProjectRoot();
        var xamlPath = Path.Combine(projectRoot, "MonitorBrightnessController",
            "Presentation", "HelpTab.xaml");

        File.Exists(xamlPath).Should().BeTrue(
            $"HelpTab.xaml should exist at {xamlPath}");

        _xamlDoc = XDocument.Load(xamlPath);

        // Navigate: UserControl > ScrollViewer > StackPanel > children
        var scrollViewer = _xamlDoc.Root!
            .Element(PresentationNs + "ScrollViewer");
        scrollViewer.Should().NotBeNull("HelpTab should contain a ScrollViewer");

        var stackPanel = scrollViewer!
            .Element(PresentationNs + "StackPanel");
        stackPanel.Should().NotBeNull("ScrollViewer should contain a StackPanel");

        _stackPanelChildren = stackPanel!
            .Elements(PresentationNs + "TextBlock")
            .ToList();
    }

    /// <summary>
    /// Verifies that all 10 required documentation section headings are present in the HelpTab.
    /// Validates: Requirements 4.2
    /// </summary>
    [Fact]
    public void HelpTab_ContainsAll10RequiredSectionHeadings()
    {
        var headings = GetHeadingTextBlocks()
            .Select(GetTextContent)
            .ToList();

        headings.Should().HaveCountGreaterThanOrEqualTo(10,
            "HelpTab should have at least 10 documentation sections");

        foreach (var requiredHeading in RequiredSectionHeadings)
        {
            headings.Should().Contain(requiredHeading,
                $"HelpTab should contain a section for '{requiredHeading}'");
        }
    }

    /// <summary>
    /// Verifies that each section heading is followed by a description TextBlock with text wrapping.
    /// Validates: Requirements 4.4
    /// </summary>
    [Fact]
    public void HelpTab_EachSectionHasHeadingFollowedByDescription()
    {
        for (int i = 0; i < _stackPanelChildren.Count - 1; i++)
        {
            var element = _stackPanelChildren[i];

            if (!IsHeadingTextBlock(element))
                continue;

            var heading = GetTextContent(element);

            // The next TextBlock should be the description
            var nextIndex = i + 1;
            nextIndex.Should().BeLessThan(_stackPanelChildren.Count,
                $"Section '{heading}' should have a description TextBlock following it");

            var description = _stackPanelChildren[nextIndex];
            var textWrapping = description.Attribute("TextWrapping")?.Value;
            textWrapping.Should().Be("Wrap",
                $"Description for section '{heading}' should have TextWrapping='Wrap'");

            var descContent = GetTextContent(description);
            descContent.Should().NotBeNullOrWhiteSpace(
                $"Description for section '{heading}' should have non-empty text content");
        }
    }

    /// <summary>
    /// Verifies there are exactly 10 section headings (one per required topic).
    /// Validates: Requirements 4.2
    /// </summary>
    [Fact]
    public void HelpTab_HasExactly10Sections()
    {
        var headings = GetHeadingTextBlocks().ToList();
        headings.Should().HaveCount(10,
            "HelpTab should have exactly 10 documentation sections");
    }

    /// <summary>
    /// Verifies that each heading TextBlock uses bold font weight for visual distinction.
    /// Validates: Requirements 4.4
    /// </summary>
    [Fact]
    public void HelpTab_HeadingsAreBold()
    {
        var headings = GetHeadingTextBlocks().ToList();

        foreach (var heading in headings)
        {
            var fontWeight = heading.Attribute("FontWeight")?.Value;
            fontWeight.Should().Be("Bold",
                $"Heading '{GetTextContent(heading)}' should have FontWeight='Bold'");
        }
    }

    private IEnumerable<XElement> GetHeadingTextBlocks()
    {
        return _stackPanelChildren.Where(IsHeadingTextBlock);
    }

    private static bool IsHeadingTextBlock(XElement element)
    {
        // A heading TextBlock has FontWeight="Bold" and a Text attribute
        var fontWeight = element.Attribute("FontWeight")?.Value;
        var hasText = element.Attribute("Text") != null;
        return fontWeight == "Bold" && hasText;
    }

    private static string GetTextContent(XElement textBlock)
    {
        // Text can come from the Text attribute or from inner content
        var textAttr = textBlock.Attribute("Text")?.Value;
        if (!string.IsNullOrEmpty(textAttr))
            return textAttr;

        // Fall back to inner text content (for multi-line descriptions)
        return textBlock.Value.Trim();
    }

    private static string FindProjectRoot()
    {
        // Walk up from the test assembly output directory to find the solution root
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "MonitorBrightnessController")) &&
                Directory.Exists(Path.Combine(dir, "MonitorBrightnessController.Tests")))
            {
                return dir;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }

        // Fallback: try relative from test output
        throw new InvalidOperationException(
            "Could not find project root directory containing MonitorBrightnessController and MonitorBrightnessController.Tests");
    }
}
