# Requirements Document

## Introduction

This specification covers four enhancements for Monitor Brightness Controller v1.4: silent startup mode for Windows auto-start scenarios, monitors tab initialization showing current state on first load, a comprehensive Help tab with full documentation, and an auto-update notification system using GitHub releases.

## Glossary

- **Application**: The Monitor Brightness Controller WPF desktop application.
- **Silent_Mode**: A startup mode triggered by the `--silent` command-line argument where the Application starts without displaying the main window, applies the configured startup profile (if enabled), and minimizes directly to the system tray.
- **CLI_Handler**: The component that parses and dispatches command-line arguments at application startup.
- **Startup_Registration**: The component that manages the application's presence in the Windows Run registry key for auto-start functionality.
- **Settings_Store**: The JSON-backed persistence layer that saves and loads application settings from `%LOCALAPPDATA%\MonitorBrightnessController\settings.json`.
- **Monitors_Tab**: The first tab in the application UI displaying monitor brightness and gamma controls.
- **Help_Tab**: A tab in the application UI providing complete in-app documentation of all features.
- **Update_Checker**: The component responsible for checking the GitHub releases page for newer versions of the application.
- **System_Tray**: The Windows notification area where the application icon resides when minimized.
- **Profile**: A named set of brightness and gamma values for one or more monitors.
- **GitHub_Releases_Page**: The page at `https://github.com/dlightman/monitor-brightness-controller/releases` listing published application versions.

## Requirements

### Requirement 1: Silent Startup Mode via Command-Line Argument

**User Story:** As a user who has configured auto-start with Windows, I want the application to start silently without showing the main window, so that my desktop is not interrupted by a configuration window every time I log in.

#### Acceptance Criteria

1. WHEN the Application is launched with the `--silent` command-line argument, THE Application SHALL start with its process running and no application window visible on screen.
2. WHEN the Application is launched with the `--silent` command-line argument and the `AutoApplyOnStartup` setting is true and a `DefaultStartupProfileName` is configured, THE Application SHALL apply the designated startup profile and then place its icon in the System_Tray without displaying any window.
3. WHEN the Application is launched with the `--silent` command-line argument, THE Application SHALL place its icon in the System_Tray.
4. WHEN the Application is launched without the `--silent` command-line argument and without CLI monitor/profile arguments, THE Application SHALL display the main window normally.
5. WHEN the user double-clicks the System_Tray icon while in Silent_Mode, THE Application SHALL display the main window.
6. IF the Application is launched with the `--silent` argument and profile auto-apply fails due to a missing or invalid startup profile, THEN THE Application SHALL remain minimized in the System_Tray without displaying an error window and SHALL log the failure for later viewing when the user opens the main window.
7. IF the Application is launched with `--silent` combined with other CLI arguments such as `--monitor` or `--profile`, THEN THE Application SHALL execute the CLI commands, remain in Silent_Mode without displaying the main window, and place its icon in the System_Tray.

### Requirement 2: Startup Registration Includes Silent Argument

**User Story:** As a user enabling "Start with Windows," I want the auto-start registration to include the `--silent` argument automatically, so that I do not need to manually configure silent startup.

#### Acceptance Criteria

1. WHEN the user enables "Start with Windows" in settings, THE Startup_Registration SHALL register the application in the `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` registry key with the value formatted as `"<exe_path>" --silent` (quoted executable path followed by a space and the literal `--silent` argument).
2. WHEN the user disables "Start with Windows" in settings, THE Startup_Registration SHALL remove the registry entry, completing successfully even if the entry does not exist.
3. WHEN the Application calls EnsureRegistration with StartWithWindows enabled, THE Startup_Registration SHALL verify that the registered value equals `"<current_exe_path>" --silent` and overwrite the value with the correct format if the path or the `--silent` argument differs or is missing.
4. IF the startup registry key cannot be opened or written to during any registration operation, THEN THE Startup_Registration SHALL return a failure result indicating the registry was inaccessible without modifying application state.

### Requirement 3: Monitors Tab Shows Current State on First Load

**User Story:** As a user opening the application, I want to see the current brightness state of my monitors immediately, so that I know the current settings before making adjustments.

#### Acceptance Criteria

1. WHEN the Application starts in GUI mode and a DefaultStartupProfileName is configured and the named profile exists and has been applied, THE Monitors_Tab SHALL display the applied profile's brightness value (0–100) and gamma value (0–100) for each monitor matched by device path.
2. WHEN the Application starts in GUI mode and no profiles exist in AppSettings, THE Monitors_Tab SHALL display a label "Current Settings" with the brightness (0–100) and gamma (0–100) values read from each connected monitor via DDC/CI.
3. WHEN the Application starts in GUI mode and profiles exist but AutoApplyOnStartup is false and no DefaultStartupProfileName is configured, THE Monitors_Tab SHALL display the brightness (0–100) and gamma (0–100) values read from each connected monitor via DDC/CI.
4. WHEN the Monitors_Tab reads live values from monitors, THE Application SHALL call MonitorService.DetectMonitors which queries each connected monitor's current brightness and gamma via DDC/CI.
5. IF DDC/CI communication fails for a monitor during first-load value reading, THEN THE Monitors_Tab SHALL display that monitor's entry with an error indication in place of the brightness and gamma values.
6. IF a startup profile is applied but does not contain an entry for a connected monitor, THEN THE Monitors_Tab SHALL display the live DDC/CI-read brightness and gamma values for that unmatched monitor.

### Requirement 4: Help Tab with Complete Documentation

**User Story:** As a user of the application, I want an in-app Help tab with complete documentation, so that I can learn about all features without leaving the application.

#### Acceptance Criteria

1. THE Help_Tab SHALL appear in the tab order after the Settings tab and before the About tab.
2. THE Help_Tab SHALL contain a dedicated section for each of the following application features: monitor brightness and gamma control, profiles, smooth transitions, system tray behavior, startup settings, CLI usage, silent startup mode, auto-update notifications, shortcut creation, and proper install, where each section includes a heading identifying the feature and a description of its purpose and usage.
3. WHEN the Help_Tab content exceeds the visible area, THE Help_Tab SHALL provide a vertical scrollbar allowing the user to scroll through all documentation content.
4. THE Help_Tab SHALL organize documentation into visually distinct sections with headings so that a user can locate a specific feature's documentation without reading the entire content.
5. THE Application SHALL provide external documentation (e.g., README or docs file) that covers the same feature topics listed in the Help_Tab content.
6. WHEN a new feature is added to the application, THE Help_Tab content SHALL include a section for the new feature following the same heading-and-description structure as existing sections.

### Requirement 5: Auto-Update Notification Check on Launch

**User Story:** As a user, I want to be notified when a new version of the application is available, so that I can decide whether to update.

#### Acceptance Criteria

1. WHEN the Application starts in GUI mode and the auto-update check setting is enabled, THE Update_Checker SHALL query the GitHub_Releases_Page for the latest published version asynchronously without blocking the UI thread.
2. WHEN the Update_Checker detects a version newer than the currently running version, THE Application SHALL display a non-modal notification within the main window indicating the new version number is available, and the notification SHALL remain visible until the user dismisses it or closes the window.
3. WHEN the update notification is displayed, THE Application SHALL include a clickable hyperlink that opens the GitHub_Releases_Page in the user's default browser.
4. THE Application SHALL NOT automatically download or install updates.
5. IF the Update_Checker fails to reach the GitHub_Releases_Page due to a network error or if the request exceeds a 10-second timeout, THEN THE Application SHALL silently continue startup without displaying an error to the user.
6. THE Update_Checker SHALL compare versions using semantic versioning (major.minor.patch), ignoring pre-release suffixes, to determine whether the latest published release has a higher version number than the currently running assembly version.
7. WHEN the Update_Checker queries the GitHub_Releases_Page, THE Update_Checker SHALL perform at most one query per application launch.

### Requirement 6: Auto-Update Check Setting

**User Story:** As a user, I want to control whether the application checks for updates on launch, so that I can opt out of network requests if I prefer.

#### Acceptance Criteria

1. THE Settings_Store SHALL persist an auto-update check enabled/disabled preference as a boolean `CheckForUpdatesOnStartup` property in AppSettings.
2. THE Application SHALL display a "Check for updates on startup" checkbox in the Settings tab, positioned after the existing startup-related settings.
3. WHEN the user toggles the "Check for updates on startup" checkbox, THE Settings_Store SHALL persist the new value immediately.
4. THE Application SHALL default the `CheckForUpdatesOnStartup` setting to `true` for new installations where the settings file does not yet exist.
5. WHEN the Application loads existing settings that do not contain the `CheckForUpdatesOnStartup` property (upgrade scenario), THE Application SHALL treat the missing property as `true`.

