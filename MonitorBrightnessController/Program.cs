using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using MonitorBrightnessController.Application;
using MonitorBrightnessController.Infrastructure;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;
using MonitorBrightnessController.Presentation;

namespace MonitorBrightnessController;

/// <summary>
/// Application entry point. Builds the dependency graph and dispatches to either the CLI
/// handler (when <c>--monitor</c> or <c>--profile</c> arguments are present) or the WPF GUI.
/// Supports <c>--silent</c> mode which starts the application minimized to the system tray.
/// </summary>
internal static class Program
{
    private const string MonitorOption = "--monitor";
    private const string ProfileOption = "--profile";
    private const string SilentOption = "--silent";

    /// <summary>
    /// Process entry point. Detects CLI invocation and either runs the CLI handler (returning
    /// its exit code) or launches the WPF application. When <c>--silent</c> is specified
    /// without CLI commands, the GUI starts hidden with only the system tray icon visible.
    /// When <c>--silent</c> is combined with CLI commands, the commands execute first, then
    /// the application enters silent mode.
    /// </summary>
    /// <param name="args">The raw command-line arguments.</param>
    /// <returns>The process exit code (0 for GUI, CLI handler's code for CLI mode).</returns>
    [STAThread]
    public static int Main(string[] args)
    {
        // Wire the dependency graph via simple constructor injection. There is no DI container
        // dependency; the chain is small and the lifetimes are all process-scoped singletons.
        IMonitorInterop interop = new MonitorInterop();
        IMonitorService monitorService = new MonitorService(interop);
        ISettingsStore settingsStore = new SettingsStore();
        IProfileManager profileManager = new ProfileManager(settingsStore);

        // Determine if the invocation has CLI commands (--monitor/--profile) and/or --silent.
        bool hasCliCommands = IsCliInvocation(args);
        bool hasSilent = HasSilentFlag(args);

        // Pure CLI mode (--monitor/--profile WITHOUT --silent): execute and exit immediately.
        if (hasCliCommands && !hasSilent)
        {
            var normalizedArgs = NormalizeArgs(args);
            ICliHandler cliHandler = new CliHandler(monitorService, profileManager);
            return cliHandler.Execute(normalizedArgs);
        }

        // Combined mode (--monitor/--profile WITH --silent): execute CLI commands first,
        // then enter silent mode (Requirements 1.7).
        if (hasCliCommands && hasSilent)
        {
            var normalizedArgs = NormalizeArgs(args);
            ICliHandler cliHandler = new CliHandler(monitorService, profileManager);
            int cliResult = cliHandler.Execute(normalizedArgs);

            // Regardless of CLI result, enter silent mode with hidden window + tray.
            return RunSilentMode(monitorService, settingsStore, profileManager, skipAutoApply: true, CreateUpdateChecker());
        }

        // Silent mode (--silent only, no CLI commands): start hidden with tray icon
        // (Requirements 1.1, 1.2, 1.3, 1.5, 1.6).
        if (hasSilent)
        {
            return RunSilentMode(monitorService, settingsStore, profileManager, skipAutoApply: false, CreateUpdateChecker());
        }

        // GUI mode: start the WPF application with the main window. The settings store and
        // profile manager are passed through so the window can perform startup auto-apply
        // and persist the auto-apply toggle (Requirements 5.2, 5.3, 5.4, 5.6, 5.7).
        // Wire the update checker for auto-update notification on GUI startup.
        IUpdateChecker updateChecker = CreateUpdateChecker();

        var app = new App();
        app.InitializeComponent();
        var window = new MainWindow(monitorService, settingsStore, profileManager, updateChecker);
        app.Run(window);
        return 0;
    }

    /// <summary>
    /// Creates a fully wired <see cref="IUpdateChecker"/> instance with an HttpClient configured
    /// for a 10-second timeout and the current assembly version. Used in all startup paths so
    /// the update check is available when the GUI is shown (either immediately or restored from tray).
    /// </summary>
    private static IUpdateChecker CreateUpdateChecker()
    {
        var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        IGitHubReleaseClient releaseClient = new GitHubReleaseClient(httpClient);
        var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version
            ?? new Version(1, 4, 0);
        return new UpdateChecker(releaseClient, currentVersion);
    }

    /// <summary>
    /// Starts the application in silent mode: creates the WPF App and MainWindow but does not
    /// show the window. The window's Visibility is set to Collapsed and the system tray icon
    /// is initialized immediately. Optionally applies the startup profile before entering
    /// silent mode.
    /// </summary>
    /// <param name="monitorService">The monitor service for brightness operations.</param>
    /// <param name="settingsStore">The settings store for loading preferences.</param>
    /// <param name="profileManager">The profile manager for applying startup profiles.</param>
    /// <param name="skipAutoApply">
    /// When true, skips the startup profile auto-apply (used when CLI commands already executed).
    /// </param>
    /// <param name="updateChecker">
    /// Optional update checker passed through to MainWindow so the update check can run when
    /// the window is restored from the system tray.
    /// </param>
    /// <returns>The process exit code (always 0).</returns>
    private static int RunSilentMode(
        IMonitorService monitorService,
        ISettingsStore settingsStore,
        IProfileManager profileManager,
        bool skipAutoApply,
        IUpdateChecker? updateChecker = null)
    {
        // Optionally apply the startup profile (Requirements 1.2, 1.6).
        if (!skipAutoApply)
        {
            try
            {
                AppSettings settings = settingsStore.Load();

                if (settings.AutoApplyOnStartup)
                {
                    string? profileToApply = settings.DefaultStartupProfileName
                        ?? settings.LastAppliedProfileName;

                    if (!string.IsNullOrEmpty(profileToApply))
                    {
                        Result<Unit> applyResult = profileManager.ApplyProfile(profileToApply, monitorService);
                        if (!applyResult.IsSuccess)
                        {
                            // Requirement 1.6: remain in tray, log error for later viewing.
                            Trace.TraceWarning(
                                $"Silent mode: failed to apply startup profile '{profileToApply}': {applyResult.Error}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Requirement 1.6: handle any failure gracefully — remain in tray.
                Trace.TraceWarning($"Silent mode: startup profile auto-apply failed: {ex.Message}");
            }
        }

        // Create the WPF application and main window, but do not show the window.
        var app = new App();
        app.InitializeComponent();
        var window = new MainWindow(monitorService, settingsStore, profileManager, updateChecker);

        // Set window to Collapsed so it is not visible (Requirement 1.1).
        window.Visibility = Visibility.Collapsed;

        // Initialize the system tray immediately so the user sees the icon (Requirement 1.3).
        // The SystemTrayManager's double-click handler will restore the window (Requirement 1.5).
        var trayManager = new SystemTrayManager(window, saveState: null);

        app.Run(window);
        return 0;
    }

    /// <summary>
    /// Determines whether the supplied arguments indicate a CLI invocation, i.e. they contain
    /// a <c>--monitor</c> or <c>--profile</c> option. The <c>--silent</c> flag alone does NOT
    /// trigger CLI mode. Also handles the common Windows shortcut mistake where all arguments
    /// are wrapped in a single quoted string.
    /// </summary>
    internal static bool IsCliInvocation(string[]? args)
    {
        if (args is null || args.Length == 0)
        {
            return false;
        }

        // Check each arg as-is (the normal case: each token is a separate array element).
        if (args.Any(arg =>
            string.Equals(arg, MonitorOption, StringComparison.Ordinal) ||
            string.Equals(arg, ProfileOption, StringComparison.Ordinal)))
        {
            return true;
        }

        // Handle the common Windows shortcut mistake: all arguments packed into a single
        // quoted string, e.g. args = ["--monitor 1 --brightness 50 --monitor 2 ..."].
        // Check if any single arg contains the option keywords as substrings with word
        // boundaries (preceded by start-of-string or space).
        foreach (string arg in args)
        {
            if (arg.Contains(MonitorOption, StringComparison.Ordinal) ||
                arg.Contains(ProfileOption, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether the supplied arguments contain the <c>--silent</c> flag. Handles both
    /// the normal case (separate array elements) and the packed single-string shortcut case.
    /// </summary>
    internal static bool HasSilentFlag(string[]? args)
    {
        if (args is null || args.Length == 0)
        {
            return false;
        }

        // Check each arg as-is.
        if (args.Any(arg => string.Equals(arg, SilentOption, StringComparison.Ordinal)))
        {
            return true;
        }

        // Handle the packed single-string shortcut case.
        foreach (string arg in args)
        {
            if (arg.Contains(SilentOption, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Normalizes arguments for the common Windows shortcut case where all CLI args are
    /// packed into a single quoted string. Splits on whitespace to produce the expected
    /// token array.
    /// </summary>
    private static string[] NormalizeArgs(string[] args)
    {
        if (args.Length == 1 &&
            (args[0].Contains(MonitorOption, StringComparison.Ordinal) ||
             args[0].Contains(ProfileOption, StringComparison.Ordinal)))
        {
            // Single arg containing CLI keywords — likely a quoted shortcut target.
            // Split on whitespace to produce individual tokens.
            return args[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        }

        return args;
    }
}
