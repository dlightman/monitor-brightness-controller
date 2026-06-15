# Monitor Brightness Controller

A lightweight Windows 11 desktop application that controls external monitor brightness and gamma via DDC/CI. Provides a graphical interface with per-monitor sliders and a command-line interface for automation and keyboard shortcuts.

Built with C# / .NET 8 / WPF. Distributed as a single executable.

![Monitors Tab](docs/screenshots/monitors-tab.png?v=2)

## Features

- **Per-monitor brightness control** — individual sliders for each DDC/CI-capable external monitor
- **Per-monitor gamma control** — adjust gamma (VCP code 0x12) alongside brightness for each monitor
- **Named profiles** — save, apply, update, and delete brightness and gamma presets (e.g. "Gaming", "Working")
- **Backward-compatible profiles** — existing brightness-only profiles continue to work without modification
- **CLI mode** — set brightness, gamma, or apply profiles via command-line arguments (no GUI shown)
- **Silent startup mode** — launch with `--silent` to start minimized to the system tray without showing a window
- **Auto-update notifications** — checks GitHub for newer versions on startup and shows a dismissible notification
- **In-app Help tab** — complete documentation of all features accessible without leaving the application
- **Windows shortcut creation** — generate desktop shortcuts for any saved profile (Settings tab)
- **System tray** — optional minimize-to-tray with double-click restore and context menu
- **Smooth transitions** — optional animated brightness and gamma fade between values (independent per setting per monitor)
- **Start with Windows** — optional auto-launch on login (now launches in silent mode automatically)
- **Startup registry self-healing** — autostart path auto-corrects if the exe is moved
- **Unified startup profile** — choose "Last Used" or a specific profile to apply on every GUI launch
- **Proper Install** — one-click copy to Program Files with UAC elevation
- **Refresh on focus** — re-reads hardware brightness and gamma when the window regains focus
- **Single-file exe** — one portable file, runs from any location

## Screenshots

| Monitors | Settings |
|----------|----------|
| ![Monitors](docs/screenshots/monitors-tab.png?v=2) | ![Settings](docs/screenshots/settings-tab.png) |

## Prerequisites

- Windows 11 (or Windows 10 with .NET 8)
- [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (x64)
- External monitors with DDC/CI enabled (check your monitor's OSD menu)

## Installation

### Download

Download the latest `MonitorBrightnessController.exe` from [Releases](../../releases) and place it anywhere on your system.

### Build from source

```bash
git clone https://github.com/dlightman/monitor-brightness-controller.git
cd monitor-brightness-controller
dotnet publish MonitorBrightnessController/MonitorBrightnessController.csproj -c Release
```

The single executable is produced at:
```
MonitorBrightnessController/bin/Release/net8.0-windows/win-x64/publish/MonitorBrightnessController.exe
```

## Usage

### GUI Mode

Launch without arguments:

```
MonitorBrightnessController.exe
```

- **Monitors tab** — drag sliders or type values (0–100) to adjust brightness and gamma for each monitor; inline Profile Strip for saving, applying, updating, and deleting presets. On first load, sliders show current monitor state (from the applied startup profile or live DDC/CI reads).
- **Settings tab** — configure startup profile, create desktop shortcuts, and set application behavior (transitions, tray, auto-start, update checks)
- **Help tab** — complete in-app documentation covering all features
- **About tab** — version, build date, and project repository link

### CLI Mode

Pass arguments to control brightness and gamma without showing the GUI:

```bash
# Set brightness for a monitor
MonitorBrightnessController.exe --monitor 1 --brightness 70

# Set gamma for a monitor
MonitorBrightnessController.exe --monitor 1 --gamma 50

# Set both brightness and gamma (in any order)
MonitorBrightnessController.exe --monitor 1 --brightness 70 --gamma 50
MonitorBrightnessController.exe --monitor 1 --gamma 50 --brightness 70

# Multiple monitors in one command
MonitorBrightnessController.exe --monitor 1 --brightness 100 --gamma 60 --monitor 2 --brightness 50 --gamma 40

# Apply a saved profile (restores both brightness and gamma)
MonitorBrightnessController.exe --profile Gaming

# Mix brightness-only and gamma-only across monitors
MonitorBrightnessController.exe --monitor 1 --brightness 80 --monitor 2 --gamma 60

# Start in silent mode (minimize to system tray, no window shown)
MonitorBrightnessController.exe --silent

# Combine silent mode with profile apply
MonitorBrightnessController.exe --silent --profile Gaming

# Combine silent mode with monitor commands
MonitorBrightnessController.exe --silent --monitor 1 --brightness 70
```

**Monitor identifiers**: use the index number (1, 2, 3...) or the monitor name (case-insensitive).

**Options within a `--monitor` group**: at least one of `--brightness` or `--gamma` is required. Both are optional individually but the group cannot be empty.

**Exit codes**: `0` = success, `1` = one or more operations failed (details on stderr).

**Partial failure**: all monitor commands are attempted even if some fail. Each failure is reported to stderr.

### Keyboard Shortcuts

The easiest approach:
1. Go to the **Settings** tab → **Create Shortcut** section
2. Select a profile and click **Create Shortcut**
3. Save the `.lnk` file (e.g. to Desktop)
4. Right-click the shortcut → Properties → **Shortcut key** → assign a hotkey (e.g. Ctrl+Alt+G)

Manual approach:
```
Target: C:\path\to\MonitorBrightnessController.exe --profile Gaming
```

> **Note**: Do not wrap arguments in quotes. If the exe path has spaces, quote only the path:
> ```
> "C:\My Programs\MonitorBrightnessController.exe" --profile Working
> ```

## Gamma Control

Gamma is controlled via DDC/CI VCP code 0x12 (Video Gain). The application reads the monitor's reported maximum for this code and normalizes it to a 0–100 percentage, matching the brightness range.

**Key behaviors:**
- Gamma slider appears below the brightness slider for each DDC/CI-capable monitor
- If a monitor doesn't support VCP code 0x12, the gamma slider is disabled
- Profiles store gamma alongside brightness — applying a profile restores both
- Legacy profiles (created before gamma support) continue to work: only brightness is applied
- Brightness and gamma are applied independently: a failure in one does not block the other
- Smooth transitions run independently per setting per monitor

## Settings

All settings are in the **Settings** tab and saved automatically to:
```
%LOCALAPPDATA%\MonitorBrightnessController\settings.json
```

| Setting | Default | Description |
|---------|---------|-------------|
| Auto apply profile on start | Off | Applies a profile automatically when the app launches |
| Startup profile | Last Used | Which profile to apply on startup ("Last Used" or a specific profile) |
| Minimize to system tray | On | Hides to tray on minimize/close instead of taskbar |
| Smooth transitions | Off | Fades brightness and gamma gradually instead of jumping |
| Transition duration | 500ms | Duration of smooth transitions (100–2000ms) |
| Start with Windows | Off | Auto-launches the app on login in silent mode (registry path self-heals on move) |
| Check for updates on startup | On | Checks GitHub for newer versions when the app launches |
| Refresh on window focus | On | Re-reads hardware brightness and gamma when the window is activated |
| Proper Install | — | Copies the exe to Program Files and updates autostart path |

## System Tray (when enabled)

- Minimize or close → hides to system tray
- Double-click tray icon → restore window
- Right-click tray icon → Restore or Exit
- Exit saves settings and terminates the process

## Silent Startup Mode

Launch with `--silent` to start the application without displaying the main window:

```
MonitorBrightnessController.exe --silent
```

In silent mode:
- The application minimizes directly to the system tray
- The configured startup profile is applied (if enabled)
- Double-click the tray icon to show the main window
- If profile application fails, the error is logged and viewable when the window is opened

When "Start with Windows" is enabled in Settings, the auto-start registry entry automatically includes `--silent`, so the application launches silently on login without any additional configuration.

The `--silent` flag can be combined with other CLI arguments:
- `--silent --profile Gaming` — applies the profile and enters silent mode
- `--silent --monitor 1 --brightness 70` — sets brightness and enters silent mode

## Auto-Update Notifications

On each GUI launch (when enabled), the application checks GitHub for newer versions:
- If a newer version is found, a notification banner appears at the top of the main window
- The banner includes the new version number and a clickable link to the release page
- The notification is non-modal and does not block any UI interaction
- Click the dismiss button to hide the notification for the current session
- The application never downloads or installs updates automatically

Disable automatic update checks by unchecking "Check for updates on startup" in the Settings tab. Network failures or timeouts (>10 seconds) are handled silently — no error is shown to the user.

## Troubleshooting

**No monitors detected**
- Enable DDC/CI in your monitor's OSD settings (often under System → DDC/CI)
- Laptop built-in displays do not support DDC/CI — only external monitors
- Some KVM switches or docking stations block DDC/CI signals

**Gamma slider disabled**
- Your monitor may not support VCP code 0x12 (Video Gain)
- Check your monitor's DDC/CI capabilities in its OSD or documentation
- The brightness slider will still work independently

**CLI shortcut not changing brightness/gamma**
- Ensure arguments are not wrapped in a single quoted string
- Test from a command prompt: `MonitorBrightnessController.exe --monitor 1 --brightness 50`
- Verify the monitor index matches (run the GUI to see the assigned indices)

**System tray icon not showing**
- Check that "Minimize to system tray" is enabled in the Settings tab
- The tray icon only appears after you minimize or close the window

## Project Structure

```
├── MonitorBrightnessController/        # Main WPF application
│   ├── Application/                    # Business logic (MonitorService, ProfileManager, CliHandler, TransitionRunner)
│   ├── Infrastructure/                 # DDC/CI interop, settings persistence, startup registration
│   ├── Interfaces/                     # Service interfaces
│   ├── Models/                         # Data models (MonitorState, Profile, AppSettings, Result<T>)
│   ├── Presentation/                   # WPF views, view models, system tray
│   └── Assets/                         # Application icon
├── MonitorBrightnessController.Tests/  # xUnit + FsCheck property-based tests
├── tools/                              # Icon generation tool
├── docs/screenshots/                   # Application screenshots
└── MonitorBrightnessController.sln     # Solution file
```

## Development

```bash
# Build
dotnet build MonitorBrightnessController.sln

# Run tests (including property-based tests with 100 iterations each)
dotnet test MonitorBrightnessController.sln

# Publish single-file exe
dotnet publish MonitorBrightnessController/MonitorBrightnessController.csproj -c Release
```

Requires only the .NET 8 SDK. No Visual Studio or additional tools needed.

## License

MIT License — see [LICENSE](LICENSE) for details.
