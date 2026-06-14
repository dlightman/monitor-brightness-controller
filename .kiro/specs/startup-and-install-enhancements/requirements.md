# Requirements Document

## Introduction

This feature enhances the Monitor Brightness Controller application with three related startup and installation capabilities: reliable auto-start with Windows, a default startup profile that is applied when the application launches in GUI mode (unless overridden by command-line arguments), and a "Proper Install" button that copies the application to Program Files and updates all autostart references to use the installed path.

## Glossary

- **Application**: The Monitor Brightness Controller WPF application (MonitorBrightnessController.exe)
- **Startup_Registration**: The component that manages the application's presence in the Windows current-user Run registry key
- **Settings_Store**: The component that persists application settings to `%LOCALAPPDATA%\MonitorBrightnessController\settings.json`
- **Profile_Manager**: The component that manages named brightness/gamma profiles and applies them to monitors
- **CLI_Handler**: The component that parses and executes command-line arguments for brightness and profile operations
- **Startup_Profile**: The profile configured by the user to be automatically applied when the application starts in GUI mode
- **Installer**: The component that copies the application to the Program Files directory and updates autostart configuration
- **Install_Directory**: The target installation path: `%ProgramFiles%\MonitorBrightnessController\`
- **Run_Registry_Key**: The Windows registry key `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` used for current-user autostart
- **CLI_Invocation**: A launch where command-line arguments contain `--monitor` or `--profile` options

## Requirements

### Requirement 1: Auto-Start with Windows Registration

**User Story:** As a user, I want the application to reliably start with Windows when I enable the auto-start setting, so that my monitor brightness is managed without manual intervention after every reboot.

#### Acceptance Criteria

1. WHEN the user enables the StartWithWindows setting, THE Startup_Registration SHALL create a value in the Run_Registry_Key with the full quoted path to the currently running executable
2. WHEN the user disables the StartWithWindows setting, THE Startup_Registration SHALL remove the application's value from the Run_Registry_Key
3. THE Startup_Registration SHALL use the registry value name "MonitorBrightnessController" for the autostart entry
4. WHEN the application starts and the StartWithWindows setting is true but the Run_Registry_Key entry is missing or contains a path that differs from the current executable path using case-insensitive comparison, THE Application SHALL re-register the correct executable path in the Run_Registry_Key
5. IF the Run_Registry_Key cannot be opened for writing, THEN THE Startup_Registration SHALL return a failure result with IsSuccess false and an Error string describing the reason, and SHALL NOT modify the persisted StartWithWindows setting value
6. IF the resolved executable path is null or empty at the time of registration, THEN THE Startup_Registration SHALL return a failure result with IsSuccess false and an Error string indicating the executable path could not be determined, and SHALL NOT write to the Run_Registry_Key

### Requirement 2: Default Startup Profile

**User Story:** As a user, I want to configure a default profile that is applied automatically when the application starts, so that my preferred monitor brightness is set without requiring manual profile selection after each boot.

#### Acceptance Criteria

1. THE Settings_Store SHALL persist a nullable DefaultStartupProfileName property in the application settings JSON file
2. WHEN the user selects a profile as the default startup profile, THE Application SHALL save the profile name in the DefaultStartupProfileName setting
3. WHEN the user clears the default startup profile selection, THE Application SHALL set the DefaultStartupProfileName setting to null
4. WHEN the application starts in GUI mode and DefaultStartupProfileName is set and the command-line arguments do not contain --monitor or --profile options, THE Application SHALL apply the configured startup profile using the Profile_Manager
5. WHEN the application starts with command-line arguments containing --monitor or --profile options, THE Application SHALL skip the default startup profile application regardless of the DefaultStartupProfileName setting value
6. IF the configured DefaultStartupProfileName references a profile name that does not exist in the Profile_Manager's profile list, THEN THE Application SHALL log a warning message containing the missing profile name and continue startup without applying a profile
7. IF the default startup profile application fails because one or more monitors in the profile are not currently connected, THEN THE Application SHALL log the failure with details of which monitors were unavailable and continue startup normally
8. WHEN the default startup profile is applied successfully at startup, THE Application SHALL update the LastAppliedProfileName setting to reflect the applied profile name
9. IF persisting the DefaultStartupProfileName setting to the settings file fails, THEN THE Application SHALL display an error message indicating the setting could not be saved

### Requirement 3: Default Startup Profile UI

**User Story:** As a user, I want a UI control to select which profile is applied at startup, so that I can easily configure and change my default brightness profile.

#### Acceptance Criteria

1. THE Application SHALL display a dropdown control in the settings area that lists all available profiles (in the order they appear in the settings store) plus a "None" option as the first entry
2. IF no DefaultStartupProfileName is configured or the configured name does not match any existing profile, THEN THE Application SHALL show "None" as the selected value in the dropdown
3. WHEN the user selects a profile from the dropdown, THE Application SHALL persist the selection to the DefaultStartupProfileName setting without requiring a separate save action
4. WHEN the user selects "None" from the dropdown, THE Application SHALL set the DefaultStartupProfileName setting to null
5. WHEN a profile that is set as the default startup profile is deleted, THE Application SHALL set the DefaultStartupProfileName setting to null and update the dropdown selection to "None"
6. IF persisting the DefaultStartupProfileName setting fails, THEN THE Application SHALL revert the dropdown selection to its previous value and display an error message indicating the setting could not be saved
7. WHEN a profile is created or deleted, THE Application SHALL update the dropdown list to reflect the current set of available profiles

### Requirement 4: Proper Install to Program Files

**User Story:** As a user, I want to install the application properly into Program Files, so that the application resides in a standard Windows location and autostart references are stable across updates.

#### Acceptance Criteria

1. WHILE the application is running from a path other than the Install_Directory, THE Application SHALL display an enabled "Proper Install" button in the settings area
2. WHEN the user clicks the "Proper Install" button, THE Installer SHALL copy the running executable to the Install_Directory, overwriting any existing file at the destination
3. WHEN the copy to Install_Directory succeeds and the StartWithWindows setting is enabled, THE Installer SHALL update the Run_Registry_Key entry to reference the new executable path in the Install_Directory
4. IF the Install_Directory does not exist, THEN THE Installer SHALL create it before copying the executable
5. IF writing to the Install_Directory requires elevated privileges, THEN THE Installer SHALL request UAC elevation to complete the file copy
6. IF the UAC elevation is denied by the user, THEN THE Installer SHALL display a message indicating the install was cancelled and leave the current configuration unchanged
7. IF the file copy to Install_Directory fails for any reason other than UAC denial, THEN THE Installer SHALL display an error message indicating the nature of the failure and leave the current configuration unchanged
8. WHEN the install completes successfully, THE Installer SHALL display a confirmation message indicating the new install path
9. WHILE the application is running from the Install_Directory, THE Application SHALL disable the "Proper Install" button and display a text label indicating the application is already properly installed
10. WHEN the install completes successfully and the application was launched from a path other than the Install_Directory, THE Installer SHALL inform the user that the application should be restarted from the new location

### Requirement 5: Install Path Consistency

**User Story:** As a user, I want the autostart registry entry to always reference the correct installed executable, so that Windows can reliably launch the application after installation.

#### Acceptance Criteria

1. WHEN the application starts and the StartWithWindows setting is enabled, THE Startup_Registration SHALL compare the Run_Registry_Key value to the currently running executable path using case-insensitive string comparison
2. IF the Run_Registry_Key entry points to a path that differs from the currently running executable path (case-insensitive), THEN THE Startup_Registration SHALL update the entry to use the full quoted path of the current executable
3. WHEN the Installer updates the Run_Registry_Key entry, THE Installer SHALL use the full quoted path to the executable in the Install_Directory (e.g., "%ProgramFiles%\MonitorBrightnessController\MonitorBrightnessController.exe")
4. IF the Run_Registry_Key entry does not exist and the StartWithWindows setting is enabled, THEN THE Startup_Registration SHALL create the entry with the full quoted path of the currently running executable
