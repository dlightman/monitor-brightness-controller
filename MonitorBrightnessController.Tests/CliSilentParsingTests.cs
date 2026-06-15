using FluentAssertions;
using MonitorBrightnessController.Application;
using Xunit;

namespace MonitorBrightnessController.Tests;

/// <summary>
/// Unit tests for the <c>--silent</c> CLI flag parsing behavior.
/// Requirements: 1.1, 1.4, 1.7
/// </summary>
public class CliSilentParsingTests
{
    // ------------------------------------------------------------------
    // --silent alone → valid parse, Silent = true, no commands, no profile
    // ------------------------------------------------------------------

    [Fact]
    public void SilentAlone_ParsesSuccessfully()
    {
        string[] args = ["--silent"];

        ParsedCliArguments result = CliHandler.ParseArguments(args);

        result.HasError.Should().BeFalse();
        result.Silent.Should().BeTrue();
        result.MonitorCommands.Should().BeEmpty();
        result.ProfileName.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // --silent combined with --profile → both parsed correctly
    // ------------------------------------------------------------------

    [Fact]
    public void SilentWithProfile_ParsesBothCorrectly()
    {
        string[] args = ["--silent", "--profile", "Night"];

        ParsedCliArguments result = CliHandler.ParseArguments(args);

        result.HasError.Should().BeFalse();
        result.Silent.Should().BeTrue();
        result.ProfileName.Should().Be("Night");
        result.MonitorCommands.Should().BeEmpty();
    }

    [Fact]
    public void ProfileThenSilent_ParsesBothCorrectly()
    {
        string[] args = ["--profile", "Night", "--silent"];

        ParsedCliArguments result = CliHandler.ParseArguments(args);

        result.HasError.Should().BeFalse();
        result.Silent.Should().BeTrue();
        result.ProfileName.Should().Be("Night");
        result.MonitorCommands.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // --silent combined with --monitor commands → both parsed correctly
    // ------------------------------------------------------------------

    [Fact]
    public void SilentWithMonitorCommand_ParsesBothCorrectly()
    {
        string[] args = ["--silent", "--monitor", "1", "--brightness", "80"];

        ParsedCliArguments result = CliHandler.ParseArguments(args);

        result.HasError.Should().BeFalse();
        result.Silent.Should().BeTrue();
        result.MonitorCommands.Should().HaveCount(1);
        result.MonitorCommands[0].Identifier.Should().Be("1");
        result.MonitorCommands[0].BrightnessRaw.Should().Be("80");
    }

    [Fact]
    public void SilentWithMultipleMonitorCommands_ParsesAllCorrectly()
    {
        string[] args =
        [
            "--silent",
            "--monitor", "1", "--brightness", "50", "--gamma", "70",
            "--monitor", "2", "--brightness", "90"
        ];

        ParsedCliArguments result = CliHandler.ParseArguments(args);

        result.HasError.Should().BeFalse();
        result.Silent.Should().BeTrue();
        result.MonitorCommands.Should().HaveCount(2);
        result.MonitorCommands[0].Identifier.Should().Be("1");
        result.MonitorCommands[0].BrightnessRaw.Should().Be("50");
        result.MonitorCommands[0].GammaRaw.Should().Be("70");
        result.MonitorCommands[1].Identifier.Should().Be("2");
        result.MonitorCommands[1].BrightnessRaw.Should().Be("90");
    }

    // ------------------------------------------------------------------
    // --silent at different positions (first, middle, last)
    // ------------------------------------------------------------------

    [Fact]
    public void SilentAtFirstPosition_ParsedCorrectly()
    {
        string[] args = ["--silent", "--monitor", "1", "--brightness", "60"];

        ParsedCliArguments result = CliHandler.ParseArguments(args);

        result.HasError.Should().BeFalse();
        result.Silent.Should().BeTrue();
        result.MonitorCommands.Should().HaveCount(1);
        result.MonitorCommands[0].Identifier.Should().Be("1");
        result.MonitorCommands[0].BrightnessRaw.Should().Be("60");
    }

    [Fact]
    public void SilentAtMiddlePosition_ParsedCorrectly()
    {
        string[] args =
        [
            "--monitor", "1", "--brightness", "60",
            "--silent",
            "--monitor", "2", "--gamma", "40"
        ];

        ParsedCliArguments result = CliHandler.ParseArguments(args);

        result.HasError.Should().BeFalse();
        result.Silent.Should().BeTrue();
        result.MonitorCommands.Should().HaveCount(2);
        result.MonitorCommands[0].Identifier.Should().Be("1");
        result.MonitorCommands[0].BrightnessRaw.Should().Be("60");
        result.MonitorCommands[1].Identifier.Should().Be("2");
        result.MonitorCommands[1].GammaRaw.Should().Be("40");
    }

    [Fact]
    public void SilentAtLastPosition_ParsedCorrectly()
    {
        string[] args = ["--monitor", "1", "--brightness", "60", "--silent"];

        ParsedCliArguments result = CliHandler.ParseArguments(args);

        result.HasError.Should().BeFalse();
        result.Silent.Should().BeTrue();
        result.MonitorCommands.Should().HaveCount(1);
        result.MonitorCommands[0].Identifier.Should().Be("1");
        result.MonitorCommands[0].BrightnessRaw.Should().Be("60");
    }

    [Fact]
    public void SilentBetweenProfileArgs_ParsedCorrectly()
    {
        // --silent positioned between --profile and its value would be consumed as
        // a flag, so place it after --profile <name> instead.
        string[] args = ["--profile", "Day", "--silent"];

        ParsedCliArguments result = CliHandler.ParseArguments(args);

        result.HasError.Should().BeFalse();
        result.Silent.Should().BeTrue();
        result.ProfileName.Should().Be("Day");
    }

    // ------------------------------------------------------------------
    // --silent with unknown args → error
    // ------------------------------------------------------------------

    [Fact]
    public void SilentWithUnknownArg_ReturnsError()
    {
        string[] args = ["--silent", "--unknown"];

        ParsedCliArguments result = CliHandler.ParseArguments(args);

        result.HasError.Should().BeTrue();
        result.ParseError.Should().Contain("Unknown argument");
        result.ShowUsage.Should().BeTrue();
    }

    [Fact]
    public void UnknownArgBeforeSilent_ReturnsError()
    {
        string[] args = ["--bogus", "--silent"];

        ParsedCliArguments result = CliHandler.ParseArguments(args);

        result.HasError.Should().BeTrue();
        result.ParseError.Should().Contain("Unknown argument");
        result.ShowUsage.Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // Additional edge case: without --silent, window shows normally (Req 1.4)
    // ------------------------------------------------------------------

    [Fact]
    public void WithoutSilent_SilentIsFalse()
    {
        string[] args = ["--monitor", "1", "--brightness", "50"];

        ParsedCliArguments result = CliHandler.ParseArguments(args);

        result.HasError.Should().BeFalse();
        result.Silent.Should().BeFalse();
    }
}
