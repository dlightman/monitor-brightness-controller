using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;

namespace MonitorBrightnessController.Presentation;

/// <summary>
/// Manages system tray integration for the application's main window using
/// <c>H.NotifyIcon.Wpf</c>. Provides minimize-to-tray, close-to-tray, a context
/// menu with Restore/Exit options, and double-click-to-restore behaviour.
/// </summary>
/// <remarks>
/// This type is intentionally decoupled from the settings/persistence layer. The
/// caller supplies an optional <see cref="Action"/> that is invoked when the user
/// chooses Exit, allowing startup wiring (see task 10) to persist state without the
/// tray manager taking a direct dependency on the settings store.
/// Satisfies Requirements 6.1, 6.2, 6.3, 6.4 and 6.5.
/// </remarks>
public sealed class SystemTrayManager : IDisposable
{
    /// <summary>The tooltip / application name shown for the tray icon (Requirement 6.1).</summary>
    private const string ApplicationName = "Monitor Brightness Controller";

    private readonly System.Windows.Window _window;
    private readonly Action? _saveState;
    private readonly TaskbarIcon _trayIcon;

    /// <summary>
    /// When <c>true</c>, the window's <see cref="System.Windows.Window.Closing"/> handler
    /// allows the process to terminate instead of hiding to the tray. Set only by an
    /// explicit Exit request (Requirement 6.4).
    /// </summary>
    private bool _allowClose;

    private bool _disposed;

    /// <summary>
    /// Creates the tray manager for the supplied window.
    /// </summary>
    /// <param name="window">The main application window to manage.</param>
    /// <param name="saveState">
    /// Optional callback invoked when the user selects Exit, before the process terminates.
    /// Used to persist current state to the settings store (Requirement 6.4).
    /// </param>
    public SystemTrayManager(System.Windows.Window window, Action? saveState = null)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _saveState = saveState;

        _trayIcon = new TaskbarIcon
        {
            // Tooltip shows the application name (Requirement 6.1).
            ToolTipText = ApplicationName,
            Visibility = Visibility.Visible,
            // Use the embedded application icon for the system tray.
            IconSource = new System.Windows.Media.Imaging.BitmapImage(
                new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute)),
        };

        _trayIcon.ContextMenu = BuildContextMenu();

        // Double-clicking the tray icon restores the window (Requirement 6.2).
        _trayIcon.TrayMouseDoubleClick += OnTrayDoubleClick;

        // Hook window lifecycle events for minimize-to-tray and close-to-tray.
        _window.StateChanged += OnWindowStateChanged;
        _window.Closing += OnWindowClosing;

        // Ensure the icon is materialised even though the control is not in a visual tree.
        _trayIcon.ForceCreate();
    }

    /// <summary>
    /// Builds the right-click context menu offering Restore and Exit (Requirement 6.3).
    /// </summary>
    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        var restoreItem = new MenuItem { Header = "Restore" };
        restoreItem.Click += (_, _) => RestoreWindow();

        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => ExitApplication();

        menu.Items.Add(restoreItem);
        menu.Items.Add(exitItem);
        return menu;
    }

    /// <summary>
    /// Hides the window from the taskbar and keeps the tray icon visible when the window is
    /// minimized (Requirement 6.1).
    /// </summary>
    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (_window.WindowState == WindowState.Minimized)
        {
            HideToTray();
        }
    }

    /// <summary>
    /// Intercepts the window close button so the application hides to the tray instead of
    /// terminating, unless an explicit Exit was requested (Requirements 6.4, 6.5).
    /// </summary>
    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    /// <summary>
    /// Hides the window from the taskbar while keeping the tray icon visible.
    /// </summary>
    private void HideToTray()
    {
        _window.ShowInTaskbar = false;
        _window.Hide();
        _trayIcon.Visibility = Visibility.Visible;
    }

    private void OnTrayDoubleClick(object? sender, RoutedEventArgs e) => RestoreWindow();

    /// <summary>
    /// Restores the window from the tray: shows it, returns it to a normal state, re-adds it
    /// to the taskbar, and brings it to the foreground (Requirement 6.2).
    /// </summary>
    public void RestoreWindow()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.ShowInTaskbar = true;
        _window.Activate();
    }

    /// <summary>
    /// Saves state, removes the tray icon, and terminates the process (Requirement 6.4).
    /// </summary>
    public void ExitApplication()
    {
        _saveState?.Invoke();

        // Allow the upcoming window close to proceed without re-hiding to the tray.
        _allowClose = true;

        Dispose();

        System.Windows.Application.Current?.Shutdown();
    }

    /// <summary>
    /// Disposes the tray icon and detaches window event handlers.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _window.StateChanged -= OnWindowStateChanged;
        _window.Closing -= OnWindowClosing;
        _trayIcon.TrayMouseDoubleClick -= OnTrayDoubleClick;

        _trayIcon.Dispose();
    }
}
