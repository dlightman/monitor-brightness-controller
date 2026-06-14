using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
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
    private readonly TransitionCoordinator _transitionCoordinator = new();

    private bool _autoApplyOnStartup;
    private bool _minimizeToTray;
    private bool _smoothTransition;
    private int _transitionDurationMs = 500;
    private bool _startWithWindows;
    private bool _refreshOnFocus = true;
    private string? _startupNotice;
    private bool _isProperlyInstalled;
    private string? _installResultMessage;
    private string _selectedStartupProfile = "None";
    private string? _startupProfileError;

    /// <summary>
    /// Creates the main window view model with only a monitor service. No startup auto-apply
    /// is performed and the auto-apply toggle is not persisted; used by the designer and by
    /// tests that exercise monitor display in isolation.
    /// </summary>
    /// <param name="monitorService">The monitor service used to detect monitors and apply brightness.</param>
    public MainWindowViewModel(IMonitorService monitorService)
    {
        _monitorService = monitorService ?? throw new ArgumentNullException(nameof(monitorService));
        ProperInstallCommand = new RelayCommand(ExecuteProperInstall, () => !IsProperlyInstalled);
        Load();
    }

    /// <summary>
    /// Creates the main window view model with full startup wiring. Loads settings, runs the
    /// startup auto-apply coordination (Requirements 5.3, 5.4, 5.6, 5.7), seeds the auto-apply
    /// toggle from the loaded settings (Requirement 5.2), and then detects monitors so their
    /// current brightness values are reflected in the UI.
    /// </summary>
    /// <param name="monitorService">The monitor service used to detect monitors and apply brightness.</param>
    /// <param name="settingsStore">The settings store used to load and persist preferences.</param>
    /// <param name="profileManager">The profile manager used to apply the last-used profile.</param>
    /// <param name="startupRegistration">Optional startup registration for managing auto-start with Windows.</param>
    /// <param name="applicationInstaller">Optional installer for copying the app to Program Files.</param>
    public MainWindowViewModel(
        IMonitorService monitorService,
        ISettingsStore settingsStore,
        IProfileManager profileManager,
        IStartupRegistration? startupRegistration = null,
        IApplicationInstaller? applicationInstaller = null)
    {
        _monitorService = monitorService ?? throw new ArgumentNullException(nameof(monitorService));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
        _startupRegistration = startupRegistration;
        _applicationInstaller = applicationInstaller;

        // Determine install state (Req 4.1, 4.9)
        _isProperlyInstalled = _applicationInstaller?.IsInstalledInProgramFiles() ?? false;
        ProperInstallCommand = new RelayCommand(ExecuteProperInstall, () => !IsProperlyInstalled);

        // Seed the toggles from persisted settings (defaults applied on first use).
        var loadedSettings = _settingsStore.Load();
        _autoApplyOnStartup = loadedSettings.AutoApplyOnStartup;
        _minimizeToTray = loadedSettings.MinimizeToTray;
        _smoothTransition = loadedSettings.SmoothTransition;
        _transitionDurationMs = loadedSettings.TransitionDurationMs;
        _startWithWindows = loadedSettings.StartWithWindows;
        _refreshOnFocus = loadedSettings.RefreshOnFocus;

        // Initialize the startup profile dropdown (Req 3.1, 3.2)
        _selectedStartupProfile = loadedSettings.DefaultStartupProfileName ?? "None";
        RefreshStartupProfileDropdown();

        // Perform startup auto-apply coordination before reading current values
        // (Requirements 5.3, 5.4, 5.6, 5.7). Any notice is surfaced in the GUI.
        var coordinator = new StartupCoordinator(_settingsStore, _profileManager, _monitorService, _startupRegistration);
        _startupNotice = coordinator.Run();

        // Detect monitors so the UI shows current brightness values (read without modifying
        // when auto-apply is disabled or the last profile was missing).
        Load();
    }

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

            _startupRegistration?.SetStartWithWindows(value);
            PersistSetting(s => s with { StartWithWindows = value });
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
    /// Rebuilds <see cref="AvailableProfilesForStartup"/> from the current profiles in the settings store.
    /// "None" is always the first entry, followed by profile names in store order (Req 3.1, 3.7).
    /// If the current DefaultStartupProfileName no longer matches any profile, resets selection to "None" (Req 3.2, 3.5).
    /// </summary>
    public void RefreshStartupProfileDropdown()
    {
        AvailableProfilesForStartup.Clear();
        AvailableProfilesForStartup.Add("None");

        if (_settingsStore is not null)
        {
            var settings = _settingsStore.Load();
            foreach (var profile in settings.Profiles)
            {
                AvailableProfilesForStartup.Add(profile.Name);
            }

            // Validate current selection still exists
            var currentDefault = settings.DefaultStartupProfileName;
            if (currentDefault is not null &&
                !settings.Profiles.Exists(p => string.Equals(p.Name, currentDefault, StringComparison.OrdinalIgnoreCase)))
            {
                // Profile no longer exists — reset to "None" (Req 3.5)
                _selectedStartupProfile = "None";
                OnPropertyChanged(nameof(SelectedStartupProfile));
            }
        }
    }

    /// <summary>
    /// Called when a profile is deleted. If the deleted profile was the default startup profile,
    /// clears the setting and updates the dropdown (Req 3.5).
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
            // Clear the default since the profile no longer exists (Req 3.5)
            _settingsStore.Save(settings with { DefaultStartupProfileName = null });
            _selectedStartupProfile = "None";
            OnPropertyChanged(nameof(SelectedStartupProfile));
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
}
