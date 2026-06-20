using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Input;
using MonitorBrightnessController.Application;
using MonitorBrightnessController.Infrastructure;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;

namespace MonitorBrightnessController.Presentation;

/// <summary>
/// View model for the main window. Detects monitors via <see cref="IMonitorService"/> and
/// exposes a bindable collection of per-monitor control view models. When no controllable
/// monitors are present, <see cref="HasNoControllableMonitors"/> drives an informational
/// message in the GUI (Requirement 2.9).
/// </summary>
/// <remarks>
/// When constructed with a settings store and profile manager, the view model also performs
/// startup auto-apply coordination (Requirements 5.3, 5.4, 5.6, 5.7) via
/// <see cref="StartupCoordinator"/> and surfaces the auto-apply toggle (Requirement 5.2)
/// through <see cref="AutoApplyOnStartup"/>, persisting changes to the settings store
/// (Requirement 5.1).
/// </remarks>
public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly IMonitorService _monitorService;
    private readonly ISettingsStore? _settingsStore;
    private readonly IProfileManager? _profileManager;
    private readonly IStartupRegistration? _startupRegistration;
    private readonly IApplicationInstaller? _applicationInstaller;
    private readonly IUpdateChecker? _updateChecker;
    private readonly TransitionCoordinator _transitionCoordinator = new();
    private bool _updateCheckPerformed;

    private bool _autoApplyOnStartup;
    private bool _minimizeToTray;
    private bool _smoothTransition;
    private int _transitionDurationMs = 500;
    private bool _startWithWindows;
    private bool _refreshOnFocus = true;
    private bool _checkForUpdatesOnStartup = true;
    private string? _startupNotice;
    private string _monitorsTabHeader = "Current Settings";
    private bool _isProperlyInstalled;
    private string? _installResultMessage;
    private string _selectedStartupProfile = "None";
    private string _selectedStartupProfileName = "Last Used";
    private string? _startupProfileError;
    private string? _selectedShortcutProfile;
    private string? _shortcutStatusMessage;
    private bool _isUpdateAvailable;
    private string _latestVersionText = string.Empty;
    private string _updateReleaseUrl = string.Empty;

    /// <summary>
    /// Creates the main window view model with only a monitor service. No startup auto-apply
    /// is performed and the auto-apply toggle is not persisted; used by the designer and by
    /// tests that exercise monitor display in isolation.
    /// </summary>
    /// <param name="monitorService">The monitor service used to detect monitors and apply brightness.</param>
    public MainWindowViewModel(IMonitorService monitorService)
    {
        _monitorService = monitorService ?? throw new ArgumentNullException(nameof(monitorService));

        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        AppVersion = version is not null ? $"{version.Major}.{version.Minor}.{version.Build}" : "Unknown";
        BuildDate = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "BuildDate")?.Value ?? "Unknown";

        ProperInstallCommand = new RelayCommand(ExecuteProperInstall, () => !IsProperlyInstalled);
        CreateShortcutCommand = new RelayCommand(ExecuteCreateShortcut, () => CanCreateShortcut);
        DismissUpdateCommand = new RelayCommand(() => IsUpdateAvailable = false);
        OpenReleaseUrlCommand = new RelayCommand(ExecuteOpenReleaseUrl);
        Load();
    }

    /// <summary>
    /// Creates the main window view model with full startup wiring. Loads settings, optionally
    /// runs the startup auto-apply coordination (Requirements 5.3, 5.4, 5.6, 5.7), seeds the
    /// auto-apply toggle from the loaded settings (Requirement 5.2), and then detects monitors
    /// so their current brightness values are reflected in the UI.
    /// </summary>
    /// <param name="monitorService">The monitor service used to detect monitors and apply brightness.</param>
    /// <param name="settingsStore">The settings store used to load and persist preferences.</param>
    /// <param name="profileManager">The profile manager used to apply the last-used profile.</param>
    /// <param name="startupRegistration">Optional startup registration for managing auto-start with Windows.</param>
    /// <param name="applicationInstaller">Optional installer for copying the app to Program Files.</param>
    /// <param name="updateChecker">Optional update checker for querying GitHub releases on startup.</param>
    /// <param name="skipAutoApply">
    /// When true, skips StartupCoordinator.Run() and profile auto-apply entirely. Used for
    /// manual launches (no --silent flag) where the UI should display hardware values only
    /// without applying any profile (Requirements 1.2, 1.3).
    /// </param>
    public MainWindowViewModel(
        IMonitorService monitorService,
        ISettingsStore settingsStore,
        IProfileManager profileManager,
        IStartupRegistration? startupRegistration = null,
        IApplicationInstaller? applicationInstaller = null,
        IUpdateChecker? updateChecker = null,
        bool skipAutoApply = false)
    {
        _monitorService = monitorService ?? throw new ArgumentNullException(nameof(monitorService));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
        _startupRegistration = startupRegistration;
        _applicationInstaller = applicationInstaller;
        _updateChecker = updateChecker;

        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        AppVersion = version is not null ? $"{version.Major}.{version.Minor}.{version.Build}" : "Unknown";
        BuildDate = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "BuildDate")?.Value ?? "Unknown";

        // Determine install state (Req 4.1, 4.9)
        _isProperlyInstalled = _applicationInstaller?.IsInstalledInProgramFiles() ?? false;
        ProperInstallCommand = new RelayCommand(ExecuteProperInstall, () => !IsProperlyInstalled);
        CreateShortcutCommand = new RelayCommand(ExecuteCreateShortcut, () => CanCreateShortcut);
        DismissUpdateCommand = new RelayCommand(() => IsUpdateAvailable = false);
        OpenReleaseUrlCommand = new RelayCommand(ExecuteOpenReleaseUrl);

        // Seed the toggles from persisted settings (defaults applied on first use).
        var loadedSettings = _settingsStore.Load();
        _autoApplyOnStartup = loadedSettings.AutoApplyOnStartup;
        _minimizeToTray = loadedSettings.MinimizeToTray;
        _smoothTransition = loadedSettings.SmoothTransition;
        _transitionDurationMs = loadedSettings.TransitionDurationMs;
        _startWithWindows = loadedSettings.StartWithWindows;
        _refreshOnFocus = loadedSettings.RefreshOnFocus;
        _checkForUpdatesOnStartup = loadedSettings.CheckForUpdatesOnStartup;

        // Initialize the startup profile dropdown (Req 3.1, 3.2)
        _selectedStartupProfile = loadedSettings.DefaultStartupProfileName ?? "None";
        // Initialize the new startup profile name (Req 6.5, 6.8)
        _selectedStartupProfileName = loadedSettings.DefaultStartupProfileName ?? "Last Used";
        RefreshStartupProfileDropdown();

        if (skipAutoApply)
        {
            // Manual launch path (Requirements 1.2, 1.3): read hardware values only,
            // no StartupCoordinator.Run(), no profile application, no profile selected.
            Load();
            MonitorsTabHeader = "Current Settings";
        }
        else
        {
            // Silent/auto-apply launch path: perform startup auto-apply coordination
            // (Requirements 5.3, 5.4, 5.6, 5.7). Any notice is surfaced in the GUI.
            var coordinator = new StartupCoordinator(_settingsStore, _profileManager, _monitorService, _startupRegistration);

            // Determine the startup decision to know which profile (if any) was targeted
            IReadOnlyList<string> profileNames = _profileManager.GetAllProfiles()
                .Select(p => p.Name)
                .ToList();
            var decision = StartupCoordinator.Decide(loadedSettings, profileNames);

            _startupNotice = coordinator.Run();

            // Detect monitors so the UI shows current brightness values (read without modifying
            // when auto-apply is disabled or the last profile was missing).
            Load();

            // Startup slider synchronization (Requirements 1.1, 1.2, 1.3, 1.4):
            // - When no startup profile applies (Req 1.1): sliders already show hardware-reported
            //   values from Load(). DDC/CI failures default to midpoint (50) in MonitorControlViewModel.
            // - When startup profile was applied successfully (Req 1.2): preview the profile values
            //   on mapped monitors. Unmapped monitors retain their hardware-reported values.
            // - When startup profile application fails (Req 1.3): _startupNotice is non-null,
            //   sliders fall back to hardware-reported values (already set by Load()).
            bool profileAppliedSuccessfully = _startupNotice is null &&
                (decision.Action == StartupAction.ApplyDefaultProfile ||
                 decision.Action == StartupAction.ApplyLastProfile);

            if (profileAppliedSuccessfully && !string.IsNullOrEmpty(decision.ProfileName))
            {
                PreviewProfile(decision.ProfileName);
                MonitorsTabHeader = $"Profile: {decision.ProfileName}";
            }
            else
            {
                MonitorsTabHeader = "Current Settings";
            }
        }

        // Fire-and-forget update check (Requirement 5.1, 5.7)
        if (loadedSettings.CheckForUpdatesOnStartup && _updateChecker is not null)
        {
            _ = CheckForUpdateInternalAsync();
        }
    }

    /// <summary>
    /// The application version in "Major.Minor.Patch" format, derived from the assembly version
    /// at startup (Requirement 7.3, 7.5).
    /// </summary>
    public string AppVersion { get; }

    /// <summary>
    /// The build date in "yyyy-MM-dd" format, derived from the AssemblyMetadata attribute
    /// at compile time (Requirement 7.4, 7.5).
    /// </summary>
    public string BuildDate { get; }

    /// <summary>The per-monitor control view models rendered by the GUI (Requirement 2.1).</summary>
    public ObservableCollection<MonitorControlViewModel> Monitors { get; } = new();

    /// <summary>
    /// True when there are no monitor controls to display, used to show the
    /// "no controllable monitors" message (Requirement 2.9).
    /// </summary>
    public bool HasNoControllableMonitors => Monitors.Count == 0;

    /// <summary>The message shown when no controllable monitors are detected.</summary>
    public string NoMonitorsMessage => "No controllable monitors were found.";

    /// <summary>
    /// Whether the last-used profile is applied automatically on GUI startup (Requirement 5.2).
    /// Setting this value persists the preference to the settings store (Requirement 5.1) when
    /// the view model was constructed with a settings store.
    /// </summary>
    public bool AutoApplyOnStartup
    {
        get => _autoApplyOnStartup;
        set
        {
            if (_autoApplyOnStartup == value)
            {
                return;
            }

            _autoApplyOnStartup = value;
            OnPropertyChanged();

            PersistAutoApply(value);
        }
    }

    /// <summary>
    /// Whether minimize/close hides to system tray. When changed, persists immediately
    /// and raises an event so the window can enable/disable the tray manager.
    /// </summary>
    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set
        {
            if (_minimizeToTray == value)
            {
                return;
            }

            _minimizeToTray = value;
            OnPropertyChanged();

            PersistMinimizeToTray(value);
        }
    }

    /// <summary>
    /// Whether brightness changes animate smoothly. When changed, persists immediately
    /// and updates all monitor view models.
    /// </summary>
    public bool SmoothTransition
    {
        get => _smoothTransition;
        set
        {
            if (_smoothTransition == value)
            {
                return;
            }

            _smoothTransition = value;
            OnPropertyChanged();

            UpdateMonitorTransitionSettings();
            PersistSetting(s => s with { SmoothTransition = value });
        }
    }

    /// <summary>
    /// Duration of smooth brightness transitions in milliseconds (100–2000). When changed,
    /// persists immediately and updates all monitor view models.
    /// </summary>
    public int TransitionDurationMs
    {
        get => _transitionDurationMs;
        set
        {
            // Clamp to valid range
            value = Math.Clamp(value, 100, 2000);

            if (_transitionDurationMs == value)
            {
                return;
            }

            _transitionDurationMs = value;
            OnPropertyChanged();

            UpdateMonitorTransitionSettings();
            PersistSetting(s => s with { TransitionDurationMs = value });
        }
    }

    /// <summary>
    /// Whether the application starts with Windows. When changed, persists immediately
    /// and updates the Windows registry.
    /// </summary>
    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (_startWithWindows == value)
            {
                return;
            }

            _startWithWindows = value;
            OnPropertyChanged();

            // Requirement 3.4: Persist the StartWithWindows boolean in SettingsStore
            // independently of whether the registry operation succeeds or fails.
            PersistSetting(s => s with { StartWithWindows = value });

            // Attempt registry update; on failure, display error but do NOT revert (Requirement 3.6).
            var registryResult = _startupRegistration?.SetStartWithWindows(value);
            if (registryResult.HasValue && !registryResult.Value.IsSuccess)
            {
                StartupNotice = registryResult.Value.Error
                    ?? "The registry could not be updated. The setting has been saved but may not take effect until the registry is accessible.";
            }
        }
    }

    /// <summary>
    /// Whether brightness values are refreshed from hardware when the window gains focus.
    /// When changed, persists immediately.
    /// </summary>
    public bool RefreshOnFocus
    {
        get => _refreshOnFocus;
        set
        {
            if (_refreshOnFocus == value)
            {
                return;
            }

            _refreshOnFocus = value;
            OnPropertyChanged();

            PersistSetting(s => s with { RefreshOnFocus = value });
        }
    }

    /// <summary>
    /// Whether the application checks GitHub for updates on GUI startup (Requirement 6.2, 6.3).
    /// When changed, persists immediately via the settings store.
    /// </summary>
    public bool CheckForUpdatesOnStartup
    {
        get => _checkForUpdatesOnStartup;
        set
        {
            if (_checkForUpdatesOnStartup == value)
            {
                return;
            }

            _checkForUpdatesOnStartup = value;
            OnPropertyChanged();

            PersistSetting(s => s with { CheckForUpdatesOnStartup = value });
        }
    }

    /// <summary>
    /// A user-facing notice produced during startup (e.g. the last profile was not found, or
    /// an auto-apply failure). Null when there is nothing to report.
    /// </summary>
    public string? StartupNotice
    {
        get => _startupNotice;
        private set
        {
            if (_startupNotice == value)
            {
                return;
            }

            _startupNotice = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasStartupNotice));
        }
    }

    /// <summary>True when a startup notice is currently displayed.</summary>
    public bool HasStartupNotice => !string.IsNullOrEmpty(_startupNotice);

    /// <summary>
    /// The header text displayed above the monitor controls in the Monitors tab.
    /// Shows the applied profile name when a startup profile was used, or "Current Settings"
    /// when displaying live DDC/CI values (Requirement 3.2).
    /// </summary>
    public string MonitorsTabHeader
    {
        get => _monitorsTabHeader;
        private set
        {
            if (_monitorsTabHeader == value)
            {
                return;
            }

            _monitorsTabHeader = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Whether the application is currently running from the Program Files install directory (Req 4.9).
    /// When true, the "Proper Install" button is disabled and <see cref="InstallStatusText"/> shows
    /// an installed confirmation.
    /// </summary>
    public bool IsProperlyInstalled
    {
        get => _isProperlyInstalled;
        private set
        {
            if (_isProperlyInstalled == value)
            {
                return;
            }

            _isProperlyInstalled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(InstallStatusText));
            ProperInstallCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Status label shown when the application is already properly installed (Req 4.9).
    /// Empty when not installed.
    /// </summary>
    public string InstallStatusText => IsProperlyInstalled ? "Application is properly installed" : "";

    /// <summary>
    /// Message surfaced to the UI after an install attempt — confirmation on success (Req 4.8, 4.10),
    /// or error details on failure (Req 4.6, 4.7).
    /// </summary>
    public string? InstallResultMessage
    {
        get => _installResultMessage;
        private set
        {
            if (_installResultMessage == value)
            {
                return;
            }

            _installResultMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasInstallResultMessage));
        }
    }

    /// <summary>True when an install result message is available for display.</summary>
    public bool HasInstallResultMessage => !string.IsNullOrEmpty(_installResultMessage);

    /// <summary>
    /// Command that triggers the install-to-Program-Files flow (Req 4.1).
    /// Enabled only when <see cref="IsProperlyInstalled"/> is false.
    /// </summary>
    public RelayCommand ProperInstallCommand { get; }

    /// <summary>
    /// The list of profiles available for selection as the default startup profile.
    /// Contains "None" as the first entry followed by profile names in store order (Req 3.1).
    /// </summary>
    public ObservableCollection<string> AvailableProfilesForStartup { get; } = new();

    /// <summary>
    /// The list of options for the Startup Profile dropdown on the Settings tab.
    /// Contains "Last Used" as the first item followed by all saved profile names
    /// in case-insensitive alphabetical order (Requirement 6.5).
    /// </summary>
    public ObservableCollection<string> StartupProfileOptions { get; } = new();

    /// <summary>
    /// The list of profile names available for the Create Shortcut dropdown on the Settings tab.
    /// Contains all saved profile names in case-insensitive alphabetical order (no "Last Used").
    /// (Requirement 5.2)
    /// </summary>
    public ObservableCollection<string> ShortcutProfileOptions { get; } = new();

    /// <summary>
    /// The profile selected in the Create Shortcut dropdown. Null by default (no selection).
    /// When changed, updates <see cref="CanCreateShortcut"/> (Requirement 5.5).
    /// </summary>
    public string? SelectedShortcutProfile
    {
        get => _selectedShortcutProfile;
        set
        {
            if (_selectedShortcutProfile == value)
            {
                return;
            }

            _selectedShortcutProfile = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanCreateShortcut));
            CreateShortcutCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// True when a profile is selected in the Create Shortcut dropdown, enabling the button
    /// (Requirement 5.5).
    /// </summary>
    public bool CanCreateShortcut => !string.IsNullOrEmpty(_selectedShortcutProfile);

    /// <summary>
    /// Command that triggers shortcut creation for the selected profile.
    /// Enabled only when <see cref="CanCreateShortcut"/> is true (Requirement 5.5).
    /// </summary>
    public RelayCommand CreateShortcutCommand { get; }

    /// <summary>
    /// Status message displayed after a shortcut creation attempt. Shows success with the
    /// file name on success (Requirement 5.6), or an error message on failure (Requirement 5.7).
    /// Cleared on the next creation attempt.
    /// </summary>
    public string? ShortcutStatusMessage
    {
        get => _shortcutStatusMessage;
        set
        {
            if (_shortcutStatusMessage == value)
            {
                return;
            }

            _shortcutStatusMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasShortcutStatusMessage));
        }
    }

    /// <summary>True when a shortcut status message is available for display.</summary>
    public bool HasShortcutStatusMessage => !string.IsNullOrEmpty(_shortcutStatusMessage);

    /// <summary>
    /// Whether a newer version of the application is available. When true, the update
    /// notification banner is displayed in the main window (Requirement 5.2).
    /// </summary>
    public bool IsUpdateAvailable
    {
        get => _isUpdateAvailable;
        set
        {
            if (_isUpdateAvailable == value)
            {
                return;
            }

            _isUpdateAvailable = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Display text for the update notification, e.g. "Version 1.5.0 is available" (Requirement 5.2).
    /// </summary>
    public string LatestVersionText
    {
        get => _latestVersionText;
        set
        {
            if (_latestVersionText == value)
            {
                return;
            }

            _latestVersionText = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The URL to the GitHub release page for the latest version (Requirement 5.3).
    /// </summary>
    public string UpdateReleaseUrl
    {
        get => _updateReleaseUrl;
        set
        {
            if (_updateReleaseUrl == value)
            {
                return;
            }

            _updateReleaseUrl = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Command that dismisses the update notification by setting <see cref="IsUpdateAvailable"/> to false.
    /// </summary>
    public RelayCommand DismissUpdateCommand { get; } = null!;

    /// <summary>
    /// Command that opens <see cref="UpdateReleaseUrl"/> in the user's default browser (Requirement 5.3).
    /// </summary>
    public RelayCommand OpenReleaseUrlCommand { get; } = null!;

    /// <summary>
    /// Delegate for actual shortcut creation logic (COM interop with WScript.Shell).
    /// Wired from code-behind. Accepts the profile name and returns a Result indicating
    /// success or failure. When null, the command will set an error message.
    /// </summary>
    public Func<string, Result<Unit>>? CreateShortcutFunc { get; set; }

    /// <summary>
    /// The currently selected startup profile name for the unified Startup Profile section.
    /// "Last Used" maps to <c>DefaultStartupProfileName = null</c>; a specific profile name
    /// maps to <c>DefaultStartupProfileName = thatName</c>.
    /// Persists the selection immediately on change (Requirement 6.8).
    /// </summary>
    public string SelectedStartupProfileName
    {
        get => _selectedStartupProfileName;
        set
        {
            if (_selectedStartupProfileName == value)
            {
                return;
            }

            var previousValue = _selectedStartupProfileName;
            _selectedStartupProfileName = value;
            OnPropertyChanged();

            // Map "Last Used" → null for persistence (Requirement 6.8)
            var profileNameToSave = value == "Last Used" ? null : value;

            if (_settingsStore is not null)
            {
                AppSettings settings = _settingsStore.Load();
                var result = _settingsStore.Save(settings with { DefaultStartupProfileName = profileNameToSave });

                if (!result.IsSuccess)
                {
                    // Revert on failure
                    _selectedStartupProfileName = previousValue;
                    OnPropertyChanged(nameof(SelectedStartupProfileName));
                    StartupProfileError = result.Error ?? "Failed to save startup profile selection.";
                }
                else
                {
                    StartupProfileError = null;
                }
            }
        }
    }

    /// <summary>
    /// The currently selected default startup profile. "None" means no profile is applied at startup.
    /// When changed, persists the selection immediately without a separate save action (Req 3.3, 3.4).
    /// On persist failure, reverts to the previous value and sets an error (Req 3.6).
    /// </summary>
    public string SelectedStartupProfile
    {
        get => _selectedStartupProfile;
        set
        {
            if (_selectedStartupProfile == value)
            {
                return;
            }

            var previousValue = _selectedStartupProfile;
            _selectedStartupProfile = value;
            OnPropertyChanged();

            // Map "None" → null for persistence (Req 3.4)
            var profileNameToSave = value == "None" ? null : value;

            if (_settingsStore is not null)
            {
                AppSettings settings = _settingsStore.Load();
                var result = _settingsStore.Save(settings with { DefaultStartupProfileName = profileNameToSave });

                if (!result.IsSuccess)
                {
                    // Revert on failure (Req 3.6)
                    _selectedStartupProfile = previousValue;
                    OnPropertyChanged(nameof(SelectedStartupProfile));
                    StartupProfileError = result.Error ?? "Failed to save default startup profile setting.";
                }
                else
                {
                    StartupProfileError = null;
                }
            }
        }
    }

    /// <summary>
    /// Error message displayed when persisting the default startup profile fails (Req 3.6).
    /// Null when there is no error.
    /// </summary>
    public string? StartupProfileError
    {
        get => _startupProfileError;
        private set
        {
            if (_startupProfileError == value)
            {
                return;
            }

            _startupProfileError = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasStartupProfileError));
        }
    }

    /// <summary>True when a startup profile persist error is currently displayed.</summary>
    public bool HasStartupProfileError => !string.IsNullOrEmpty(_startupProfileError);

    /// <summary>
    /// Rebuilds <see cref="AvailableProfilesForStartup"/>, <see cref="StartupProfileOptions"/>,
    /// and <see cref="ShortcutProfileOptions"/> from the current profiles in the settings store.
    /// For AvailableProfilesForStartup: "None" is first, followed by profile names in store order.
    /// For StartupProfileOptions: "Last Used" is first, followed by profile names in case-insensitive
    /// alphabetical order (Requirement 6.5).
    /// For ShortcutProfileOptions: all profile names in case-insensitive alphabetical order (Requirement 5.2).
    /// If the current DefaultStartupProfileName no longer matches any profile, resets selections
    /// to their default values and persists the change (Requirements 3.5, 6.9).
    /// </summary>
    public void RefreshStartupProfileDropdown()
    {
        AvailableProfilesForStartup.Clear();
        AvailableProfilesForStartup.Add("None");

        StartupProfileOptions.Clear();
        StartupProfileOptions.Add("Last Used");

        ShortcutProfileOptions.Clear();

        if (_settingsStore is not null)
        {
            var settings = _settingsStore.Load();
            foreach (var profile in settings.Profiles)
            {
                AvailableProfilesForStartup.Add(profile.Name);
            }

            // Add profile names in case-insensitive alphabetical order for StartupProfileOptions and ShortcutProfileOptions
            var sortedNames = settings.Profiles
                .Select(p => p.Name)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var name in sortedNames)
            {
                StartupProfileOptions.Add(name);
                ShortcutProfileOptions.Add(name);
            }

            // Validate current selection still exists
            var currentDefault = settings.DefaultStartupProfileName;
            if (currentDefault is not null &&
                !settings.Profiles.Exists(p => string.Equals(p.Name, currentDefault, StringComparison.OrdinalIgnoreCase)))
            {
                // Profile no longer exists — reset to "None" (Req 3.5)
                _selectedStartupProfile = "None";
                OnPropertyChanged(nameof(SelectedStartupProfile));

                // Also reset SelectedStartupProfileName to "Last Used" (Req 6.9)
                _selectedStartupProfileName = "Last Used";
                OnPropertyChanged(nameof(SelectedStartupProfileName));
            }

            // If the selected shortcut profile no longer exists, clear it
            if (_selectedShortcutProfile is not null &&
                !settings.Profiles.Exists(p => string.Equals(p.Name, _selectedShortcutProfile, StringComparison.OrdinalIgnoreCase)))
            {
                SelectedShortcutProfile = null;
            }
        }
    }

    /// <summary>
    /// Called when a profile is deleted. If the deleted profile was the default startup profile,
    /// clears the setting and updates the dropdowns (Requirements 3.5, 6.9).
    /// </summary>
    /// <param name="deletedProfileName">The name of the deleted profile.</param>
    public void NotifyProfileDeleted(string deletedProfileName)
    {
        if (_settingsStore is null)
        {
            return;
        }

        var settings = _settingsStore.Load();
        if (string.Equals(settings.DefaultStartupProfileName, deletedProfileName, StringComparison.OrdinalIgnoreCase))
        {
            // Clear the default since the profile no longer exists (Req 3.5, 6.9)
            _settingsStore.Save(settings with { DefaultStartupProfileName = null });
            _selectedStartupProfile = "None";
            OnPropertyChanged(nameof(SelectedStartupProfile));

            // Reset SelectedStartupProfileName to "Last Used" and persist (Req 6.9)
            _selectedStartupProfileName = "Last Used";
            OnPropertyChanged(nameof(SelectedStartupProfileName));
        }

        RefreshStartupProfileDropdown();
    }

    /// <summary>
    /// Called when a profile is created. Refreshes the dropdown list (Req 3.7).
    /// </summary>
    public void NotifyProfileCreated()
    {
        RefreshStartupProfileDropdown();
    }

    /// <summary>
    /// Refreshes all profile-related dropdowns after a profile create or delete operation.
    /// This is a convenience method that ensures all dropdown collections stay consistent.
    /// </summary>
    public void RefreshAllProfileDropdowns()
    {
        RefreshStartupProfileDropdown();
    }

    /// <summary>
    /// Detects monitors and rebuilds the <see cref="Monitors"/> collection. Each control view
    /// model commits brightness changes through <see cref="IMonitorService.SetBrightness"/>
    /// and gamma changes through <see cref="IMonitorService.SetGamma"/>. Smooth transitions
    /// for both settings are managed by the shared <see cref="TransitionCoordinator"/>.
    /// </summary>
    public void Load()
    {
        Monitors.Clear();

        IReadOnlyList<MonitorState> detected = _monitorService.DetectMonitors();
        foreach (MonitorState state in detected)
        {
            var vm = new MonitorControlViewModel(
                state,
                _monitorService.SetBrightness,
                _monitorService.SetGamma,
                _transitionCoordinator)
            {
                SmoothTransitionEnabled = _smoothTransition,
                TransitionDurationMs = _transitionDurationMs
            };
            Monitors.Add(vm);
        }

        OnPropertyChanged(nameof(HasNoControllableMonitors));
    }

    /// <summary>
    /// Re-reads brightness and gamma from hardware for each monitor without full re-detection.
    /// Updates view model values to reflect any external changes.
    /// </summary>
    public void RefreshMonitorValues()
    {
        foreach (var vm in Monitors)
        {
            var brightnessResult = _monitorService.GetBrightness(vm.MonitorIndex);
            if (brightnessResult.IsSuccess)
            {
                vm.Brightness = brightnessResult.Value;
            }

            var gammaResult = _monitorService.GetGamma(vm.MonitorIndex);
            if (gammaResult.IsSuccess)
            {
                vm.Gamma = gammaResult.Value;
            }
        }
    }

    /// <summary>
    /// Restores each monitor's sliders to the current hardware-reported brightness and gamma values.
    /// Called when the profile dropdown selection is cleared (deselected). For monitors where the
    /// hardware read succeeds, the slider is updated to the hardware value. For monitors where the
    /// read fails, the last displayed value is retained and an error message is shown
    /// (Requirement 4.6).
    /// </summary>
    /// <remarks>Requirements 4.5, 4.6</remarks>
    public void RestoreHardwareValues()
    {
        var failedMonitors = new List<string>();

        foreach (var monitor in Monitors)
        {
            bool monitorFailed = false;

            var brightnessResult = _monitorService.GetBrightness(monitor.MonitorIndex);
            if (brightnessResult.IsSuccess)
            {
                monitor.Brightness = brightnessResult.Value;
            }
            else
            {
                monitorFailed = true;
            }

            var gammaResult = _monitorService.GetGamma(monitor.MonitorIndex);
            if (gammaResult.IsSuccess)
            {
                monitor.Gamma = gammaResult.Value;
            }
            else
            {
                monitorFailed = true;
            }

            if (monitorFailed)
            {
                failedMonitors.Add(monitor.MonitorName);
            }
        }

        // Requirement 4.6: display an error message indicating which monitors could not be read
        if (failedMonitors.Count > 0)
        {
            StartupNotice = $"Could not read hardware values for: {string.Join(", ", failedMonitors)}. Sliders remain at their last displayed position.";
        }
    }

    /// <summary>
    /// Previews a profile's brightness and gamma values on the monitor sliders without applying
    /// to hardware. For each monitor present in the profile's maps, the corresponding slider is
    /// updated; monitors not in the maps retain their current displayed values.
    /// Legacy profiles (null MonitorGammaMap) leave gamma sliders unchanged (Requirement 4.3).
    /// </summary>
    /// <param name="profileName">
    /// The name of the profile to preview. If null or empty, the method returns immediately
    /// (deselection is handled by <see cref="RestoreHardwareValues"/>).
    /// </param>
    /// <remarks>Requirements 4.1, 4.2, 4.3</remarks>
    public void PreviewProfile(string? profileName)
    {
        if (string.IsNullOrEmpty(profileName))
        {
            return;
        }

        if (_profileManager is null)
        {
            return;
        }

        var profileResult = _profileManager.GetProfile(profileName);
        if (!profileResult.IsSuccess)
        {
            return;
        }

        var profile = profileResult.Value;

        foreach (var monitor in Monitors)
        {
            // Update brightness if the profile maps this monitor
            if (profile.MonitorBrightnessMap.TryGetValue(monitor.DevicePath, out int brightness))
            {
                monitor.Brightness = brightness;
            }

            // Update gamma if the profile has a gamma map and maps this monitor
            if (profile.MonitorGammaMap is not null &&
                profile.MonitorGammaMap.TryGetValue(monitor.DevicePath, out int gamma))
            {
                monitor.Gamma = gamma;
            }
        }
    }

    /// <summary>
    /// Executes the shortcut creation flow by invoking <see cref="CreateShortcutFunc"/>.
    /// Clears the previous status message before attempting creation.
    /// On success, sets <see cref="ShortcutStatusMessage"/> to indicate the created file.
    /// On failure, sets <see cref="ShortcutStatusMessage"/> to the error message.
    /// </summary>
    private void ExecuteCreateShortcut()
    {
        ShortcutStatusMessage = null;

        if (string.IsNullOrEmpty(_selectedShortcutProfile))
        {
            return;
        }

        if (CreateShortcutFunc is null)
        {
            ShortcutStatusMessage = "Shortcut creation is not available.";
            return;
        }

        var result = CreateShortcutFunc(_selectedShortcutProfile);
        if (result.IsSuccess)
        {
            ShortcutStatusMessage = $"Shortcut created: Brightness - {_selectedShortcutProfile}.lnk";
        }
        else
        {
            ShortcutStatusMessage = result.Error ?? "Failed to create shortcut.";
        }
    }

    /// <summary>
    /// Executes the proper install flow: copies the exe to Program Files,
    /// updates the registry if StartWithWindows is enabled, and surfaces the result (Req 4.3, 4.6, 4.7, 4.8, 4.10).
    /// </summary>
    private void ExecuteProperInstall()
    {
        if (_applicationInstaller is null)
        {
            return;
        }

        var result = _applicationInstaller.InstallToProgramFiles();
        if (result.IsSuccess)
        {
            // Update registry if StartWithWindows is enabled (Req 4.3)
            if (_startWithWindows)
            {
                _startupRegistration?.UpdateRegisteredPath(result.Value);
            }

            // Surface install success info (Req 4.8, 4.10)
            InstallResultMessage = $"Installed to: {result.Value}. Please restart the application from the new location.";
            IsProperlyInstalled = true;
        }
        else
        {
            // Surface error (Req 4.6, 4.7)
            InstallResultMessage = result.Error ?? "Install failed.";
        }
    }

    /// <summary>
    /// Persists the auto-apply preference to the settings store (Requirement 5.1). Does
    /// nothing when no settings store was provided.
    /// </summary>
    private void PersistAutoApply(bool value)
    {
        if (_settingsStore is null)
        {
            return;
        }

        AppSettings settings = _settingsStore.Load();
        _settingsStore.Save(settings with { AutoApplyOnStartup = value });
    }

    private void PersistMinimizeToTray(bool value)
    {
        if (_settingsStore is null)
        {
            return;
        }

        AppSettings settings = _settingsStore.Load();
        _settingsStore.Save(settings with { MinimizeToTray = value });
    }

    /// <summary>
    /// Persists a setting change using the provided transform function.
    /// </summary>
    private void PersistSetting(Func<AppSettings, AppSettings> transform)
    {
        if (_settingsStore is null)
        {
            return;
        }

        AppSettings settings = _settingsStore.Load();
        _settingsStore.Save(transform(settings));
    }

    /// <summary>
    /// Pushes current smooth transition settings to all monitor view models.
    /// </summary>
    private void UpdateMonitorTransitionSettings()
    {
        foreach (var vm in Monitors)
        {
            vm.SmoothTransitionEnabled = _smoothTransition;
            vm.TransitionDurationMs = _transitionDurationMs;
        }
    }

    /// <summary>
    /// Opens <see cref="UpdateReleaseUrl"/> in the user's default browser (Requirement 5.3).
    /// </summary>
    private void ExecuteOpenReleaseUrl()
    {
        if (string.IsNullOrEmpty(_updateReleaseUrl))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = _updateReleaseUrl,
            UseShellExecute = true
        });
    }

    /// <summary>
    /// Asynchronously checks for application updates. Populates notification properties
    /// when a newer version is detected. Ensures only one check per launch and silently
    /// catches all exceptions (Requirement 5.5, 5.7).
    /// </summary>
    private async Task CheckForUpdateInternalAsync()
    {
        if (_updateCheckPerformed)
        {
            return;
        }

        _updateCheckPerformed = true;

        try
        {
            var result = await _updateChecker!.CheckForUpdateAsync().ConfigureAwait(false);
            if (result.IsUpdateAvailable)
            {
                LatestVersionText = $"Version {result.LatestVersion} is available";
                UpdateReleaseUrl = result.ReleaseUrl ?? string.Empty;
                IsUpdateAvailable = true;
            }
        }
        catch
        {
            // Silently swallow all exceptions (Requirement 5.5)
        }
    }
}
