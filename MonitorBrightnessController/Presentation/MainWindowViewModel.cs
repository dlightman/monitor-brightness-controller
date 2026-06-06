using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
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

    private bool _autoApplyOnStartup;
    private bool _minimizeToTray;
    private bool _smoothTransition;
    private int _transitionDurationMs = 500;
    private bool _startWithWindows;
    private bool _refreshOnFocus = true;
    private string? _startupNotice;

    /// <summary>
    /// Creates the main window view model with only a monitor service. No startup auto-apply
    /// is performed and the auto-apply toggle is not persisted; used by the designer and by
    /// tests that exercise monitor display in isolation.
    /// </summary>
    /// <param name="monitorService">The monitor service used to detect monitors and apply brightness.</param>
    public MainWindowViewModel(IMonitorService monitorService)
    {
        _monitorService = monitorService ?? throw new ArgumentNullException(nameof(monitorService));
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
    public MainWindowViewModel(
        IMonitorService monitorService,
        ISettingsStore settingsStore,
        IProfileManager profileManager)
    {
        _monitorService = monitorService ?? throw new ArgumentNullException(nameof(monitorService));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));

        // Seed the toggles from persisted settings (defaults applied on first use).
        var loadedSettings = _settingsStore.Load();
        _autoApplyOnStartup = loadedSettings.AutoApplyOnStartup;
        _minimizeToTray = loadedSettings.MinimizeToTray;
        _smoothTransition = loadedSettings.SmoothTransition;
        _transitionDurationMs = loadedSettings.TransitionDurationMs;
        _startWithWindows = loadedSettings.StartWithWindows;
        _refreshOnFocus = loadedSettings.RefreshOnFocus;

        // Perform startup auto-apply coordination before reading current values
        // (Requirements 5.3, 5.4, 5.6, 5.7). Any notice is surfaced in the GUI.
        var coordinator = new StartupCoordinator(_settingsStore, _profileManager, _monitorService);
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

            StartupRegistration.SetStartWithWindows(value);
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
    /// Detects monitors and rebuilds the <see cref="Monitors"/> collection. Each control view
    /// model commits brightness changes through <see cref="IMonitorService.SetBrightness"/>.
    /// </summary>
    public void Load()
    {
        Monitors.Clear();

        IReadOnlyList<MonitorState> detected = _monitorService.DetectMonitors();
        foreach (MonitorState state in detected)
        {
            var vm = new MonitorControlViewModel(state, _monitorService.SetBrightness)
            {
                SmoothTransitionEnabled = _smoothTransition,
                TransitionDurationMs = _transitionDurationMs
            };
            Monitors.Add(vm);
        }

        OnPropertyChanged(nameof(HasNoControllableMonitors));
    }

    /// <summary>
    /// Re-reads brightness from hardware for each monitor without full re-detection.
    /// Updates view model values to reflect any external changes.
    /// </summary>
    public void RefreshMonitorValues()
    {
        foreach (var vm in Monitors)
        {
            var result = _monitorService.GetBrightness(vm.MonitorIndex);
            if (result.IsSuccess)
            {
                vm.Brightness = result.Value;
            }
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
