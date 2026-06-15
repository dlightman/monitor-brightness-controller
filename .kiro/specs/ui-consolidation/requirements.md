# Requirements Document

## Introduction

This feature consolidates the Monitor Brightness Controller UI by merging the Profiles tab into the Monitors tab as a compact profile strip, moving the Create Shortcut functionality to the Settings tab, reworking the startup profile configuration into a single cohesive section, adding an About tab with version and build information, and ensuring sliders sync correctly with current monitor or profile values on load and profile selection.

## Glossary

- **Application**: The Monitor Brightness Controller WPF desktop application
- **Monitors_Tab**: The primary tab displaying per-monitor brightness and gamma sliders and the inline profile strip
- **Settings_Tab**: The tab containing application preferences, startup profile configuration, shortcut creation, and install options
- **About_Tab**: The tab displaying project link, version, and build date information
- **Profile_Strip**: A compact horizontal section on the Monitors tab containing the profile dropdown and action buttons
- **Profile_Dropdown**: A ComboBox listing all saved profile names; blank (no selection) when no profiles exist
- **Startup_Profile_Section**: The unified section on the Settings tab combining auto-apply toggle and startup profile dropdown
- **Startup_Profile_Dropdown**: A ComboBox listing all saved profiles plus a "Last Used" option, selectable only when auto-apply is enabled
- **Slider**: A WPF Slider control representing brightness or gamma for a specific monitor (range 0–100)
- **Profile**: A named collection of per-monitor brightness and gamma values identified by device path
- **Assembly_Info**: Build-time metadata embedded in the assembly including version number and build date

## Requirements

### Requirement 1: Slider Synchronization on Load

**User Story:** As a user, I want the brightness and gamma sliders to reflect actual monitor values when the application starts, so that I can see the true state of my monitors immediately.

#### Acceptance Criteria

1. WHEN the Application starts without a DefaultStartupProfileName configured, THE Monitors_Tab SHALL query each detected monitor via DDC/CI and display the brightness slider and gamma slider at the hardware-reported values (0–100) for that monitor within 3 seconds of the tab becoming visible.
2. WHEN the Application starts with a DefaultStartupProfileName configured and AutoApplyOnStartup enabled, THE Monitors_Tab SHALL apply the startup profile and display each Slider at the profile-defined brightness and gamma values (0–100) for monitors present in the profile, and at the hardware-reported values for monitors not present in the profile.
3. IF the startup profile application fails for one or more monitors, THEN THE Monitors_Tab SHALL display each Slider at the current hardware-reported brightness and gamma value, falling back to hardware reads for all monitors regardless of which monitors failed profile application.
4. IF a DDC/CI hardware read fails for a monitor during startup synchronization, THEN THE Monitors_Tab SHALL display the brightness and gamma sliders for that monitor at their midpoint value (50) and show an error indicator on that monitor's panel.

### Requirement 2: Slider Synchronization on Profile Selection

**User Story:** As a user, I want sliders to preview a profile's values when I select it from the dropdown, so that I can see what the profile would set before applying it.

#### Acceptance Criteria

1. WHEN a user selects a profile from the Profile_Dropdown on the Monitors_Tab, THE Monitors_Tab SHALL immediately update each brightness Slider (range 0–100) and each gamma Slider (range 0–100) to display the values defined in the selected profile's MonitorBrightnessMap and MonitorGammaMap for each mapped monitor, without waiting for an Apply action
2. WHEN a user selects a profile that does not contain a mapping for a connected monitor, THE Slider for that unmapped monitor SHALL retain its current displayed value
3. WHEN a user selects a legacy profile that has no gamma map, THE gamma Slider for each monitor SHALL retain its current displayed value
4. WHEN the Profile_Dropdown selection is cleared (set to blank), THE Monitors_Tab SHALL restore each Slider to the current hardware-reported brightness and gamma value for that monitor
5. IF the hardware-reported value for a monitor cannot be read when the Profile_Dropdown selection is cleared, THEN THE Slider for that monitor SHALL retain its last displayed value

### Requirement 3: Profile Strip on Monitors Tab

**User Story:** As a user, I want profile management controls directly on the Monitors tab in a compact layout, so that I can manage profiles without switching tabs.

#### Acceptance Criteria

1. THE Monitors_Tab SHALL display the Profile_Strip as a compact horizontal section below the monitor slider controls, rendered as a single-row strip with the Profile_Dropdown and all buttons arranged horizontally side by side
2. THE Profile_Strip SHALL contain a Profile_Dropdown listing all saved profile names in case-insensitive alphabetical order
3. WHEN no profiles are saved, THE Profile_Dropdown SHALL display with no selection and an empty list
4. WHEN the user clicks the "Apply" button with a profile selected, THE Application SHALL apply the selected profile's brightness and gamma values to all connected monitors that are mapped in the profile, skipping any disconnected monitors
5. IF the Apply operation fails because none of the selected profile's mapped monitors are currently connected, THEN THE Application SHALL display an error message indicating that no mapped monitors are available
6. WHEN the user clicks the "Update" button with a profile selected, THE Application SHALL overwrite the selected profile's brightness and gamma mappings with the current slider values for all connected monitors
7. WHEN the user clicks the "Delete" button with a profile selected, THE Application SHALL display a confirmation dialog before deleting the selected profile
8. WHEN the user confirms deletion in the confirmation dialog, THE Application SHALL delete the selected profile and update the Profile_Dropdown to reflect the removal
9. THE Profile_Strip SHALL contain a "Save As New" button that is always enabled regardless of Profile_Dropdown selection state
10. WHEN the user clicks the "Save As New" button, THE Application SHALL open a popup input dialog prompting the user to enter a profile name of 1 to 64 characters consisting only of letters, digits, hyphens, and underscores
11. IF the user confirms a profile name in the Save As New dialog that is invalid or duplicates an existing profile name (case-insensitive), THEN THE Application SHALL display an error message indicating the validation failure and keep the dialog open
12. WHEN the user confirms a valid, unique profile name in the Save As New dialog, THE Application SHALL save a new profile mapping all connected monitors to their current brightness and gamma slider values using the entered name, and add it to the Profile_Dropdown
13. IF the user attempts to save a new profile when the maximum profile count of 50 has been reached, THEN THE Application SHALL display an error message indicating the profile limit has been reached
14. WHILE no profile is selected in the Profile_Dropdown, THE "Apply", "Update", and "Delete" buttons SHALL be disabled

### Requirement 4: Remove Profiles Tab

**User Story:** As a user, I want a streamlined interface without a separate Profiles tab, so that the UI is less cluttered now that profile management is on the Monitors tab.

#### Acceptance Criteria

1. THE Application SHALL NOT display a "Profiles" tab in the tab control
2. THE Application SHALL display exactly 3 tabs in the tab control with headers in left-to-right order: "Monitors", "Settings", "About"
3. WHEN the Application launches, THE Application SHALL display the "Monitors" tab as the selected tab

### Requirement 5: Create Shortcut on Settings Tab

**User Story:** As a user, I want to create desktop shortcuts for profiles from the Settings tab, so that shortcut creation is grouped with other configuration options.

#### Acceptance Criteria

1. THE Settings_Tab SHALL display a "Create Shortcut" section containing a profile dropdown and a "Create Shortcut" button
2. THE profile dropdown in the Create Shortcut section SHALL list all saved profile names and SHALL have no selection by default when the section is first displayed
3. WHEN the user clicks "Create Shortcut" with a profile selected, THE Application SHALL present a save-file dialog defaulting to the user's Desktop folder with the filename "Brightness - {profileName}.lnk", and upon confirmation SHALL create a Windows shortcut (.lnk file) at the chosen location that launches the Application with the argument `--profile <name>`
4. THE created shortcut SHALL set its target to the Application executable path, its arguments to `--profile <name>`, and its working directory to the executable's parent folder, so that the Application runs in CLI mode without loading the GUI
5. WHILE no profile is selected in the Create Shortcut dropdown, THE "Create Shortcut" button SHALL be disabled
6. WHEN shortcut creation completes successfully, THE Application SHALL display a status message indicating the shortcut file name that was created
7. IF shortcut creation fails due to a file-system error or unavailable COM component, THEN THE Application SHALL display an error message indicating the failure reason and SHALL NOT leave a partially written file at the target location

### Requirement 6: Unified Startup Profile Section

**User Story:** As a user, I want a single cohesive section for startup profile configuration, so that the relationship between auto-apply and profile selection is clear.

#### Acceptance Criteria

1. THE Settings_Tab SHALL display a "Startup Profile" section combining the auto-apply toggle and the Startup_Profile_Dropdown
2. THE Startup_Profile_Section SHALL contain a checkbox labeled "Auto apply profile on start"
3. WHILE the "Auto apply profile on start" checkbox is unchecked, THE Startup_Profile_Dropdown SHALL be greyed out and non-interactive
4. WHILE the "Auto apply profile on start" checkbox is checked, THE Startup_Profile_Dropdown SHALL be enabled and selectable
5. THE Startup_Profile_Dropdown SHALL list a "Last Used" option displayed in italic text as the first item, followed by all saved profile names in case-insensitive alphabetical order
6. WHEN "Last Used" is selected and the application starts with a non-null LastAppliedProfileName, THE Application SHALL apply the profile identified by LastAppliedProfileName on startup
7. WHEN a specific profile name is selected, THE Application SHALL apply that profile on startup
8. THE Startup_Profile_Dropdown SHALL persist its selection to the DefaultStartupProfileName setting across application restarts
9. WHEN a profile listed in the Startup_Profile_Dropdown is deleted, THE Startup_Profile_Dropdown SHALL reset to "Last Used" and persist that change
10. IF "Last Used" is selected and LastAppliedProfileName is null at startup, THEN THE Application SHALL skip automatic profile application and log no error
11. IF a specific profile name is selected in the Startup_Profile_Dropdown but that profile no longer exists at startup, THEN THE Application SHALL skip automatic profile application, reset the dropdown selection to "Last Used", and persist that change

### Requirement 7: About Tab

**User Story:** As a user, I want an About tab showing project information, so that I can find the version, build date, and project repository.

#### Acceptance Criteria

1. THE About_Tab SHALL be the third tab in the tab control (after Monitors_Tab and Settings_Tab)
2. WHEN the user clicks the hyperlink displayed on the About_Tab, THE Application SHALL open https://github.com/dlightman/monitor-brightness-controller in the user's default web browser
3. THE About_Tab SHALL display the current build version in the format "Major.Minor.Patch" (e.g., "1.2.0"), pulled from the assembly's Version attribute at compile time
4. THE About_Tab SHALL display the build date in the format "yyyy-MM-dd" (e.g., "2025-01-15"), pulled from the assembly's metadata at compile time
5. THE About_Tab SHALL NOT require manual updates to display correct version or build date information; both values SHALL be derived automatically from build-time sources
6. THE About_Tab SHALL display all information (version, build date, and repository hyperlink) without requiring network access or external file reads at runtime

### Requirement 8: Tab Order

**User Story:** As a user, I want a logical tab ordering, so that navigation is intuitive after the Profiles tab is removed.

#### Acceptance Criteria

1. THE Application SHALL display exactly three tabs with the following headers in left-to-right order: "Monitors", "Settings", "About"
2. THE Application SHALL NOT display a tab with the header "Help" or a tab with the header "Profiles"
3. WHEN the Application window is first displayed, THE Application SHALL select the "Monitors" tab by default
