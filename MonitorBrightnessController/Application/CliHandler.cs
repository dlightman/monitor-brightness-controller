using System;
using System.Collections.Generic;
using System.IO;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;

namespace MonitorBrightnessController.Application;

/// <summary>
/// A single parsed <c>--monitor &lt;id&gt; --brightness &lt;value&gt;</c> pair, preserving the
/// original (un-validated) identifier and brightness strings exactly as supplied on the
/// command line and in the order they appeared.
/// </summary>
/// <param name="Identifier">The raw monitor identifier (index or name) as supplied.</param>
/// <param name="BrightnessRaw">The raw brightness value string as supplied.</param>
public sealed record MonitorBrightnessCommand(string Identifier, string BrightnessRaw);

/// <summary>
/// The structured result of parsing CLI arguments. Carries the ordered list of
/// monitor-brightness commands, an optional profile name, and any parse error.
/// This type is produced by the pure <see cref="CliHandler.ParseArguments(string[])"/>
/// method so the parsing logic can be tested without hardware.
/// </summary>
public sealed record ParsedCliArguments
{
    /// <summary>The ordered monitor-brightness commands parsed from the arguments.</summary>
    public IReadOnlyList<MonitorBrightnessCommand> MonitorCommands { get; init; }
        = new List<MonitorBrightnessCommand>();

    /// <summary>The profile name supplied via <c>--profile</c>, or null when not present.</summary>
    public string? ProfileName { get; init; }

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
        IReadOnlyList<MonitorBrightnessCommand> commands, string? profileName) => new()
    {
        MonitorCommands = commands,
        ProfileName = profileName,
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
    private const string ProfileOption = "--profile";

    /// <summary>Usage help shown for unknown or missing arguments.</summary>
    public const string UsageText =
        "Usage:\n" +
        "  --monitor <id> --brightness <value>   Set brightness (0-100) for a monitor (repeatable)\n" +
        "  --profile <name>                      Apply a named brightness profile";

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
            _error.WriteLine("Cannot combine --profile with --monitor/--brightness arguments.");
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
    /// Recognizes repeatable <c>--monitor &lt;id&gt; --brightness &lt;value&gt;</c> pairs and a single
    /// <c>--profile &lt;name&gt;</c> argument, preserving pair order and the original raw strings.
    /// This method performs no brightness range or monitor existence validation; it only
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

        var commands = new List<MonitorBrightnessCommand>();
        string? profileName = null;

        int i = 0;
        while (i < args.Length)
        {
            string arg = args[i];

            if (string.Equals(arg, MonitorOption, StringComparison.Ordinal))
            {
                // Expect: --monitor <id> --brightness <value>
                if (i + 1 >= args.Length || IsOption(args[i + 1]))
                {
                    return ParsedCliArguments.WithError("Missing identifier for --monitor argument.");
                }

                string identifier = args[i + 1];

                if (i + 2 >= args.Length || !string.Equals(args[i + 2], BrightnessOption, StringComparison.Ordinal))
                {
                    return ParsedCliArguments.WithError($"Missing --brightness value for monitor {identifier}");
                }

                if (i + 3 >= args.Length || IsOption(args[i + 3]))
                {
                    return ParsedCliArguments.WithError($"Missing --brightness value for monitor {identifier}");
                }

                commands.Add(new MonitorBrightnessCommand(identifier, args[i + 3]));
                i += 4;
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

        if (commands.Count == 0 && profileName is null)
        {
            return ParsedCliArguments.WithError("No arguments specified.", showUsage: true);
        }

        return ParsedCliArguments.Success(commands, profileName);
    }

    private static bool IsOption(string value) => value.StartsWith("--", StringComparison.Ordinal);

    // ---------------------------------------------------------------------
    // Execution
    // ---------------------------------------------------------------------

    private int ExecuteMonitorCommands(IReadOnlyList<MonitorBrightnessCommand> commands)
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

        // Attempt every command even if earlier ones fail (Req 3.7).
        foreach (MonitorBrightnessCommand command in commands)
        {
            if (!MonitorService.TryParseBrightness(command.BrightnessRaw, out int brightness))
            {
                _error.WriteLine($"Invalid brightness value '{command.BrightnessRaw}': must be integer 0-100");
                anyFailed = true;
                continue;
            }

            MonitorState? target = _monitorService.FindMonitor(command.Identifier);
            if (target is null)
            {
                _error.WriteLine($"Monitor '{command.Identifier}' not found");
                anyFailed = true;
                continue;
            }

            Result<Unit> result = _monitorService.SetBrightness(target.MonitorIndex, brightness);
            if (!result.IsSuccess)
            {
                _error.WriteLine($"Failed to set brightness on monitor '{command.Identifier}': {result.Error}");
                anyFailed = true;
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
