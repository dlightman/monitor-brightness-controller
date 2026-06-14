using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using Microsoft.Win32;
using MonitorBrightnessController.Application;
using MonitorBrightnessController.Infrastructure;
using MonitorBrightnessController.Interfaces;

namespace MonitorBrightnessController.Presentation;

/// <summary>
/// Interaction logic for MainWindow.xaml. Hosts monitors, profiles, settings, and help.
/// Manages system tray lifecycle based on the MinimizeToTray setting.
/// </summary>
public partial class MainWindow : Window
{
    private SystemTrayManager? _trayManager;
    private MainWindowViewModel? _viewModel;

    public MainWindow(IMonitorService monitorService)
    {
        InitializeComponent();
        var settingsStore = new SettingsStore();
        var profileManager = new ProfileManager(settingsStore);
        Setup(monitorService, settingsStore, profileManager);
    }

    public MainWindow(
        IMonitorService monitorService,
        ISettingsStore settingsStore,
        IProfileManager profileManager)
    {
        InitializeComponent();
        Setup(monitorService, settingsStore, profileManager);
    }

    public MainWindow()
        : this(new MonitorService(new MonitorInterop()))
    {
    }

    private void Setup(IMonitorService monitorService, ISettingsStore settingsStore, IProfileManager profileManager)
    {
        // Wire real implementations for startup registration and install (Requirements 1.4, 5.1, 5.2, 5.4)
        var registryWrapper = new RegistryKeyWrapper(Registry.CurrentUser);
        var startupRegistration = new StartupRegistration(registryWrapper);
        var applicationInstaller = new ApplicationInstaller();

        _viewModel = new MainWindowViewModel(monitorService, settingsStore, profileManager, startupRegistration, applicationInstaller);
        DataContext = _viewModel;

        WireProfilePanel(profileManager, monitorService);
        PopulateHelp();

        // Initialize system tray based on the current setting
        if (_viewModel.MinimizeToTray)
        {
            EnableTray();
        }

        // React to setting changes
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.MinimizeToTray))
        {
            if (_viewModel!.MinimizeToTray)
            {
                EnableTray();
            }
            else
            {
                DisableTray();
            }
        }
    }

    /// <inheritdoc/>
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);

        if (_viewModel is not null && _viewModel.RefreshOnFocus)
        {
            _viewModel.RefreshMonitorValues();
        }
    }

    private void EnableTray()
    {
        if (_trayManager is not null)
        {
            return;
        }

        _trayManager = new SystemTrayManager(this, saveState: null);
    }

    private void DisableTray()
    {
        _trayManager?.Dispose();
        _trayManager = null;
    }

    private void WireProfilePanel(IProfileManager profileManager, IMonitorService monitorService)
    {
        IReadOnlyDictionary<string, int> CaptureBrightnessMap()
        {
            var detected = monitorService.DetectMonitors();
            return detected
                .Where(m => m.IsControllable && m.CurrentBrightness.HasValue)
                .ToDictionary(m => m.DevicePath, m => m.CurrentBrightness!.Value);
        }

        var profilePanel = new ProfilePanel();
        profilePanel.Initialize(profileManager, monitorService);
        profilePanel.DataContext = new ProfilePanelViewModel(profileManager, CaptureBrightnessMap, monitorService);
        ProfilePanelHost.Content = profilePanel;
    }

    private void PopulateHelp()
    {
        HelpContent.Inlines.Clear();

        HelpContent.Inlines.Add(new Bold(new Run("Monitor Brightness Controller\n")) { FontSize = 16 });
        HelpContent.Inlines.Add(new Run("\n"));

        HelpContent.Inlines.Add(new Bold(new Run("GUI Usage\n")));
        HelpContent.Inlines.Add(new Run(
            "• The Monitors tab shows one control group per detected DDC/CI monitor\n" +
            "• Each monitor has a Brightness slider (0–100) and a Gamma slider (0–100)\n" +
            "• Drag the slider or type a value in the text box and press Enter\n" +
            "• Changes are applied immediately to the monitor via DDC/CI\n" +
            "• Gamma controls VCP code 0x12 (Video Gain) — if your monitor does not\n" +
            "  support this code, the gamma slider will be disabled\n\n"));

        HelpContent.Inlines.Add(new Bold(new Run("Profiles\n")));
        HelpContent.Inlines.Add(new Run(
            "• Go to the Profiles tab to create, apply, edit, and delete profiles\n" +
            "• Creating a profile saves both brightness and gamma for all connected monitors\n" +
            "• Apply restores brightness and gamma levels to connected monitors\n" +
            "• Update overwrites the selected profile with current brightness and gamma levels\n" +
            "• Create Shortcut makes a Windows .lnk for one-click/hotkey profile apply\n" +
            "• Profile names: 1–64 characters, letters/digits/hyphens/underscores only\n" +
            "• Legacy profiles (created before gamma support) still work — only brightness is applied\n\n"));

        HelpContent.Inlines.Add(new Bold(new Run("CLI Mode (for keyboard shortcuts)\n")));
        HelpContent.Inlines.Add(new Run(
            "Set brightness and/or gamma directly:\n" +
            "  MonitorBrightnessController.exe --monitor 1 --brightness 70\n" +
            "  MonitorBrightnessController.exe --monitor 1 --gamma 50\n" +
            "  MonitorBrightnessController.exe --monitor 1 --brightness 70 --gamma 50\n" +
            "  MonitorBrightnessController.exe --monitor 1 --brightness 100 --monitor 2 --brightness 50\n\n" +
            "Apply a saved profile (restores both brightness and gamma):\n" +
            "  MonitorBrightnessController.exe --profile focus\n\n" +
            "Both --brightness and --gamma are optional within a --monitor group,\n" +
            "but at least one must be specified.\n\n" +
            "Monitor identifier: use index (1, 2, 3) or name (case-insensitive)\n\n"));

        HelpContent.Inlines.Add(new Bold(new Run("Creating Windows Keyboard Shortcuts\n")));
        HelpContent.Inlines.Add(new Run(
            "The easiest way: select a profile in the Profiles tab and click Create Shortcut.\n" +
            "Then right-click the shortcut → Properties → Shortcut key → assign a hotkey.\n\n" +
            "Manual method:\n" +
            "1. Right-click desktop → New → Shortcut\n" +
            "2. Set Target to the exe path followed by arguments:\n" +
            "   C:\\path\\to\\MonitorBrightnessController.exe --profile focus\n" +
            "3. Right-click shortcut → Properties → Shortcut key → assign a hotkey\n\n"));

        HelpContent.Inlines.Add(new Bold(new Run("Settings\n")));
        HelpContent.Inlines.Add(new Run(
            "• Auto-apply on startup: restores your last-used profile when the app starts\n" +
            "• Default startup profile: choose a specific profile to apply every launch\n" +
            "  (overrides auto-apply; skipped when launched with --monitor or --profile args)\n" +
            "• Minimize to tray: hides to system tray on minimize/close (disable to use taskbar)\n" +
            "• Smooth transitions: animates brightness and gamma changes over a configurable duration\n" +
            "• Transitions run independently per monitor and per setting (brightness/gamma)\n" +
            "• Start with Windows: auto-launches the app on login; the registry entry\n" +
            "  auto-heals if the exe is moved or installed to a new location\n" +
            "• Proper Install: copies the app to Program Files for a standard install location;\n" +
            "  updates the autostart registry entry automatically\n" +
            "• Settings are saved at %LOCALAPPDATA%\\MonitorBrightnessController\\settings.json\n\n"));

        HelpContent.Inlines.Add(new Bold(new Run("System Tray (when enabled)\n")));
        HelpContent.Inlines.Add(new Run(
            "• Minimize or close → hides to system tray\n" +
            "• Double-click tray icon → restore window\n" +
            "• Right-click tray icon → Restore or Exit\n" +
            "• Exit saves settings and terminates\n\n"));

        HelpContent.Inlines.Add(new Bold(new Run("Troubleshooting\n")));
        HelpContent.Inlines.Add(new Run(
            "• No monitors detected? Enable DDC/CI in your monitor's OSD settings menu\n" +
            "• Laptop built-in displays do not support DDC/CI (external only)\n" +
            "• Gamma slider disabled? Your monitor may not support VCP code 0x12\n" +
            "• CLI shortcut not working? Ensure arguments are not wrapped in one quoted string\n" +
            "• Test from cmd first: MonitorBrightnessController.exe --monitor 1 --brightness 50\n\n"));

        HelpContent.Inlines.Add(new Bold(new Run("Prerequisites\n")));
        HelpContent.Inlines.Add(new Run(
            "• Windows 11\n" +
            "• .NET 8 Runtime (x64)\n" +
            "• External monitors with DDC/CI enabled\n"));
    }
}
