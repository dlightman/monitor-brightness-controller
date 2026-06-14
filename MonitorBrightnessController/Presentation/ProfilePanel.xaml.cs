using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MonitorBrightnessController.Application;
using MonitorBrightnessController.Infrastructure;
using MonitorBrightnessController.Interfaces;

namespace MonitorBrightnessController.Presentation;

/// <summary>
/// Interaction logic for ProfilePanel.xaml. Provides UI for creating, applying, editing,
/// deleting profiles, and creating Windows shortcuts for profiles.
/// </summary>
public partial class ProfilePanel : UserControl
{
    private IProfileManager? _profileManager;
    private IMonitorService? _monitorService;

    public ProfilePanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Sets the dependencies after construction (called from MainWindow wiring).
    /// </summary>
    public void Initialize(IProfileManager profileManager, IMonitorService monitorService)
    {
        _profileManager = profileManager;
        _monitorService = monitorService;
    }

    private ProfilePanelViewModel? ViewModel => DataContext as ProfilePanelViewModel;

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.CreateProfile();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.DeleteSelectedProfile();
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.ApplySelectedProfile();
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.UpdateSelectedProfile();
    }

    private void CreateShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        string? profileName = ViewModel?.SelectedProfileName;
        if (string.IsNullOrEmpty(profileName))
        {
            return;
        }

        // Prompt user for save location and shortcut name
        var dialog = new SaveFileDialog
        {
            Title = "Save Profile Shortcut",
            Filter = "Shortcut (*.lnk)|*.lnk",
            FileName = $"Brightness - {profileName}.lnk",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            string exePath = GetExePath();
            string arguments = $"--profile {profileName}";
            CreateWindowsShortcut(dialog.FileName, exePath, arguments, $"Apply brightness profile: {profileName}");
            ViewModel!.SetStatus($"Shortcut created: {Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception ex)
        {
            ViewModel!.SetError($"Failed to create shortcut: {ex.Message}");
        }
    }

    private static string GetExePath()
    {
        // Get the path to the running executable
        string? processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath))
        {
            return processPath;
        }

        // Fallback
        return Path.Combine(AppContext.BaseDirectory, "MonitorBrightnessController.exe");
    }

    /// <summary>
    /// Creates a Windows .lnk shortcut file using COM IShellLink via the WScript.Shell
    /// approach (type library-free, works without Visual Studio interop assemblies).
    /// </summary>
    private static void CreateWindowsShortcut(string shortcutPath, string targetPath, string arguments, string description)
    {
        // Use the Windows Script Host COM object to create a proper .lnk file
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            throw new InvalidOperationException("WScript.Shell COM object not available.");
        }

        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            try
            {
                shortcut.TargetPath = targetPath;
                shortcut.Arguments = arguments;
                shortcut.Description = description;
                shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath) ?? "";
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
