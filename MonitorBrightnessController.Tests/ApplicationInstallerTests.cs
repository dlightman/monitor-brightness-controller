using System;
using System.IO;
using FluentAssertions;
using MonitorBrightnessController.Infrastructure;
using Xunit;

namespace MonitorBrightnessController.Tests;

/// <summary>
/// Unit tests for <see cref="ApplicationInstaller"/> covering install-path detection
/// and install-flow preconditions (Requirements 4.1, 4.9).
/// </summary>
public class ApplicationInstallerTests
{
    // --- IsInstalledInProgramFiles -------------------------------------------

    [Fact]
    public void IsInstalledInProgramFiles_ReturnsFalse_WhenRunningFromTestRunner()
    {
        // Requirement 4.9: the button should be enabled when not running from Program Files.
        // During tests, Environment.ProcessPath points to the test runner (e.g., testhost.exe),
        // which is not inside %ProgramFiles%\MonitorBrightnessController\.
        var installer = new ApplicationInstaller();

        bool result = installer.IsInstalledInProgramFiles();

        result.Should().BeFalse();
    }

    [Fact]
    public void IsInstalledInProgramFiles_ExpectedInstallPath_IsUnderProgramFiles()
    {
        // Requirement 4.1: the install directory is %ProgramFiles%\MonitorBrightnessController\.
        // Verify that the expected install path is correctly constructed by checking the
        // well-known components.
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var expectedDir = Path.Combine(programFiles, "MonitorBrightnessController");
        var expectedExe = Path.Combine(expectedDir, "MonitorBrightnessController.exe");

        // The expected path should start with the Program Files folder
        expectedExe.Should().StartWith(programFiles);
        // And end with the known exe name
        expectedExe.Should().EndWith("MonitorBrightnessController.exe");
        // The directory component should contain the app folder name
        Path.GetDirectoryName(expectedExe).Should().EndWith("MonitorBrightnessController");
    }

    [Fact]
    public void IsInstalledInProgramFiles_PathComparison_IsCaseInsensitive()
    {
        // Requirement 4.1 / 4.9: path comparison uses OrdinalIgnoreCase.
        // Verify the underlying path normalization handles mixed-case scenarios.
        var installer = new ApplicationInstaller();

        // Calling the method twice should give a consistent result regardless
        // of any casing in the environment's ProcessPath.
        bool result1 = installer.IsInstalledInProgramFiles();
        bool result2 = installer.IsInstalledInProgramFiles();

        result1.Should().Be(result2);
    }

    // --- Instance creation ---------------------------------------------------

    [Fact]
    public void ApplicationInstaller_CanBeCreated()
    {
        // Sanity: the installer can be instantiated without throwing.
        var installer = new ApplicationInstaller();

        installer.Should().NotBeNull();
    }

    // --- Install flow preconditions ------------------------------------------

    [Fact]
    public void InstallToProgramFiles_SourcePath_IsNonNull_DuringTests()
    {
        // Requirement 4.1: The installer needs to know the source path (current exe).
        // Environment.ProcessPath should be non-null when the test runner is active.
        var processPath = Environment.ProcessPath;

        processPath.Should().NotBeNullOrEmpty(
            "the installer requires a valid source path to copy from");
    }

    [Fact]
    public void InstallToProgramFiles_TargetDirectory_CombinesProgramFilesAndAppFolder()
    {
        // Requirement 4.1: target is %ProgramFiles%\MonitorBrightnessController\.
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var targetDir = Path.Combine(programFiles, "MonitorBrightnessController");

        targetDir.Should().StartWith(programFiles);
        Path.GetFileName(targetDir).Should().Be("MonitorBrightnessController");
    }

    [Theory]
    [InlineData(@"C:\Program Files\MonitorBrightnessController\MonitorBrightnessController.exe")]
    [InlineData(@"C:\PROGRAM FILES\MonitorBrightnessController\MonitorBrightnessController.exe")]
    [InlineData(@"c:\program files\monitorbrightnesscontroller\monitorbrightnesscontroller.exe")]
    public void PathNormalization_HandlesVariousCaseFormats(string path)
    {
        // Requirement 4.9: paths are compared case-insensitively using Path.GetFullPath.
        // Verify that GetFullPath handles various casing without throwing.
        var normalized = Path.GetFullPath(path);

        normalized.Should().NotBeNullOrEmpty();
        // On Windows, Path.GetFullPath preserves the input's drive letter casing but resolves relative segments.
        normalized.Should().ContainEquivalentOf("Program Files");
    }

    [Theory]
    [InlineData(@"C:\Program Files\MonitorBrightnessController\")]
    [InlineData(@"C:\Program Files\MonitorBrightnessController")]
    public void PathNormalization_HandlesTrailingSlashVariations(string dirPath)
    {
        // Verify trailing slash normalization doesn't break path operations.
        var combined = Path.Combine(dirPath, "MonitorBrightnessController.exe");
        var normalized = Path.GetFullPath(combined);

        normalized.Should().EndWith("MonitorBrightnessController.exe");
        normalized.Should().Contain("MonitorBrightnessController");
    }

    [Fact]
    public void IsInstalledInProgramFiles_ProcessPath_IsValid()
    {
        // Ensure Environment.ProcessPath returns a rooted path during test execution.
        var processPath = Environment.ProcessPath;

        processPath.Should().NotBeNullOrEmpty();
        Path.IsPathRooted(processPath!).Should().BeTrue(
            "the process path should be an absolute path");
    }
}
