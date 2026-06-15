using System.IO;
using System.Linq;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using MonitorBrightnessController.Application;

namespace MonitorBrightnessController.Tests.Properties;

// Feature: ui-consolidation, Property 14: Shortcut arguments correctly formed

/// <summary>
/// Property-based tests verifying that shortcut parameters (arguments, target, working directory)
/// are correctly formed for any valid profile name and executable path.
/// </summary>
public class ShortcutProperties
{
    /// <summary>
    /// Generator for valid profile names: 1–64 characters from [a-zA-Z0-9_-].
    /// </summary>
    private static Gen<string> ValidProfileNameGen()
    {
        var allowedChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-".ToCharArray();

        return Gen.Choose(1, 64)
            .SelectMany(len =>
                Gen.ArrayOf(len, Gen.Elements(allowedChars))
                    .Select(chars => new string(chars)));
    }

    /// <summary>
    /// Generator for plausible executable paths (e.g., C:\SomeFolder\App.exe).
    /// Uses a realistic folder structure with a .exe filename.
    /// </summary>
    private static Gen<string> ExecutablePathGen()
    {
        var folderChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray();

        var folderGen = Gen.Choose(1, 20)
            .SelectMany(len =>
                Gen.ArrayOf(len, Gen.Elements(folderChars))
                    .Select(chars => new string(chars)));

        return Gen.Choose(1, 4)
            .SelectMany(depth => Gen.ListOf(depth, folderGen))
            .Select(folders => @"C:\" + string.Join(@"\", folders) + @"\MonitorBrightnessController.exe");
    }

    /// <summary>
    /// Property 14: Shortcut arguments correctly formed
    ///
    /// For any valid profile name, the created shortcut SHALL have its arguments set to
    /// `--profile {name}`, its target set to the application executable path, and its
    /// working directory set to the executable's parent folder.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 5.4**
    /// </remarks>
    [Property(MaxTest = 100)]
    public Property ShortcutArguments_AreCorrectlyFormed_ForAnyValidProfileName()
    {
        var arb = Arb.From(
            ValidProfileNameGen().SelectMany(name =>
                ExecutablePathGen().Select(exePath => (name, exePath))));

        return Prop.ForAll(arb, pair =>
        {
            var (profileName, exePath) = pair;

            // Act
            string arguments = ShortcutHelper.BuildArguments(profileName);
            string target = ShortcutHelper.GetTargetPath(exePath);
            string workingDir = ShortcutHelper.GetWorkingDirectory(exePath);

            // Assert: arguments must be "--profile {name}"
            arguments.Should().Be($"--profile {profileName}",
                "shortcut arguments must be formatted as --profile followed by the profile name");

            // Assert: target must be the executable path
            target.Should().Be(exePath,
                "shortcut target must be the application executable path");

            // Assert: working directory must be the executable's parent folder
            string expectedWorkingDir = Path.GetDirectoryName(exePath) ?? "";
            workingDir.Should().Be(expectedWorkingDir,
                "shortcut working directory must be the executable's parent folder");
        });
    }
}
