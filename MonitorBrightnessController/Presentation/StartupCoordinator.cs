using System;
using System.Collections.Generic;
using System.Linq;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;

namespace MonitorBrightnessController.Presentation;

/// <summary>
/// The startup action chosen by <see cref="StartupCoordinator"/> based on the loaded
/// settings and the set of currently stored profiles.
/// </summary>
public enum StartupAction
{
    /// <summary>Auto-apply is disabled: read current brightness values without changing them (Requirement 5.4).</summary>
    AutoApplyDisabled,

    /// <summary>Auto-apply is enabled and the last-applied profile still exists: apply it (Requirement 5.3).</summary>
    ApplyLastProfile,

    /// <summary>Auto-apply is enabled but the last-applied profile is missing: skip and notify (Requirement 5.6).</summary>
    LastProfileMissing,

    /// <summary>CLI arguments override startup profile: skip all auto-apply (Requirement 2.5).</summary>
    CliOverride,

    /// <summary>Apply the configured default startup profile (Requirement 2.4).</summary>
    ApplyDefaultProfile,

    /// <summary>The configured default startup profile does not exist: skip and notify (Requirement 2.6).</summary>
    DefaultProfileMissing,
}

/// <summary>
/// The outcome of the pure startup decision: which action to take, the profile name it
/// concerns (if any), and a user-facing notice to display (if any).
/// </summary>
/// <param name="Action">The action the GUI should perform at startup.</param>
/// <param name="ProfileName">The profile name involved in the action, or null.</param>
/// <param name="Notice">A user-facing notice to display, or null when there is nothing to report.</param>
public readonly record struct StartupDecision(StartupAction Action, string? ProfileName, string? Notice);

/// <summary>
/// Coordinates GUI startup behavior for auto-apply (Requirements 5.2, 5.3, 5.4, 5.6, 5.7)
/// and startup registration reconciliation (Requirements 1.4, 2.4, 2.5, 2.6, 2.7, 2.8).
/// </summary>
/// <remarks>
/// The decision of what to do at startup is a pure function of the loaded
/// <see cref="AppSettings"/> and the set of stored profile names; see <see cref="Decide"/>.
/// <see cref="Run"/> performs that decision against the injected services, applying the
/// last-used profile when appropriate and returning any notice to surface in the GUI.
/// Loading settings via <see cref="ISettingsStore.Load"/> already yields defaults when the
/// file is missing or unreadable, satisfying Requirement 5.7.
/// </remarks>
public sealed class StartupCoordinator
{
    private readonly ISettingsStore _settingsStore;
    private readonly IProfileManager _profileManager;
    private readonly IMonitorService _monitorService;
    private readonly IStartupRegistration? _startupRegistration;

    /// <summary>
    /// Creates a startup coordinator over the supplied services.
    /// </summary>
    /// <param name="settingsStore">The settings store used to load preferences and last-used state.</param>
    /// <param name="profileManager">The profile manager used to resolve and apply profiles.</param>
    /// <param name="monitorService">The monitor service used to apply brightness during auto-apply.</param>
    /// <param name="startupRegistration">
    /// Optional startup registration service. When provided, <see cref="Run"/> calls
    /// <see cref="IStartupRegistration.EnsureRegistration"/> to reconcile the registry entry
    /// (Requirements 1.4, 5.1, 5.2).
    /// </param>
    public StartupCoordinator(
        ISettingsStore settingsStore,
        IProfileManager profileManager,
        IMonitorService monitorService,
        IStartupRegistration? startupRegistration = null)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
        _monitorService = monitorService ?? throw new ArgumentNullException(nameof(monitorService));
        _startupRegistration = startupRegistration;
    }

    /// <summary>
    /// Determines, without side effects, what the GUI should do at startup.
    /// </summary>
    /// <param name="settings">The loaded application settings.</param>
    /// <param name="existingProfileNames">The names of all currently stored profiles.</param>
    /// <param name="isCliOverride">When true, CLI arguments are present and startup profile application is skipped (Requirement 2.5).</param>
    /// <returns>The startup decision describing the action and any notice to display.</returns>
    public static StartupDecision Decide(AppSettings settings, IReadOnlyList<string> existingProfileNames, bool isCliOverride = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(existingProfileNames);

        // Requirement 2.5: CLI arguments override all startup profile application.
        if (isCliOverride)
        {
            return new StartupDecision(StartupAction.CliOverride, null, null);
        }

        // Requirement 2.4 / 2.6: Default startup profile takes precedence when configured.
        if (!string.IsNullOrEmpty(settings.DefaultStartupProfileName))
        {
            bool defaultProfileExists = existingProfileNames.Any(name =>
                string.Equals(name, settings.DefaultStartupProfileName, StringComparison.OrdinalIgnoreCase));

            if (defaultProfileExists)
            {
                // Requirement 2.4: apply the configured default startup profile.
                return new StartupDecision(StartupAction.ApplyDefaultProfile, settings.DefaultStartupProfileName, null);
            }

            // Requirement 2.6: configured profile doesn't exist — warn and skip.
            string missingNotice = $"Default startup profile '{settings.DefaultStartupProfileName}' was not found.";
            return new StartupDecision(StartupAction.DefaultProfileMissing, settings.DefaultStartupProfileName, missingNotice);
        }

        // --- Existing AutoApplyOnStartup logic (fallback when no default profile is set) ---

        // Requirement 5.4: when auto-apply is disabled, read current values without changing them.
        if (!settings.AutoApplyOnStartup)
        {
            return new StartupDecision(StartupAction.AutoApplyDisabled, null, null);
        }

        string? lastProfile = settings.LastAppliedProfileName;
        bool profileExists =
            !string.IsNullOrEmpty(lastProfile) &&
            existingProfileNames.Any(name =>
                string.Equals(name, lastProfile, StringComparison.OrdinalIgnoreCase));

        // Requirement 5.3: auto-apply enabled and the last profile still exists -> apply it.
        if (profileExists)
        {
            return new StartupDecision(StartupAction.ApplyLastProfile, lastProfile, null);
        }

        // Requirement 5.6: auto-apply enabled but the last profile is missing (or none recorded)
        // -> skip applying, surface a notice, and read current values without changing them.
        string notice = string.IsNullOrEmpty(lastProfile)
            ? "Auto-apply is enabled but no profile has been applied yet."
            : $"Last profile '{lastProfile}' was not found.";
        return new StartupDecision(StartupAction.LastProfileMissing, lastProfile, notice);
    }

    /// <summary>
    /// Loads settings, decides the startup action, and performs it: applying the last-used
    /// profile when enabled and present, or otherwise leaving monitor brightness unchanged.
    /// </summary>
    /// <param name="isCliOverride">
    /// When true, CLI arguments are present (contain --monitor or --profile) and the startup
    /// profile application is skipped (Requirement 2.5). Defaults to false for backward compatibility.
    /// </param>
    /// <returns>
    /// A user-facing notice to display in the GUI (e.g. a missing-profile or apply-failure
    /// message), or null when there is nothing to report.
    /// </returns>
    public string? Run(bool isCliOverride = false)
    {
        // Loading already returns defaults for a missing/corrupt store (Requirement 5.7).
        AppSettings settings = _settingsStore.Load();

        // Requirement 1.4: reconcile the registry entry when StartWithWindows is enabled.
        if (_startupRegistration != null)
        {
            _startupRegistration.EnsureRegistration(settings.StartWithWindows);
        }

        IReadOnlyList<string> profileNames = _profileManager.GetAllProfiles()
            .Select(p => p.Name)
            .ToList();

        StartupDecision decision = Decide(settings, profileNames, isCliOverride: isCliOverride);

        switch (decision.Action)
        {
            case StartupAction.ApplyDefaultProfile:
                // Requirement 2.4: apply the configured default startup profile.
                Result<Unit> defaultApplied = _profileManager.ApplyProfile(decision.ProfileName!, _monitorService);
                if (defaultApplied.IsSuccess)
                {
                    // Requirement 2.8: update LastAppliedProfileName on success.
                    var updatedSettings = settings with { LastAppliedProfileName = decision.ProfileName };
                    _settingsStore.Save(updatedSettings);
                    return null;
                }
                // Requirement 2.7: handle apply failure (e.g. disconnected monitors) gracefully — log and continue.
                return $"Could not apply startup profile '{decision.ProfileName}': {defaultApplied.Error}";

            case StartupAction.DefaultProfileMissing:
                // Requirement 2.6: configured default profile doesn't exist — surface notice.
                return decision.Notice;

            case StartupAction.CliOverride:
                // Requirement 2.5: CLI override — skip all auto-apply.
                return null;

            case StartupAction.ApplyLastProfile:
                // Requirement 5.3: apply the last-used profile to all mapped connected monitors.
                Result<Unit> applied = _profileManager.ApplyProfile(decision.ProfileName!, _monitorService);
                return applied.IsSuccess
                    ? null
                    : $"Could not apply profile '{decision.ProfileName}': {applied.Error}";

            case StartupAction.LastProfileMissing:
                // Requirement 5.6: skip application, surface the notice; current values are read
                // by the monitor service without modification.
                return decision.Notice;

            case StartupAction.AutoApplyDisabled:
            default:
                // Requirement 5.4: nothing to apply; current values are read without changes.
                return null;
        }
    }

    /// <summary>
    /// Determines whether the given command-line arguments represent a CLI override
    /// (i.e., contain --monitor or --profile), which should skip startup profile application
    /// (Requirement 2.5).
    /// </summary>
    /// <param name="args">The command-line arguments passed to the application, or null.</param>
    /// <returns>True if CLI override is detected; false otherwise.</returns>
    public static bool IsCliOverride(string[]? args)
    {
        if (args == null || args.Length == 0)
            return false;

        return args.Any(arg =>
            arg.Equals("--monitor", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("--profile", StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith("--monitor=", StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith("--profile=", StringComparison.OrdinalIgnoreCase));
    }
}
