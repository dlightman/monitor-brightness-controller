# Requirements Document

## Introduction

This document specifies the combined bug fixes and feature enhancements for Monitor Brightness Controller v1.5. The changes are organized into three implementation waves: Wave 1 addresses startup and profile selection bugs, Wave 2 introduces an Inno Setup installer to replace the current binary-only distribution, and Wave 3 integrates the installer build into the existing build pipeline and documentation workflow.

## Glossary

- **Application**: The Monitor Brightness Controller WPF desktop application (MonitorBrightnessController.exe)
- **Manual_Launch**: Starting the Application by user action (double-click, Start Menu shortcut, taskbar pin) without the `--silent` command-line flag
- **Silent_Launch**: Starting the Application with the `--silent` command-line flag, typically triggered by the Windows auto-start registry entry
- **Auto_Start_Registry_Entry**: The HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run value named "MonitorBrightnessController" with value `"<exePath>" --silent`
- **Auto_Apply**: The behavior where the Application applies a configured startup profile to monitors immediately after launch, controlled by the AutoApplyOnStartup setting
- **Startup_Profile**: The profile configured in settings (DefaultStartupProfileName) or the last-used profile (LastAppliedProfileName) to apply during Auto_Apply
- **Profile_Selection**: The act of choosing a profile from the dropdown on the Monitors tab
- **Profile_Apply**: The act of sending a profile's brightness and gamma values to the physical monitors via DDC/CI
- **Profile_Preview**: Loading a profile's brightness and gamma values into the UI sliders without sending values to the physical monitors
- **Settings_Store**: The JSON file at %LOCALAPPDATA%\MonitorBrightnessController\settings.json
- **Installer**: The Inno Setup-based installer executable (.exe) that installs the Application
- **Monitor_Service**: The component responsible for detecting monitors and sending brightness/gamma commands via DDC/CI
- **Profile_Manager**: The component responsible for loading, saving, and applying brightness profiles

## Requirements

---

### Wave 1: Bug Fixes

---

### Requirement 1: Manual Launch Displays Hardware Values Only

**User Story:** As a user, I want the app to show my monitors' current live brightness and gamma values when I launch it manually, so that I see what my monitors are actually set to without anything being changed.

#### Acceptance Criteria

1. WHEN a Manual_Launch occurs, THE Application SHALL read the current hardware-reported brightness and gamma values (each an integer in the range 0–100) for each detected controllable monitor via the Monitor_Service and display those values in the corresponding UI sliders and text inputs
2. WHEN a Manual_Launch occurs, THE Application SHALL NOT invoke Profile_Apply for any profile
3. WHEN a Manual_Launch occurs, THE Application SHALL NOT send any SetBrightness or SetGamma commands to the Monitor_Service
4. WHEN a Manual_Launch occurs, THE Application SHALL display the Monitors tab profile dropdown with no profile selected (empty selection)
5. IF a Manual_Launch occurs and the Monitor_Service fails to read brightness or gamma for a controllable monitor via DDC/CI, THEN THE Application SHALL default that monitor's slider to 50, display the text as "unknown", disable the slider controls for that monitor, and show an error indicator on the monitor's panel

---

### Requirement 2: Silent Launch Applies Startup Profile

**User Story:** As a user, I want the app to automatically apply my configured startup profile when Windows auto-starts it (via --silent), so that my preferred monitor settings take effect without manual intervention.

#### Acceptance Criteria

1. WHEN a Silent_Launch occurs AND AutoApplyOnStartup is true AND a Startup_Profile is configured, THE Application SHALL invoke Profile_Apply for the configured Startup_Profile within 10 seconds of process start
2. WHEN a Silent_Launch occurs AND AutoApplyOnStartup is true AND DefaultStartupProfileName is set AND the named profile exists, THE Application SHALL use DefaultStartupProfileName as the Startup_Profile
3. WHEN a Silent_Launch occurs AND AutoApplyOnStartup is true AND DefaultStartupProfileName is not set AND LastAppliedProfileName is set AND the named profile exists, THE Application SHALL use LastAppliedProfileName as the Startup_Profile
4. WHEN a Silent_Launch occurs AND AutoApplyOnStartup is false, THE Application SHALL NOT invoke Profile_Apply
5. WHEN a Silent_Launch occurs AND AutoApplyOnStartup is true AND no Startup_Profile can be resolved (both DefaultStartupProfileName and LastAppliedProfileName are null or empty), THE Application SHALL NOT invoke Profile_Apply and SHALL NOT display an error
6. IF Profile_Apply fails during Silent_Launch, THEN THE Application SHALL log the failure details via Trace, store a user-facing notice for display when the window is next shown, and remain running in the system tray
7. WHEN a Silent_Launch occurs, THE Application SHALL start with the main window hidden and only the system tray icon visible, without any taskbar entry
8. IF a Silent_Launch occurs AND AutoApplyOnStartup is true AND DefaultStartupProfileName references a profile that no longer exists, THEN THE Application SHALL reset DefaultStartupProfileName to null (reverting to "Last Used" behavior), persist the change, and NOT invoke Profile_Apply
9. WHEN a Silent_Launch occurs AND the command-line arguments also contain --monitor or --profile flags, THE Application SHALL skip Startup_Profile auto-apply and execute only the explicit CLI command

---

### Requirement 3: Start With Windows Registry Management

**User Story:** As a user, I want the "Start with Windows" toggle to reliably add or remove the auto-start registry entry, so that I have consistent control over whether the app starts at login.

#### Acceptance Criteria

1. WHEN the user enables "Start with Windows" in Settings, THE Application SHALL create the Auto_Start_Registry_Entry with value `"<currentExePath>" --silent`
2. WHEN the user disables "Start with Windows" in Settings, THE Application SHALL remove the Auto_Start_Registry_Entry by deleting the "MonitorBrightnessController" value from the Run key
3. WHEN the user disables "Start with Windows" in Settings AND the Auto_Start_Registry_Entry does not exist, THE Application SHALL complete successfully without error
4. WHEN the user changes the "Start with Windows" toggle in Settings, THE Application SHALL persist the StartWithWindows boolean in the Settings_Store independently of whether the registry operation succeeds or fails
5. WHEN the Application starts AND detects an existing Auto_Start_Registry_Entry, THE Application SHALL set StartWithWindows to true in the Settings_Store and reflect the toggle as enabled in the Settings UI
6. IF the registry operation fails when the user enables "Start with Windows" (due to denied permissions or an inaccessible key), THEN THE Application SHALL display an error message indicating the registry could not be updated and SHALL NOT revert the Settings_Store value
7. WHEN the user enables "Start with Windows" in Settings AND the Auto_Start_Registry_Entry already exists with a different path, THE Application SHALL overwrite the entry with the current executable path

---

### Requirement 4: Profile Selection Previews Without Applying

**User Story:** As a user, I want selecting a profile from the Monitors tab dropdown to only preview the values in the sliders, so that I can review what the profile will do before committing to applying it.

#### Acceptance Criteria

1. WHEN the user selects a profile from the Monitors tab dropdown, THE Application SHALL perform Profile_Preview by loading the profile's brightness values (0–100) into the corresponding monitor brightness sliders and, if the profile contains a gamma map, loading the gamma values (0–100) into the corresponding monitor gamma sliders
2. WHEN the user selects a profile from the Monitors tab dropdown, THE Application SHALL NOT send SetBrightness or SetGamma commands to the Monitor_Service
3. WHEN the user selects a profile that has no gamma map (legacy profile), THE Application SHALL leave the gamma sliders at their current positions unchanged
4. WHEN the user clicks the "Apply" button after selecting a profile, THE Application SHALL invoke Profile_Apply to send the profile's brightness and gamma values to the physical monitors via the Monitor_Service
5. WHEN the user deselects a profile (clears the dropdown selection), THE Application SHALL restore the UI sliders to the current hardware-reported values by reading brightness and gamma from the Monitor_Service
6. IF the Monitor_Service fails to read hardware values when the user deselects a profile, THEN THE Application SHALL leave the affected sliders at their last displayed position and display an error message indicating which monitors could not be read

---

### Wave 2: Inno Setup Installer

---

### Requirement 5: Installer Package

**User Story:** As a user, I want a proper Windows installer for Monitor Brightness Controller, so that I get a clean installation experience with Start Menu integration and upgrade support.

#### Acceptance Criteria

1. THE Installer SHALL be an Inno Setup-compiled executable (.exe) that installs the Application
2. THE Installer SHALL default the installation directory to `{autopf}\MonitorBrightnessController` (Program Files) and allow the user to select a custom directory
3. THE Installer SHALL present a checkbox for creating a Start Menu shortcut named "Monitor Brightness Controller" targeting the installed MonitorBrightnessController.exe (default: checked)
4. THE Installer SHALL present a checkbox for creating a Desktop shortcut named "Monitor Brightness Controller" targeting the installed MonitorBrightnessController.exe (default: unchecked)
5. THE Installer SHALL present a checkbox for "Start with Windows" (default: unchecked) that creates the Auto_Start_Registry_Entry when checked
6. WHEN "Start with Windows" is selected during install, THE Installer SHALL create the Auto_Start_Registry_Entry with value `"<installedExePath>" --silent`
7. THE Installer SHALL register an uninstaller executable in Windows Programs and Features (Add/Remove Programs) with display name "Monitor Brightness Controller"
8. WHEN the uninstaller is executed, THE Installer uninstall routine SHALL remove the installed application files, shortcuts, and the Auto_Start_Registry_Entry if it exists

---

### Requirement 6: Installer Upgrade Behavior

**User Story:** As a user, I want to upgrade to new versions without reconfiguring my installation, so that updates are fast and painless.

#### Acceptance Criteria

1. WHEN a previous installation is detected, THE Installer SHALL pre-fill the installation directory with the previous installation path and allow the user to change it
2. WHEN a previous installation is detected, THE Installer SHALL preserve the previously selected shortcut options (Start Menu, Desktop) by reading the existing shortcut state and pre-checking the corresponding checkboxes
3. WHEN a previous installation is detected, THE Installer SHALL preserve the previous "Start with Windows" registry entry state by checking for the existing Auto_Start_Registry_Entry and pre-checking the checkbox accordingly
4. THE Installer SHALL use an AppId that remains consistent across versions to enable upgrade detection
5. WHEN upgrading, THE Installer SHALL close the running Application process before replacing files by sending a termination request and waiting up to 5 seconds for the process to exit
6. IF the running Application process does not exit within 5 seconds during upgrade, THEN THE Installer SHALL display a message indicating the application could not be closed and prompt the user to close it manually before continuing
7. WHEN upgrading, THE Installer SHALL NOT delete or overwrite the user settings file at %LOCALAPPDATA%\MonitorBrightnessController\settings.json

---

### Requirement 7: Installer and Application Startup Integration

**User Story:** As a user, I want the in-app "Start with Windows" toggle to work seamlessly with the installer's startup option, so that both methods manage the same registry entry.

#### Acceptance Criteria

1. THE Application SHALL use the same registry key path and value format as the Installer: HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run with name "MonitorBrightnessController" and value `"<exePath>" --silent`
2. WHEN the Application launches and detects an existing registry entry under HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run with name "MonitorBrightnessController" while the Settings_Store has StartWithWindows set to false (indicating the entry was created externally by the installer), THE Application SHALL set StartWithWindows to true in the Settings_Store and persist the change
3. WHEN the user toggles "Start with Windows" in the Application Settings, THE Application SHALL create the registry entry (if enabled) or delete the registry entry (if disabled), and update StartWithWindows in the Settings_Store, taking precedence over any prior installer-set state
4. WHEN the Application launches with StartWithWindows set to true in the Settings_Store, THE Application SHALL compare the registry entry value against the current executable path and update the registry entry to reference the current executable path if the paths differ
5. IF the Application cannot open or write to the registry key during any startup registration operation, THEN THE Application SHALL return a failure result indicating the registry key could not be accessed and preserve the existing Settings_Store value unchanged

---

### Requirement 8: Distribution Model

**User Story:** As a developer, I want a single distribution artifact (the installer), so that there is one clear way to install and update the application.

#### Acceptance Criteria

1. THE build process SHALL produce an Inno Setup installer as the sole distribution artifact, placing only the installer executable in the builds folder
2. THE build process SHALL NOT produce a separate portable standalone executable for distribution
3. THE Installer SHALL install the framework-dependent single-file published Application with runtime identifier win-x64 and SelfContained set to false
4. THE Installer script (.iss) SHALL reside in the repository root directory alongside the project source code
5. WHEN the build process completes, THE builds folder SHALL contain only the versioned installer executable and no raw application binaries or debug symbol files

---

### Wave 3: Build Integration and Documentation

---

### Requirement 9: Build Pipeline Integration

**User Story:** As a developer, I want the installer to be built automatically as part of the publish workflow, so that every release produces a ready-to-distribute installer.

#### Acceptance Criteria

1. WHEN a publish build completes successfully, THE build pipeline SHALL invoke the Inno Setup compiler (ISCC.exe) to compile the installer script and produce an installer executable named `MonitorBrightnessControllerSetup-{VERSION}.exe`
2. THE build pipeline SHALL place the produced installer in the `builds/v{VERSION}/` directory alongside the other build artifacts
3. THE existing pre-build-docs-check hook SHALL continue to validate documentation, changelog, and version settings before build execution
4. THE Inno Setup script SHALL reference the version number exclusively from the .csproj `Version` property to avoid version mismatch between the application and the installer
5. IF the Inno Setup compiler is not found in the build environment or the installer compilation fails, THEN THE build pipeline SHALL fail the build with an error message indicating the cause of the installer compilation failure

---

### Requirement 10: Documentation and Versioning

**User Story:** As a developer, I want documentation and versioning updated for v1.5.0, so that users and contributors have accurate information.

#### Acceptance Criteria

1. WHEN releasing v1.5.0, THE CHANGELOG.md SHALL contain a `## [1.5.0]` section with a release date, organized into subsections (Added, Changed, Fixed, Removed as applicable) that list all bug fixes from Requirements 1–4 under "Fixed" and all new features from Requirements 5–9 under "Added" or "Changed" as appropriate
2. WHEN releasing v1.5.0, THE .csproj SHALL have Version set to `1.5.0`, AssemblyVersion set to `1.5.0.0`, and FileVersion set to `1.5.0.0`
3. THE README.md Installation section SHALL document the Inno Setup installer as the primary installation method with download instructions, while retaining the existing "Build from source" subsection and portable single-exe option
4. THE in-app Help tab SHALL add a new section for each user-facing feature introduced in Requirements 5–9 that is not already covered, and update existing sections whose behavior has changed in v1.5.0
5. THE README.md Features list and Usage sections SHALL be updated to describe any new capabilities or changed behaviors introduced in v1.5.0, consistent with the CHANGELOG entries
