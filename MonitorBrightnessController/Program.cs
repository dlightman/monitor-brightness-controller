using System;
using System.Linq;
using MonitorBrightnessController.Application;
using MonitorBrightnessController.Infrastructure;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Presentation;

namespace MonitorBrightnessController;

/// <summary>
/// Application entry point. Builds the dependency graph and dispatches to either the CLI
/// handler (when <c>--monitor</c> or <c>--profile</c> arguments are present) or the WPF GUI.
/// </summary>
internal static class Program
{
    private const string MonitorOption = "--monitor";
    private const string ProfileOption = "--profile";

    /// <summary>
    /// Process entry point. Detects CLI invocation and either runs the CLI handler (returning
    /// its exit code) or launches the WPF application.
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

        // CLI mode: any --monitor or --profile argument routes to the CLI handler and exits
        // without showing the GUI (Requirements 3.1, 4.1).
        if (IsCliInvocation(args))
        {
            var normalizedArgs = NormalizeArgs(args);
            ICliHandler cliHandler = new CliHandler(monitorService, profileManager);
            return cliHandler.Execute(normalizedArgs);
        }

        // GUI mode: start the WPF application with the main window. The settings store and
        // profile manager are passed through so the window can perform startup auto-apply
        // and persist the auto-apply toggle (Requirements 5.2, 5.3, 5.4, 5.6, 5.7).
        var app = new App();
        app.InitializeComponent();
        var window = new MainWindow(monitorService, settingsStore, profileManager);
        app.Run(window);
        return 0;
    }

    /// <summary>
    /// Determines whether the supplied arguments indicate a CLI invocation, i.e. they contain
    /// a <c>--monitor</c> or <c>--profile</c> option. Also handles the common Windows shortcut
    /// mistake where all arguments are wrapped in a single quoted string.
    /// </summary>
    private static bool IsCliInvocation(string[]? args)
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
