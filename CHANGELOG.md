# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

<!-- Next build: v1.3 -->

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
