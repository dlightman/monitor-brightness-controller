using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Navigation;
using Microsoft.Win32;
using MonitorBrightnessController.Application;
using MonitorBrightnessController.Infrastructure;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;

namespace MonitorBrightnessController.Presentation;

/// <summary>
/// Interaction logic for MainWindow.xaml. Hosts monitors, settings, and about tabs.
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

        // Wire shortcut creation delegate (Requirements 5.3, 5.4, 5.6, 5.7)
        _viewModel.CreateShortcutFunc = CreateShortcutForProfile;

        WireProfileStrip(profileManager, monitorService);

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

    private void WireProfileStrip(IProfileManager profileManager, IMonitorService monitorService)
    {
        var profileStripViewModel = new ProfileStripViewModel(profileManager, monitorService);

        // Wire slider preview callback to MainWindowViewModel (Requirements 2.1, 2.4, 3.4)
        // Handle both profile selection (preview) and deselection (restore hardware values)
        profileStripViewModel.OnProfileSelected = profileName =>
        {
            if (profileName is null)
                _viewModel!.RestoreHardwareValues();
            else
                _viewModel!.PreviewProfile(profileName);
        };

        // Wire capture functions to read current slider values from monitor VMs (Requirements 3.6, 3.12)
        profileStripViewModel.CaptureBrightnessMap = () =>
        {
            var map = new Dictionary<string, int>();
            foreach (var monitorVm in _viewModel!.Monitors)
            {
                map[monitorVm.DevicePath] = monitorVm.Brightness;
            }
            return map;
        };

        profileStripViewModel.CaptureGammaMap = () =>
        {
            var map = new Dictionary<string, int>();
            foreach (var monitorVm in _viewModel!.Monitors)
            {
                map[monitorVm.DevicePath] = monitorVm.Gamma;
            }
            return map;
        };

        // Wire profile change notification to refresh all dropdowns (Requirements 3.8, 3.12)
        profileStripViewModel.OnProfilesChanged = () =>
        {
            _viewModel!.RefreshAllProfileDropdowns();
        };

        ProfileStripControl.DataContext = profileStripViewModel;
    }

    /// <summary>
    /// Opens the hyperlink URI in the user's default web browser (Requirement 7.2).
    /// </summary>
    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = e.Uri.AbsoluteUri,
            UseShellExecute = true
        });
        e.Handled = true;
    }

    /// <summary>
    /// Creates a Windows shortcut (.lnk) for the given profile using WScript.Shell COM.
    /// Shows a SaveFileDialog defaulting to the Desktop with the filename
    /// "Brightness - {profileName}.lnk". On success returns Result.Success; on failure
    /// cleans up any partial file and returns Result.Failure with the error description.
    /// (Requirements 5.3, 5.4, 5.6, 5.7)
    /// </summary>
    private Result<Unit> CreateShortcutForProfile(string profileName)
    {
        // Present save-file dialog defaulting to Desktop (Requirement 5.3)
        var dialog = new SaveFileDialog
        {
            Title = "Save Profile Shortcut",
            Filter = "Shortcut (*.lnk)|*.lnk",
            FileName = $"Brightness - {profileName}.lnk",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };

        if (dialog.ShowDialog(this) != true)
        {
            // User cancelled — no-op, return success so VM doesn't show an error
            return Result<Unit>.Success(Unit.Value);
        }

        string shortcutPath = dialog.FileName;

        try
        {
            string exePath = GetExePath();
            string arguments = $"--profile {profileName}";
            string workingDirectory = Path.GetDirectoryName(exePath) ?? "";

            CreateWindowsShortcut(shortcutPath, exePath, arguments, workingDirectory);

            return Result<Unit>.Success(Unit.Value);
        }
        catch (Exception ex)
        {
            // Ensure no partial file left (Requirement 5.7)
            try
            {
                if (File.Exists(shortcutPath))
                {
                    File.Delete(shortcutPath);
                }
            }
            catch
            {
                // Best-effort cleanup; don't mask the original error
            }

            return Result<Unit>.Failure($"Failed to create shortcut: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the path to the running application executable.
    /// </summary>
    private static string GetExePath()
    {
        string? processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath))
        {
            return processPath;
        }

        // Fallback
        return Path.Combine(AppContext.BaseDirectory, "MonitorBrightnessController.exe");
    }

    /// <summary>
    /// Creates a Windows .lnk shortcut file using COM WScript.Shell.
    /// Sets TargetPath, Arguments, and WorkingDirectory per Requirements 5.4.
    /// </summary>
    private static void CreateWindowsShortcut(string shortcutPath, string targetPath, string arguments, string workingDirectory)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            throw new InvalidOperationException("WScript.Shell COM object is not available on this system.");
        }

        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            try
            {
                shortcut.TargetPath = targetPath;
                shortcut.Arguments = arguments;
                shortcut.WorkingDirectory = workingDirectory;
                shortcut.Save();
            }
            finally
            {
                Marshal.FinalReleaseComObject(shortcut);
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }
}
