# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

## [1.5.0] - 2026-06-20

### Fixed

- **Manual launch no longer applies startup profiles** — launching the app without `--silent` now reads and displays current hardware brightness/gamma values only, with no profile auto-apply (Req 1)
- **Silent launch correctly prioritizes DefaultStartupProfileName over LastAppliedProfileName** — startup decision logic now properly falls back to LastAppliedProfileName only when DefaultStartupProfileName is null/empty, and resets missing default profiles gracefully (Req 2)
- **"Start with Windows" registry management** — syncs externally-created entries (e.g., from installer), overwrites registry value when exe path changes, and tolerates missing entries on disable without error (Req 3)
- **Profile selection now previews values without applying to hardware** — selecting a profile from the Monitors tab dropdown loads brightness/gamma into sliders as a preview; an explicit "Apply" click is required to send values to monitors (Req 4)

### Added

- **Inno Setup installer** — full Windows installer with configurable install directory, Start Menu and Desktop shortcut options, "Start with Windows" checkbox, uninstaller registration, and upgrade support that preserves user settings (Req 5, 6)
- **Installer/app registry integration** — the installer and the in-app "Start with Windows" toggle share the same registry key and format, with the app detecting and syncing externally-created entries on startup (Req 7)
- **Build pipeline ISCC.exe integration** — `publish.ps1` now invokes the Inno Setup compiler after `dotnet publish` to produce a versioned installer executable automatically (Req 9)
- **publish.ps1 build script** — PowerShell script that orchestrates dotnet publish, Inno Setup compilation, and build output cleanup in a single command

### Changed

- **Distribution model changed to installer-only** — the build process now produces an Inno Setup installer as the sole distribution artifact; raw standalone executables are no longer placed in the builds folder (Req 8)
- **Registry entry management improved for installer compatibility** — startup path comparison on launch updates the registry entry when paths differ, and write failures are surfaced to the user without reverting settings (Req 7)

## [1.4.0] - 2026-06-15

### Added

- **Silent startup mode** — launch with `--silent` to start minimized to the system tray without showing a window; applies configured startup profile if enabled
- **Startup registration uses `--silent`** — "Start with Windows" registry entry automatically includes `--silent` so auto-start is always silent; `EnsureRegistration` corrects mismatched values
- **Monitors tab initial state** — on first load, sliders display current brightness/gamma values from the applied startup profile (matched monitors) or live DDC/CI reads (unmatched/no profile)
- **Help tab** — new in-app documentation tab (after Settings, before About) with scrollable sections covering all 10 features: Monitor Brightness & Gamma Control, Profiles, Smooth Transitions, System Tray Behavior, Startup Settings, CLI Usage, Silent Startup Mode, Auto-Update Notifications, Shortcut Creation, Proper Install
- **Auto-update notification** — on GUI startup, asynchronously checks GitHub releases for a newer version and displays a dismissible banner with a link to the release page; never downloads or installs automatically
- **Check for updates on startup setting** — persisted boolean (`CheckForUpdatesOnStartup`, defaults to `true`) with a checkbox in the Settings tab; controls whether the update check runs on launch
- **External documentation updated** — README and CHANGELOG cover all v1.4 features matching Help tab content

### Changed

- `ParsedCliArguments` extended with `Silent` property; `--silent` can appear at any position among other CLI arguments
- `Program.Main` dispatches silent mode: creates hidden window + tray without calling `Show()`; combined with `--monitor`/`--profile` executes commands first then enters silent mode
- `StartupRegistration.SetStartWithWindows(true)` writes `"<exePath>" --silent` format
- `StartupRegistration.EnsureRegistration` validates and corrects registry value to include `--silent`
- `MainWindowViewModel.Load()` populates monitor sliders from profile values or DDC/CI reads on first load
- Settings table in README updated with "Check for updates on startup" entry
- Version bumped to 1.4.0

## [1.3.0] - 2026-06-14

### Added

- **Profile Strip on Monitors tab** — compact horizontal profile management (dropdown + Apply, Update, Delete, Save As New) directly on the Monitors tab
- **Slider preview on profile selection** — selecting a profile immediately previews its brightness/gamma values on sliders without applying to hardware
- **About tab** — displays version, build date (auto-generated at compile time), and GitHub repository link
- **Unified Startup Profile section** — "Auto apply profile on start" checkbox with dropdown supporting "Last Used" and specific profile selection (Settings tab)
- **Create Shortcut section** — moved to Settings tab with profile dropdown and desktop shortcut creation via WScript.Shell COM
- **Hardware restore on deselection** — clearing the profile dropdown restores sliders to current hardware-reported values
- **DDC/CI failure handling** — monitors with failed hardware reads default to midpoint (50) with error indicator
- **Profile name input dialog** — validates 1–64 chars of `[a-zA-Z0-9_-]`, duplicate checking, 50-profile limit

### Changed

- UI consolidated from 4 tabs to 3: Monitors → Settings → About
- Removed standalone Profiles tab (functionality merged into Monitors tab Profile Strip)
- Removed Help tab (replaced by About tab with version/build info)
- `ProfileStripViewModel` replaces `ProfilePanelViewModel` with slider sync callbacks
- `StartupCoordinator.Decide` now implements full "Last Used" semantics (null DefaultStartupProfileName = apply LastAppliedProfileName)
- Startup profile dropdown resets to "Last Used" when the selected profile is deleted
- Version bumped to 1.3.0

### Removed

- `ProfilePanel.xaml`, `ProfilePanel.xaml.cs`, `ProfilePanelViewModel.cs`
- `WireProfilePanel()` and `PopulateHelp()` methods from MainWindow code-behind
- Help tab content and related wiring

## [1.2.0] - 2026-06-14

### Added

- **Default startup profile** — configure a profile to apply automatically on GUI launch (Settings tab dropdown)
- **Proper Install button** — one-click copy to Program Files with UAC elevation (Settings tab)
- **Startup registry self-healing** — the autostart registry entry is automatically reconciled on launch if the exe path has changed
- **CLI override detection** — startup profile is skipped when launched with --monitor or --profile arguments

### Changed

- Refactored `StartupRegistration` from static to instance-based with `IStartupRegistration` interface for testability
- `StartupCoordinator.Decide` now accepts a `bool isCliOverride` parameter (default: false, backward-compatible)
- `MainWindowViewModel` constructor accepts optional `IStartupRegistration` and `IApplicationInstaller` parameters

### Fixed

- Autostart registry entry now uses the correct quoted exe path after the app is moved or installed to a new location
