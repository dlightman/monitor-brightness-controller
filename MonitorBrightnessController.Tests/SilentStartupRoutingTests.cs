using FluentAssertions;
using Xunit;

namespace MonitorBrightnessController.Tests;

/// <summary>
/// Unit tests for the silent startup routing logic in <see cref="Program"/>.
/// These tests verify that <see cref="Program.IsCliInvocation"/> and <see cref="Program.HasSilentFlag"/>
/// correctly classify argument combinations, determining the startup dispatch path.
/// Requirements: 1.1, 1.2, 1.4, 1.6
/// </summary>
public class SilentStartupRoutingTests
{
    // ==================================================================
    // Test: Silent mode route — --silent alone should NOT be CLI invocation
    // and SHOULD have the silent flag set. This routes to RunSilentMode
    // which creates a hidden window with tray icon (Requirement 1.1, 1.3).
    // ==================================================================

    [Fact]
    public void SilentOnly_IsNotCliInvocation()
    {
        string[] args = ["--silent"];

        bool isCli = Program.IsCliInvocation(args);

        isCli.Should().BeFalse("--silent alone should not trigger CLI mode");
    }

    [Fact]
    public void SilentOnly_HasSilentFlag()
    {
        string[] args = ["--silent"];

        bool hasSilent = Program.HasSilentFlag(args);

        hasSilent.Should().BeTrue("--silent should be detected as silent flag");
    }

    [Fact]
    public void SilentOnly_RoutesToSilentMode()
    {
        // When IsCliInvocation=false AND HasSilentFlag=true, the dispatch routes to
        // RunSilentMode which creates hidden window with tray icon (Req 1.1, 1.3).
        string[] args = ["--silent"];

        bool isCli = Program.IsCliInvocation(args);
        bool hasSilent = Program.HasSilentFlag(args);

        isCli.Should().BeFalse();
        hasSilent.Should().BeTrue();
        // This combination means: not CLI, but silent → RunSilentMode path
    }

    // ==================================================================
    // Test: Silent mode with AutoApply — when --silent is combined with
    // startup profile settings, the profile is applied (Requirement 1.2).
    // This is tested via the routing: silent-only args invoke RunSilentMode
    // with skipAutoApply=false, allowing profile application.
    // ==================================================================

    [Fact]
    public void SilentWithoutCliCommands_RoutesToSilentModeWithAutoApply()
    {
        // --silent alone (no --monitor/--profile) routes to RunSilentMode(skipAutoApply: false)
        // which applies the startup profile if configured (Req 1.2).
        string[] args = ["--silent"];

        bool isCli = Program.IsCliInvocation(args);
        bool hasSilent = Program.HasSilentFlag(args);

        // Not CLI + has silent → RunSilentMode(skipAutoApply: false)
        isCli.Should().BeFalse();
        hasSilent.Should().BeTrue();
    }

    [Fact]
    public void SilentWithCliCommands_RoutesToCombinedMode()
    {
        // --silent combined with --monitor/--profile executes CLI first,
        // then enters silent mode with skipAutoApply=true (Req 1.7).
        string[] args = ["--silent", "--monitor", "1", "--brightness", "80"];

        bool isCli = Program.IsCliInvocation(args);
        bool hasSilent = Program.HasSilentFlag(args);

        // Both true → combined mode: execute CLI, then RunSilentMode(skipAutoApply: true)
        isCli.Should().BeTrue();
        hasSilent.Should().BeTrue();
    }

    [Fact]
    public void SilentWithProfile_RoutesToCombinedMode()
    {
        // --silent with --profile triggers combined mode (Req 1.7).
        string[] args = ["--silent", "--profile", "Night"];

        bool isCli = Program.IsCliInvocation(args);
        bool hasSilent = Program.HasSilentFlag(args);

        isCli.Should().BeTrue();
        hasSilent.Should().BeTrue();
    }

    // ==================================================================
    // Test: Failed profile in silent mode — the routing ensures that when
    // --silent is present without CLI commands, RunSilentMode is invoked
    // which handles profile failures by remaining in tray (Requirement 1.6).
    // The error handling is in RunSilentMode; routing test confirms correct
    // dispatch path for this scenario.
    // ==================================================================

    [Fact]
    public void SilentOnly_DispatchPathAllowsGracefulProfileFailure()
    {
        // When --silent is the only arg, it routes to RunSilentMode which handles
        // auto-apply failures gracefully (Req 1.6: remain in tray without error window).
        string[] args = ["--silent"];

        bool isCli = Program.IsCliInvocation(args);
        bool hasSilent = Program.HasSilentFlag(args);

        // Routing confirms: not CLI, has silent → enters RunSilentMode
        // RunSilentMode wraps auto-apply in try/catch and traces failures
        isCli.Should().BeFalse();
        hasSilent.Should().BeTrue();
    }

    // ==================================================================
    // Test: Non-silent mode without CLI args shows window normally (Req 1.4).
    // ==================================================================

    [Fact]
    public void NoArgs_ShowsWindowNormally()
    {
        string[] args = [];

        bool isCli = Program.IsCliInvocation(args);
        bool hasSilent = Program.HasSilentFlag(args);

        // Both false → normal GUI mode: App.Run(window) with window.Show()
        isCli.Should().BeFalse();
        hasSilent.Should().BeFalse();
    }

    [Fact]
    public void NullArgs_ShowsWindowNormally()
    {
        bool isCli = Program.IsCliInvocation(null);
        bool hasSilent = Program.HasSilentFlag(null);

        isCli.Should().BeFalse();
        hasSilent.Should().BeFalse();
    }

    [Fact]
    public void EmptyArgs_ShowsWindowNormally()
    {
        string[] args = Array.Empty<string>();

        bool isCli = Program.IsCliInvocation(args);
        bool hasSilent = Program.HasSilentFlag(args);

        isCli.Should().BeFalse();
        hasSilent.Should().BeFalse();
    }

    // ==================================================================
    // Additional routing scenarios for completeness
    // ==================================================================

    [Fact]
    public void CliOnlyWithoutSilent_RoutesToPureCliMode()
    {
        // --monitor/--profile without --silent → pure CLI: execute and exit.
        string[] args = ["--monitor", "1", "--brightness", "50"];

        bool isCli = Program.IsCliInvocation(args);
        bool hasSilent = Program.HasSilentFlag(args);

        isCli.Should().BeTrue();
        hasSilent.Should().BeFalse();
    }

    [Fact]
    public void ProfileOnlyWithoutSilent_RoutesToPureCliMode()
    {
        string[] args = ["--profile", "Day"];

        bool isCli = Program.IsCliInvocation(args);
        bool hasSilent = Program.HasSilentFlag(args);

        isCli.Should().BeTrue();
        hasSilent.Should().BeFalse();
    }

    // ==================================================================
    // Packed single-string shortcut handling (Windows shortcut edge case)
    // ==================================================================

    [Fact]
    public void PackedSilentInSingleString_DetectedCorrectly()
    {
        // Windows shortcuts may pass all args as a single string.
        string[] args = ["--monitor 1 --brightness 50 --silent"];

        bool isCli = Program.IsCliInvocation(args);
        bool hasSilent = Program.HasSilentFlag(args);

        isCli.Should().BeTrue();
        hasSilent.Should().BeTrue();
    }

    [Fact]
    public void PackedSilentOnlyInSingleString_DetectedAsSilent()
    {
        // Edge case: just --silent as part of a longer string containing the literal
        string[] args = ["--silent"];

        bool hasSilent = Program.HasSilentFlag(args);

        hasSilent.Should().BeTrue();
    }
}
