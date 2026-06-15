using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using MonitorBrightnessController.Application;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;
using MbcUnit = MonitorBrightnessController.Models.Unit;
using NSubstitute;

namespace MonitorBrightnessController.Tests.Properties;

// Feature: gamma-control, Property 5: CLI parsing extracts gamma regardless of argument order
// Feature: gamma-control, Property 6: CLI single-setting commands invoke only that setting
// Feature: gamma-control, Property 7: CLI partial failure processes all commands
// Feature: gamma-control, Property 18: --monitor without any setting is a parse error

/// <summary>
/// Custom arbitraries for CLI parsing property tests.
/// </summary>
public static class CliParsingArbitraries
{
    /// <summary>
    /// Generates valid monitor identifiers: non-empty alphanumeric strings
    /// that do not start with "--" (to avoid being confused with options).
    /// </summary>
    public static Arbitrary<string> MonitorIdentifier()
    {
        var gen = Gen.Sized(size =>
        {
            var length = Gen.Choose(1, Math.Max(1, Math.Min(size, 10)));
            return length.SelectMany(len =>
                Gen.ArrayOf(len, Gen.Elements(
                    "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray()))
                .Select(chars => new string(chars)));
        });

        return Arb.From(gen);
    }

    /// <summary>
    /// Generates valid brightness/gamma value strings (integers 0-100).
    /// </summary>
    public static Arbitrary<int> ValidSettingValue()
    {
        return Arb.From(Gen.Choose(0, 100));
    }
}

/// <summary>
/// Property-based tests for CLI gamma parsing and execution.
/// Tests argument order independence, single-setting isolation, partial failure semantics,
/// and bare --monitor error detection.
/// </summary>
public class CliParsingProperties
{
    // -------------------------------------------------------------------------
    // Property 5: CLI parsing extracts gamma regardless of argument order
    // -------------------------------------------------------------------------

    /// <summary>
    /// Property 5: For any valid --monitor command containing both --brightness and --gamma
    /// in either order, parsing produces the same MonitorCommand with the correct identifier,
    /// brightness value, and gamma value.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 5.1, 5.2**
    /// </remarks>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(CliParsingArbitraries) })]
    public void CliParsing_ExtractsGamma_RegardlessOfArgumentOrder(string monitorId, int brightness, int gamma)
    {
        // Constrain to valid ranges
        if (string.IsNullOrWhiteSpace(monitorId) || monitorId.StartsWith("--"))
            return;
        if (brightness < 0 || brightness > 100)
            return;
        if (gamma < 0 || gamma > 100)
            return;

        string brightnessStr = brightness.ToString();
        string gammaStr = gamma.ToString();

        // Order 1: --monitor <id> --brightness <val> --gamma <val>
        var argsBrightnessFirst = new[]
        {
            "--monitor", monitorId,
            "--brightness", brightnessStr,
            "--gamma", gammaStr
        };

        // Order 2: --monitor <id> --gamma <val> --brightness <val>
        var argsGammaFirst = new[]
        {
            "--monitor", monitorId,
            "--gamma", gammaStr,
            "--brightness", brightnessStr
        };

        ParsedCliArguments result1 = CliHandler.ParseArguments(argsBrightnessFirst);
        ParsedCliArguments result2 = CliHandler.ParseArguments(argsGammaFirst);

        // Both should succeed
        result1.HasError.Should().BeFalse(
            "parsing --monitor {0} --brightness {1} --gamma {2} should succeed", monitorId, brightnessStr, gammaStr);
        result2.HasError.Should().BeFalse(
            "parsing --monitor {0} --gamma {1} --brightness {2} should succeed", monitorId, gammaStr, brightnessStr);

        // Both should produce the same single command
        result1.MonitorCommands.Should().HaveCount(1);
        result2.MonitorCommands.Should().HaveCount(1);

        MonitorCommand cmd1 = result1.MonitorCommands[0];
        MonitorCommand cmd2 = result2.MonitorCommands[0];

        // Verify the parsed values are the same regardless of order
        cmd1.Identifier.Should().Be(monitorId);
        cmd2.Identifier.Should().Be(monitorId);
        cmd1.BrightnessRaw.Should().Be(brightnessStr);
        cmd2.BrightnessRaw.Should().Be(brightnessStr);
        cmd1.GammaRaw.Should().Be(gammaStr);
        cmd2.GammaRaw.Should().Be(gammaStr);
    }

    // -------------------------------------------------------------------------
    // Property 6: CLI single-setting commands invoke only that setting
    // -------------------------------------------------------------------------

    /// <summary>
    /// Property 6 (gamma-only): For any monitor command specifying only --gamma,
    /// execution calls SetGamma but NOT SetBrightness on that monitor.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 5.4, 5.5**
    /// </remarks>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(CliParsingArbitraries) })]
    public void CliExecution_GammaOnly_CallsSetGammaButNotSetBrightness(int gammaValue)
    {
        if (gammaValue < 0 || gammaValue > 100)
            return;

        var monitorService = Substitute.For<IMonitorService>();
        var profileManager = Substitute.For<IProfileManager>();

        var monitor = new MonitorState
        {
            MonitorIndex = 1,
            MonitorName = "TestMonitor",
            DevicePath = "\\\\?\\DISPLAY#TEST#1",
            PhysicalHandle = new IntPtr(42),
            IsControllable = true,
            CurrentBrightness = 50,
            CurrentGamma = 50
        };

        monitorService.DetectMonitors().Returns(new List<MonitorState> { monitor });
        monitorService.FindMonitor("1").Returns(monitor);
        monitorService.SetGamma(1, gammaValue).Returns(Result<MbcUnit>.Success(MbcUnit.Value));

        var stderr = new StringWriter();
        var handler = new CliHandler(monitorService, profileManager, TextWriter.Null, stderr);

        var args = new[] { "--monitor", "1", "--gamma", gammaValue.ToString() };
        int exitCode = handler.Execute(args);

        exitCode.Should().Be(0, "a valid gamma-only command should succeed");
        monitorService.Received(1).SetGamma(1, gammaValue);
        monitorService.DidNotReceive().SetBrightness(Arg.Any<int>(), Arg.Any<int>());
    }

    /// <summary>
    /// Property 6 (brightness-only): For any monitor command specifying only --brightness,
    /// execution calls SetBrightness but NOT SetGamma on that monitor.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 5.4, 5.5**
    /// </remarks>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(CliParsingArbitraries) })]
    public void CliExecution_BrightnessOnly_CallsSetBrightnessButNotSetGamma(int brightnessValue)
    {
        if (brightnessValue < 0 || brightnessValue > 100)
            return;

        var monitorService = Substitute.For<IMonitorService>();
        var profileManager = Substitute.For<IProfileManager>();

        var monitor = new MonitorState
        {
            MonitorIndex = 1,
            MonitorName = "TestMonitor",
            DevicePath = "\\\\?\\DISPLAY#TEST#1",
            PhysicalHandle = new IntPtr(42),
            IsControllable = true,
            CurrentBrightness = 50,
            CurrentGamma = 50
        };

        monitorService.DetectMonitors().Returns(new List<MonitorState> { monitor });
        monitorService.FindMonitor("1").Returns(monitor);
        monitorService.SetBrightness(1, brightnessValue).Returns(Result<MbcUnit>.Success(MbcUnit.Value));

        var stderr = new StringWriter();
        var handler = new CliHandler(monitorService, profileManager, TextWriter.Null, stderr);

        var args = new[] { "--monitor", "1", "--brightness", brightnessValue.ToString() };
        int exitCode = handler.Execute(args);

        exitCode.Should().Be(0, "a valid brightness-only command should succeed");
        monitorService.Received(1).SetBrightness(1, brightnessValue);
        monitorService.DidNotReceive().SetGamma(Arg.Any<int>(), Arg.Any<int>());
    }

    // -------------------------------------------------------------------------
    // Property 7: CLI partial failure processes all commands
    // -------------------------------------------------------------------------

    /// <summary>
    /// Property 7: For any sequence of monitor commands where some monitors exist and
    /// some don't, the CLI attempts every command in the sequence and returns exit code 1.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 5.3, 5.7**
    /// </remarks>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(CliParsingArbitraries) })]
    public void CliExecution_PartialFailure_ProcessesAllCommands(int gammaValue)
    {
        if (gammaValue < 0 || gammaValue > 100)
            return;

        var monitorService = Substitute.For<IMonitorService>();
        var profileManager = Substitute.For<IProfileManager>();

        // Set up two monitors: index 1 exists, index 2 exists
        var monitor1 = new MonitorState
        {
            MonitorIndex = 1,
            MonitorName = "Monitor1",
            DevicePath = "\\\\?\\DISPLAY#TEST#1",
            PhysicalHandle = new IntPtr(1),
            IsControllable = true,
            CurrentBrightness = 50,
            CurrentGamma = 50
        };
        var monitor2 = new MonitorState
        {
            MonitorIndex = 2,
            MonitorName = "Monitor2",
            DevicePath = "\\\\?\\DISPLAY#TEST#2",
            PhysicalHandle = new IntPtr(2),
            IsControllable = true,
            CurrentBrightness = 50,
            CurrentGamma = 50
        };

        monitorService.DetectMonitors().Returns(new List<MonitorState> { monitor1, monitor2 });

        // "1" resolves, "nonexistent" does NOT resolve, "2" resolves
        monitorService.FindMonitor("1").Returns(monitor1);
        monitorService.FindMonitor("nonexistent").Returns((MonitorState?)null);
        monitorService.FindMonitor("2").Returns(monitor2);

        monitorService.SetGamma(1, gammaValue).Returns(Result<MbcUnit>.Success(MbcUnit.Value));
        monitorService.SetGamma(2, gammaValue).Returns(Result<MbcUnit>.Success(MbcUnit.Value));

        var stderr = new StringWriter();
        var handler = new CliHandler(monitorService, profileManager, TextWriter.Null, stderr);

        // Three commands: monitor 1 (succeeds), nonexistent (fails), monitor 2 (succeeds)
        var args = new[]
        {
            "--monitor", "1", "--gamma", gammaValue.ToString(),
            "--monitor", "nonexistent", "--gamma", gammaValue.ToString(),
            "--monitor", "2", "--gamma", gammaValue.ToString()
        };

        int exitCode = handler.Execute(args);

        // Exit code should be 1 because one command failed
        exitCode.Should().Be(1, "at least one command failed so exit code should be 1");

        // All commands should have been attempted (monitor 1 and 2 should have SetGamma called)
        monitorService.Received(1).SetGamma(1, gammaValue);
        monitorService.Received(1).SetGamma(2, gammaValue);

        // The "nonexistent" monitor should have been looked up
        monitorService.Received(1).FindMonitor("nonexistent");

        // Error output should contain mention of the failed monitor
        stderr.ToString().Should().Contain("nonexistent",
            "the error output should identify the monitor that could not be found");
    }

    // -------------------------------------------------------------------------
    // Property 18: --monitor without any setting is a parse error
    // -------------------------------------------------------------------------

    /// <summary>
    /// Property 18: For any CLI argument sequence containing --monitor <id> not followed by
    /// at least one of --brightness or --gamma, parsing returns an error result.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 5.6**
    /// </remarks>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(CliParsingArbitraries) })]
    public void CliParsing_MonitorWithoutSetting_IsParseError(string monitorId)
    {
        // Constrain: non-empty identifier that doesn't look like an option
        if (string.IsNullOrWhiteSpace(monitorId) || monitorId.StartsWith("--"))
            return;

        // Bare --monitor <id> with nothing following
        var args = new[] { "--monitor", monitorId };

        ParsedCliArguments result = CliHandler.ParseArguments(args);

        result.HasError.Should().BeTrue(
            "--monitor {0} without --brightness or --gamma should be a parse error", monitorId);
        result.ParseError.Should().NotBeNullOrWhiteSpace(
            "the parse error should contain a descriptive message");
    }
}
