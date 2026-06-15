using System;
using System.Collections.Generic;
using System.IO;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;

namespace MonitorBrightnessController.Application;

/// <summary>
/// A single parsed <c>--monitor &lt;id&gt;</c> command with optional brightness and/or gamma
/// values, preserving the original (un-validated) strings exactly as supplied on the
/// command line and in the order they appeared.
/// </summary>
/// <param name="Identifier">The raw monitor identifier (index or name) as supplied.</param>
/// <param name="BrightnessRaw">The raw brightness value string as supplied, or null when not specified.</param>
/// <param name="GammaRaw">The raw gamma value string as supplied, or null when not specified.</param>
public sealed record MonitorCommand(string Identifier, string? BrightnessRaw, string? GammaRaw);

/// <summary>
/// The structured result of parsing CLI arguments. Carries the ordered list of
/// monitor-brightness commands, an optional profile name, and any parse error.
/// This type is produced by the pure <see cref="CliHandler.ParseArguments(string[])"/>
/// method so the parsing logic can be tested without hardware.
/// </summary>
public sealed record ParsedCliArguments
{
    /// <summary>The ordered monitor commands parsed from the arguments.</summary>
    public IReadOnlyList<MonitorCommand> MonitorCommands { get; init; }
        = new List<MonitorCommand>();

    /// <summary>The profile name supplied via <c>--profile</c>, or null when not present.</summary>
    public string? ProfileName { get; init; }

    /// <summary>True when the <c>--silent</c> flag was present in the arguments.</summary>
    public bool Silent { get; init; }

    /// <summary>A human-readable parse error message, or null when parsing succeeded.</summary>
    public string? ParseError { get; init; }

    /// <summary>
    /// True when the parse error is a usage error (unknown argument or no arguments) and the
    /// caller should additionally emit usage help.
    /// </summary>
    public bool ShowUsage { get; init; }

    /// <summary>True when parsing failed.</summary>
    public bool HasError => ParseError is not null;

    /// <summary>Creates a successful parse result.</summary>
    public static ParsedCliArguments Success(
        IReadOnlyList<MonitorCommand> commands, string? profileName, bool silent = false) => new()
    {
        MonitorCommands = commands,
        ProfileName = profileName,
        Silent = silent,
    };

    /// <summary>Creates a failed parse result carrying the given error.</summary>
    public static ParsedCliArguments WithError(string error, bool showUsage = false) => new()
    {
        ParseError = error,
        ShowUsage = showUsage,
    };
}

/// <summary>
/// Parses command-line arguments and executes the corresponding brightness or profile
/// operations. Direct brightness commands attempt every monitor pair even when some fail
/// (partial failure), reporting per-monitor errors to standard error and returning a
/// non-zero exit code if any operation fails.
/// </summary>
public sealed class CliHandler : ICliHandler
{
    private const string MonitorOption = "--monitor";
    private const string BrightnessOption = "--brightness";
    private const string GammaOption = "--gamma";
    private const string ProfileOption = "--profile";
    private const string SilentOption = "--silent";

    /// <summary>Usage help shown for unknown or missing arguments.</summary>
    public const string UsageText =
        "Usage:\n" +
        "  --monitor <id> --brightness <value>   Set brightness (0-100) for a monitor (repeatable)\n" +
        "  --monitor <id> --gamma <value>        Set gamma (0-100) for a monitor (repeatable)\n" +
        "  --monitor <id> --brightness <value> --gamma <value>  Set both brightness and gamma\n" +
        "\n" +
        "  Both --brightness and --gamma are optional within a --monitor group,\n" +
        "  but at least one must be specified.\n" +
        "\n" +
        "  --profile <name>                      Apply a named brightness and gamma profile\n" +
        "  --silent                              Start minimized to system tray (no window shown)";

    private readonly IMonitorService _monitorService;
    private readonly IProfileManager _profileManager;
    private readonly TextWriter _out;
    private readonly TextWriter _error;

    /// <summary>
    /// Creates a new <see cref="CliHandler"/>.
    /// </summary>
    /// <param name="monitorService">Service used to detect monitors and apply brightness.</param>
    /// <param name="profileManager">Manager used to apply named profiles.</param>
    /// <param name="standardOut">Writer for standard output; defaults to <see cref="Console.Out"/>.</param>
    /// <param name="standardError">Writer for standard error; defaults to <see cref="Console.Error"/>.</param>
    public CliHandler(
        IMonitorService monitorService,
        IProfileManager profileManager,
        TextWriter? standardOut = null,
        TextWriter? standardError = null)
    {
        _monitorService = monitorService ?? throw new ArgumentNullException(nameof(monitorService));
        _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
        _out = standardOut ?? Console.Out;
        _error = standardError ?? Console.Error;
    }

    /// <inheritdoc />
    public int Execute(string[] args)
    {
        ParsedCliArguments parsed = ParseArguments(args);

        if (parsed.HasError)
        {
            _error.WriteLine(parsed.ParseError);
            if (parsed.ShowUsage)
            {
                _error.WriteLine(UsageText);
            }

            return 1;
        }

        // The two supported invocation patterns are mutually exclusive.
        if (parsed.ProfileName is not null && parsed.MonitorCommands.Count > 0)
        {
            _error.WriteLine("Cannot combine --profile with --monitor arguments.");
            _error.WriteLine(UsageText);
            return 1;
        }

        if (parsed.ProfileName is not null)
        {
            return ExecuteProfile(parsed.ProfileName);
        }

        return ExecuteMonitorCommands(parsed.MonitorCommands);
    }

    // ---------------------------------------------------------------------
    // Pure parsing (no hardware dependency) — exercised directly by property tests.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Parses raw command-line arguments into a structured <see cref="ParsedCliArguments"/>.
    /// Recognizes repeatable <c>--monitor &lt;id&gt;</c> groups containing at least one of
    /// <c>--brightness &lt;value&gt;</c> or <c>--gamma &lt;value&gt;</c> (in any order), and a single
    /// <c>--profile &lt;name&gt;</c> argument, preserving group order and the original raw strings.
    /// This method performs no value range or monitor existence validation; it only
    /// validates argument structure.
    /// </summary>
    /// <param name="args">The raw command-line arguments.</param>
    /// <returns>The structured parse result, including any structural parse error.</returns>
    public static ParsedCliArguments ParseArguments(string[]? args)
    {
        if (args is null || args.Length == 0)
        {
            return ParsedCliArguments.WithError("No arguments specified.", showUsage: true);
        }

        var commands = new List<MonitorCommand>();
        string? profileName = null;
        bool silent = false;

        int i = 0;
        while (i < args.Length)
        {
            string arg = args[i];

            if (string.Equals(arg, SilentOption, StringComparison.Ordinal))
            {
                silent = true;
                i++;
            }
            else if (string.Equals(arg, MonitorOption, StringComparison.Ordinal))
            {
                // Expect: --monitor <id> followed by at least one of --brightness/--gamma
                if (i + 1 >= args.Length || IsOption(args[i + 1]))
                {
                    return ParsedCliArguments.WithError("Missing identifier for --monitor argument.");
                }

                string identifier = args[i + 1];
                i += 2;

                string? brightnessRaw = null;
                string? gammaRaw = null;

                // Consume --brightness and/or --gamma in any order until the next
                // --monitor, --profile, --silent, or end-of-args is reached.
                while (i < args.Length)
                {
                    if (string.Equals(args[i], BrightnessOption, StringComparison.Ordinal))
                    {
                        if (brightnessRaw is not null)
                        {
                            return ParsedCliArguments.WithError(
                                $"Duplicate --brightness for monitor {identifier}");
                        }

                        if (i + 1 >= args.Length || IsOption(args[i + 1]))
                        {
                            return ParsedCliArguments.WithError(
                                $"Missing --brightness value for monitor {identifier}");
                        }

                        brightnessRaw = args[i + 1];
                        i += 2;
                    }
                    else if (string.Equals(args[i], GammaOption, StringComparison.Ordinal))
                    {
                        if (gammaRaw is not null)
                        {
                            return ParsedCliArguments.WithError(
                                $"Duplicate --gamma for monitor {identifier}");
                        }

                        if (i + 1 >= args.Length || IsOption(args[i + 1]))
                        {
                            return ParsedCliArguments.WithError(
                                $"Missing --gamma value for monitor {identifier}");
                        }

                        gammaRaw = args[i + 1];
                        i += 2;
                    }
                    else
                    {
                        // Next token is not --brightness or --gamma — end of this monitor group.
                        break;
                    }
                }

                // At least one of --brightness or --gamma must be present.
                if (brightnessRaw is null && gammaRaw is null)
                {
                    return ParsedCliArguments.WithError(
                        $"--monitor {identifier} requires at least one of --brightness or --gamma.");
                }

                commands.Add(new MonitorCommand(identifier, brightnessRaw, gammaRaw));
            }
            else if (string.Equals(arg, ProfileOption, StringComparison.Ordinal))
            {
                if (i + 1 >= args.Length || IsOption(args[i + 1]))
                {
                    return ParsedCliArguments.WithError("Missing name for --profile argument.");
                }

                if (profileName is not null)
                {
                    return ParsedCliArguments.WithError("Only one --profile argument may be specified.");
                }

                profileName = args[i + 1];
                i += 2;
            }
            else
            {
                return ParsedCliArguments.WithError($"Unknown argument '{arg}'.", showUsage: true);
            }
        }

        // --silent alone is a valid invocation (GUI silent mode).
        if (commands.Count == 0 && profileName is null && !silent)
        {
            return ParsedCliArguments.WithError("No arguments specified.", showUsage: true);
        }

        return ParsedCliArguments.Success(commands, profileName, silent);
    }

    private static bool IsOption(string value) => value.StartsWith("--", StringComparison.Ordinal);

    // ---------------------------------------------------------------------
    // Execution
    // ---------------------------------------------------------------------

    private int ExecuteMonitorCommands(IReadOnlyList<MonitorCommand> commands)
    {
        // Detect monitors once so identifier resolution and partial failure handling operate
        // against a single consistent snapshot (Req 3.7).
        IReadOnlyList<MonitorState> monitors = _monitorService.DetectMonitors();
        if (monitors.Count == 0)
        {
            _error.WriteLine("No controllable monitors detected.");
            return 1;
        }

        bool anyFailed = false;

        // Attempt every command even if earlier ones fail (partial failure semantics).
        foreach (MonitorCommand command in commands)
        {
            MonitorState? target = _monitorService.FindMonitor(command.Identifier);
            if (target is null)
            {
                _error.WriteLine($"Monitor '{command.Identifier}' not found");
                anyFailed = true;
                continue;
            }

            // Process brightness if specified.
            if (command.BrightnessRaw is not null)
            {
                if (!MonitorService.TryParseBrightness(command.BrightnessRaw, out int brightness))
                {
                    _error.WriteLine($"Invalid brightness value '{command.BrightnessRaw}': must be integer 0-100");
                    anyFailed = true;
                }
                else
                {
                    Result<Unit> result = _monitorService.SetBrightness(target.MonitorIndex, brightness);
                    if (!result.IsSuccess)
                    {
                        _error.WriteLine($"Failed to set brightness on monitor '{command.Identifier}': {result.Error}");
                        anyFailed = true;
                    }
                }
            }

            // Process gamma if specified.
            if (command.GammaRaw is not null)
            {
                if (!int.TryParse(command.GammaRaw, out int gamma) || gamma < 0 || gamma > 100)
                {
                    _error.WriteLine($"Invalid gamma value '{command.GammaRaw}': must be integer 0-100");
                    anyFailed = true;
                }
                else
                {
                    Result<Unit> result = _monitorService.SetGamma(target.MonitorIndex, gamma);
                    if (!result.IsSuccess)
                    {
                        _error.WriteLine($"Failed to set gamma on monitor '{command.Identifier}': {result.Error}");
                        anyFailed = true;
                    }
                }
            }
        }

        return anyFailed ? 1 : 0;
    }

    private int ExecuteProfile(string profileName)
    {
        Result<Unit> result = _profileManager.ApplyProfile(profileName, _monitorService);
        if (!result.IsSuccess)
        {
            _error.WriteLine(result.Error);
            return 1;
        }

        return 0;
    }
}
