# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

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
