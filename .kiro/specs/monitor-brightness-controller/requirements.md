# Requirements Document

## Introduction

Monitor Brightness Controller is a Windows 11 desktop application built with C# / .NET 8 / WPF that allows users to control the brightness of all connected external monitors independently. The application communicates with monitors via DDC/CI protocol using Windows monitor configuration APIs. It provides both a graphical user interface with per-monitor sliders and a command-line interface for automation and keyboard shortcut integration. The application is distributed as a single executable with minimal dependencies.

## Glossary

- **Application**: The Monitor Brightness Controller desktop application
- **Monitor**: An external display connected to the system that supports DDC/CI communication
- **DDC_CI**: Display Data Channel / Command Interface — a protocol for communication between a computer and a connected display
- **Brightness_Value**: An integer percentage from 0 to 100 representing the luminance level of a monitor
- **Monitor_Index**: A numeric identifier (1, 2, 3, ...) assigned to each detected external monitor based on enumeration order
- **Monitor_Name**: A human-readable friendly name reported by the monitor (e.g., "DELL U2723QE")
- **Profile**: A named preset that maps each monitor to a specific Brightness_Value
- **GUI**: The WPF-based graphical user interface of the Application
- **CLI**: The command-line interface mode of the Application, activated by passing arguments at launch
- **System_Tray**: The Windows notification area where the Application can reside when minimized
- **Settings_Store**: A persistent file on disk where profiles, preferences, and last-used state are saved between sessions

## Requirements

### Requirement 1: Monitor Detection and Enumeration

**User Story:** As a user, I want the application to detect all connected external monitors that support DDC/CI, so that I can control their brightness.

#### Acceptance Criteria

1. WHEN the Application starts, THE Application SHALL enumerate all connected external monitors that support DDC_CI and assign each a unique Monitor_Index starting at 1, using a deterministic ordering based on the Windows device path.
2. WHEN the Application starts, THE Application SHALL retrieve the Monitor_Name for each detected Monitor from the display's EDID data. IF the EDID data does not contain a valid name, THE Application SHALL use the string "Monitor <Monitor_Index>" as a fallback.
3. IF a connected external monitor does not support DDC_CI, THEN THE Application SHALL exclude the monitor from the controllable list and display a notice in the GUI identifying the unsupported monitor by its device path.
4. WHEN a monitor is detected, THE Application SHALL read the current Brightness_Value from the monitor via DDC_CI. IF the read fails, THE Application SHALL display the monitor in the list with a Brightness_Value of "unknown" and disable its controls until communication is restored.
5. IF no external monitors supporting DDC_CI are detected, THEN THE Application SHALL display an informational message indicating that no controllable monitors were found.

### Requirement 2: GUI Brightness Control

**User Story:** As a user, I want a simple graphical interface showing each monitor with a slider and text input, so that I can adjust brightness visually.

#### Acceptance Criteria

1. THE GUI SHALL display one control group per detected Monitor, showing the Monitor_Index and Monitor_Name as a label.
2. THE GUI SHALL provide a slider control for each Monitor that allows setting the Brightness_Value from 0 to 100 in integer increments of 1.
3. THE GUI SHALL provide a numeric text input for each Monitor that allows typing a Brightness_Value from 0 to 100.
4. WHEN the user moves a slider, THE GUI SHALL update the corresponding text input to reflect the new Brightness_Value.
5. WHEN the user types a valid integer value between 0 and 100 in the text input and commits the entry, THE GUI SHALL update the corresponding slider to reflect the new Brightness_Value.
6. WHEN the user releases the slider at a new position or commits a valid value in the text input, THE Application SHALL send the new Brightness_Value to the corresponding Monitor via DDC_CI.
7. IF the user enters a non-integer value or a value outside the range 0 to 100 in the text input, THEN THE GUI SHALL prevent the value from being committed, retain the previous valid Brightness_Value in the text input and slider, and display a validation error message adjacent to the control group.
8. IF the Application fails to set the Brightness_Value on a Monitor via DDC_CI, THEN THE GUI SHALL display an error message identifying the affected Monitor, and revert the slider and text input to the last successfully applied Brightness_Value for that Monitor.
9. IF no monitors supporting DDC_CI are detected, THEN THE GUI SHALL display an informational message indicating that no controllable monitors were found.

### Requirement 3: CLI Direct Brightness Control

**User Story:** As a user, I want to set monitor brightness via command-line arguments, so that I can create keyboard shortcuts for quick adjustments.

#### Acceptance Criteria

1. WHEN the Application is launched with `--monitor <identifier> --brightness <value>` arguments, THE Application SHALL set the Brightness_Value for the specified Monitor via DDC_CI, exit without displaying the GUI, and return exit code 0.
2. THE CLI SHALL accept both Monitor_Index and Monitor_Name as the `<identifier>` parameter for the `--monitor` argument, using case-insensitive exact matching for Monitor_Name values.
3. THE CLI SHALL support multiple `--monitor <identifier> --brightness <value>` pairs in a single invocation to set brightness on multiple monitors sequentially.
4. IF the specified Monitor identifier does not match any detected Monitor, THEN THE CLI SHALL write an error message to standard error indicating the unrecognized identifier and exit with exit code 1.
5. IF the specified Brightness_Value is not an integer between 0 and 100, THEN THE CLI SHALL write a validation error message to standard error indicating the invalid value and exit with exit code 1.
6. IF the DDC_CI communication fails when applying a Brightness_Value to a Monitor, THEN THE CLI SHALL write an error message to standard error identifying the affected Monitor and exit with exit code 1.
7. IF multiple monitor-brightness pairs are specified and one or more pairs fail while others succeed, THEN THE CLI SHALL apply brightness to all reachable monitors, write an error message to standard error for each failed Monitor, and exit with exit code 1.

### Requirement 4: CLI Named Profiles

**User Story:** As a user, I want to define named brightness profiles, so that I can switch between presets (e.g., "focus", "movie") with a single command.

#### Acceptance Criteria

1. WHEN the Application is launched with `--profile <name>` argument, THE Application SHALL apply the Brightness_Values defined in the specified Profile to all mapped monitors that are currently connected and exit with exit code 0 without displaying the GUI.
2. THE Application SHALL store Profile definitions in the Settings_Store, where each Profile maps one or more Monitor identifiers to Brightness_Values, supporting a maximum of 50 stored Profiles.
3. THE GUI SHALL provide an interface to create, edit, and delete Profiles, where each Profile name must be between 1 and 64 characters consisting of alphanumeric characters, hyphens, and underscores.
4. IF the specified Profile name does not exist in the Settings_Store, THEN THE CLI SHALL write an error message to standard error and exit with a non-zero exit code. Profile name matching SHALL be case-insensitive.
5. IF a Profile references a Monitor that is not currently connected, THEN THE Application SHALL skip that Monitor, apply brightness to remaining connected monitors, write a warning to standard output, and exit with exit code 0.
6. IF a Profile references only Monitors that are not currently connected, THEN THE Application SHALL write an error message to standard error indicating no mapped monitors are available and exit with a non-zero exit code.
7. IF the GUI receives a request to create a Profile with a name that already exists in the Settings_Store (case-insensitive match), THEN THE GUI SHALL reject the creation and display a validation error indicating the name is already in use.

### Requirement 5: Persistence and Startup Behavior

**User Story:** As a user, I want the application to remember my settings and optionally restore the last profile on startup, so that my preferred brightness levels persist across reboots.

#### Acceptance Criteria

1. WHEN a Profile is created, edited, deleted, or a user preference is changed, THE Application SHALL persist all Profiles and user preferences to the Settings_Store on disk.
2. THE GUI SHALL provide a toggle setting to enable or disable automatic profile application at startup, with the toggle defaulting to disabled on first use.
3. WHILE the auto-apply setting is enabled, WHEN the Application starts in GUI mode, THE Application SHALL apply the last-used Profile brightness values to all mapped monitors.
4. WHILE the auto-apply setting is disabled, WHEN the Application starts in GUI mode, THE Application SHALL read current brightness values from monitors without changing them.
5. WHEN a Profile is applied, THE Application SHALL save the name of that Profile to the Settings_Store as the most recently applied Profile.
6. IF the auto-apply setting is enabled at startup and the last-used Profile no longer exists in the Settings_Store, THEN THE Application SHALL skip profile application, display a notice indicating the profile was not found, and read current brightness values from monitors without changing them.
7. IF the Settings_Store file is missing or unreadable at startup, THEN THE Application SHALL create a new Settings_Store with default preferences and continue startup without applying any Profile.

### Requirement 6: System Tray Integration

**User Story:** As a user, I want the application to minimize to the system tray, so that it stays accessible without occupying taskbar space.

#### Acceptance Criteria

1. WHEN the user minimizes the Application window, THE Application SHALL hide the window from the taskbar and display an icon in the System_Tray with a tooltip showing the application name.
2. WHEN the user double-clicks the System_Tray icon, THE Application SHALL restore the window to its previous size and position and remove the icon from the System_Tray.
3. WHEN the user right-clicks the System_Tray icon, THE Application SHALL display a context menu with options to restore the window or exit the application.
4. WHEN the user selects "Exit" from the System_Tray context menu, THE Application SHALL save current state to the Settings_Store, remove the System_Tray icon, and then terminate the process.
5. WHEN the user clicks the window close button, THE Application SHALL hide the window from the taskbar and display an icon in the System_Tray instead of terminating the process.

### Requirement 7: Build and Distribution

**User Story:** As a user, I want the application to be distributed as a single executable with no additional dependencies beyond .NET 8, so that setup is straightforward.

#### Acceptance Criteria

1. THE Application SHALL compile and publish as a single executable file using `dotnet publish` with the PublishSingleFile property enabled, producing one `.exe` file with no additional files required at runtime.
2. THE Application SHALL use framework-dependent deployment targeting the `win-x64` runtime identifier, requiring only the .NET 8 runtime to be installed on the target machine.
3. THE Application SHALL require only the .NET 8 SDK for building from source, with the build invocable via `dotnet publish` from the repository root without additional tools or scripts.
4. THE Application SHALL not depend on Visual Studio or any IDE-specific tooling for building.
5. THE Application SHALL target the Windows 11 operating system and use Windows-specific APIs for DDC_CI communication.
6. THE Application SHALL produce an executable that runs correctly from any filesystem location without requiring a fixed installation path, supporting placement in directories on the system PATH for CLI usage.
