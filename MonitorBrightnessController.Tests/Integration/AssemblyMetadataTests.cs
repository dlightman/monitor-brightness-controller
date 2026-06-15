using System.Reflection;
using System.Text.RegularExpressions;
using MonitorBrightnessController.Presentation;
using Xunit;

namespace MonitorBrightnessController.Tests.Integration;

/// <summary>
/// Integration tests verifying that assembly version and BuildDate metadata
/// are embedded at compile time and readable at runtime without network access
/// or external file reads.
/// Validates: Requirements 7.5, 7.6
/// </summary>
public class AssemblyMetadataTests
{
    private readonly Assembly _assembly = typeof(MainWindowViewModel).Assembly;

    [Fact]
    public void BuildDate_metadata_is_present_and_matches_date_format()
    {
        // Arrange & Act
        var buildDate = _assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "BuildDate")?.Value;

        // Assert
        Assert.NotNull(buildDate);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", buildDate);

        // Verify it's actually a valid date
        Assert.True(
            DateOnly.TryParseExact(buildDate, "yyyy-MM-dd", out _),
            $"BuildDate '{buildDate}' is not a valid date in yyyy-MM-dd format");
    }

    [Fact]
    public void Assembly_version_is_present_with_major_minor_build_components()
    {
        // Arrange & Act
        var version = _assembly.GetName().Version;

        // Assert
        Assert.NotNull(version);
        Assert.True(version.Major >= 0, "Major version should be >= 0");
        Assert.True(version.Minor >= 0, "Minor version should be >= 0");
        Assert.True(version.Build >= 0, "Build version should be >= 0");
    }

    [Fact]
    public void Assembly_version_formats_as_major_minor_patch()
    {
        // Arrange
        var version = _assembly.GetName().Version;

        // Act
        var formatted = $"{version!.Major}.{version.Minor}.{version.Build}";

        // Assert — should match the pattern "X.Y.Z" with non-negative integers
        Assert.Matches(@"^\d+\.\d+\.\d+$", formatted);
    }

    [Fact]
    public void Metadata_is_embedded_in_assembly_no_external_access_needed()
    {
        // This test verifies the metadata comes from assembly attributes (compile-time),
        // not from any external source. If we can read them from the assembly object,
        // they are embedded and require no network or file I/O at runtime.
        var allMetadata = _assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToList();

        // BuildDate must be among the embedded metadata attributes
        Assert.Contains(allMetadata, attr => attr.Key == "BuildDate");

        // Version is intrinsic to the assembly name — always embedded
        var assemblyName = _assembly.GetName();
        Assert.NotNull(assemblyName.Version);
    }
}
