# Requirements Document

## Introduction

This feature extends the Monitor Brightness Controller application to support per-monitor gamma control via DDC/CI alongside the existing brightness control. Gamma adjustments use VCP code 0x12 (Video Gain / Contrast) and follow the same interaction patterns as brightness: per-monitor sliders in the GUI, CLI arguments for automation, and inclusion in named profiles. Profiles are extended to store both brightness and gamma settings per monitor so that a single profile application restores the user's complete display configuration.

## Glossary

- **Application**: The Monitor Brightness Controller WPF desktop application.
- **Monitor_Service**: The application-layer service that orchestrates monitor detection, state tracking, and DDC/CI operations for brightness and gamma.
- **Monitor_Interop**: The infrastructure layer that communicates directly with monitors via Win32 DDC/CI P/Invoke calls.
- **Profile_Manager**: The application-layer service that manages named profiles and their persistence.
- **CLI_Handler**: The component that parses command-line arguments and executes brightness/gamma or profile operations without showing the GUI.
- **Settings_Store**: The infrastructure component responsible for persisting application state (profiles, preferences) to JSON on disk.
- **Gamma**: The monitor's video gain setting controlled via DDC/CI VCP code 0x12, expressed as an integer in the range [0, 100].
- **Profile**: A named preset that maps monitor device paths to both brightness and gamma values.
- **Monitor_Control_Group**: The WPF user control displaying sliders and value inputs for a single monitor.

## Requirements

### Requirement 1: Read Gamma from Monitor

**User Story:** As a user, I want the application to read the current gamma value from each DDC/CI-capable monitor, so that I can see my monitors' current gamma settings.

#### Acceptance Criteria

1. WHEN the Application detects monitors, THE Monitor_Interop SHALL read the current gamma value (VCP code 0x12) for each DDC/CI-capable monitor, normalize the raw VCP current value against the VCP-reported maximum to a percentage, clamp the result to the range [0, 100], and return it as an integer.
2. IF the DDC/CI read for gamma (VCP code 0x12) fails on a monitor, THEN THE Monitor_Service SHALL set that monitor's current gamma to null and store the error description in the monitor's ErrorMessage field within MonitorState.
3. THE Monitor_Service SHALL store the current gamma value as a nullable integer in the in-memory MonitorState for each detected monitor.
4. IF a monitor is DDC/CI-capable but does not support VCP code 0x12, THEN THE Monitor_Interop SHALL return a failure result, and THE Monitor_Service SHALL set that monitor's current gamma to null and record an error message indicating that gamma is not supported.

### Requirement 2: Set Gamma on Monitor

**User Story:** As a user, I want to set the gamma value on any DDC/CI-capable monitor, so that I can adjust my display's gamma to my preference.

#### Acceptance Criteria

1. WHEN a gamma value in the range [0, 100] and a valid monitor index are provided, THE Monitor_Interop SHALL write the gamma value (VCP code 0x12) to the physical monitor identified by that index.
2. IF the gamma value is outside the range [0, 100], THEN THE Monitor_Service SHALL return a failure result with an error message indicating the value is out of the accepted range, without sending a DDC/CI command.
3. WHEN the gamma is set successfully, THE Monitor_Service SHALL update the in-memory MonitorState for the target monitor with the new gamma value and clear any previous error message.
4. IF the DDC/CI write for gamma fails, THEN THE Monitor_Service SHALL return a failure result with an error message indicating the DDC/CI communication failure, preserve the existing gamma value in the MonitorState, and set the error message on the monitor state.
5. IF the specified monitor index does not match any detected monitor, THEN THE Monitor_Service SHALL return a failure result with an error message indicating the monitor was not found, without sending a DDC/CI command.

### Requirement 3: GUI Gamma Slider

**User Story:** As a user, I want a gamma slider for each monitor in the Monitors tab, so that I can adjust gamma visually alongside brightness.

#### Acceptance Criteria

1. THE Monitor_Control_Group SHALL display a gamma slider with range [0, 100] and integer step of 1 for each DDC/CI-capable monitor, positioned below the existing brightness slider.
2. WHEN the user drags the gamma slider to a new position, THE Application SHALL send the integer gamma value to the Monitor_Service.
3. WHEN the user finishes typing a gamma value in the text input and the value is a valid integer within [0, 100], THE Application SHALL send the gamma value to the Monitor_Service.
4. IF the user types a gamma value that is non-numeric or outside the range [0, 100], THEN THE Application SHALL reject the input, revert the displayed value to the last known gamma value, and not send a command to the Monitor_Service.
5. IF the Monitor_Service fails to apply a gamma value via DDC/CI, THEN THE Application SHALL display an error message indicating the gamma change failed and revert the slider position to the last successfully read gamma value.
6. WHEN the Application detects monitors during startup or re-enumeration, THE Application SHALL read and display the current gamma value from hardware for each controllable monitor.
7. WHEN the Application window gains focus and RefreshOnFocus is enabled, THE Application SHALL re-read and display the current gamma value from hardware for each controllable monitor within 2 seconds.
8. WHILE a monitor does not support DDC/CI, THE Monitor_Control_Group SHALL display the gamma slider as disabled with the text input non-editable.

### Requirement 4: Smooth Gamma Transitions

**User Story:** As a user, I want gamma changes to animate smoothly when smooth transitions are enabled, so that gamma adjustments are not jarring.

#### Acceptance Criteria

1. WHILE the SmoothTransition setting is enabled, WHEN the user changes the gamma value, THE Application SHALL transition the gamma from the current value to the target value by applying incremental intermediate values over the configured TransitionDurationMs (100–2000 ms), where the gamma value is an integer in the range 0–100.
2. WHILE the SmoothTransition setting is disabled, WHEN the user changes the gamma value, THE Application SHALL apply the target gamma value in a single DDC/CI call with no intermediate steps.
3. WHILE a gamma transition is in progress, WHEN the user requests a new gamma value, THE Application SHALL cancel the current transition and begin a new transition from the most recently applied intermediate value to the new target value.
4. IF a DDC/CI command fails during a gamma transition, THEN THE Application SHALL stop the transition, retain the last successfully applied gamma value as the current value, and display an error message indicating the communication failure.

### Requirement 5: CLI Gamma Argument

**User Story:** As a user, I want to set gamma via command-line arguments, so that I can automate gamma adjustments with scripts and shortcuts.

#### Acceptance Criteria

1. WHEN the CLI_Handler receives a `--monitor <id> --gamma <value>` argument pair, THE CLI_Handler SHALL resolve the monitor by numeric index or case-insensitive name match and apply the specified gamma value to that monitor, returning exit code 0 on success.
2. WHEN the CLI_Handler receives both `--brightness <value>` and `--gamma <value>` for the same `--monitor <id>`, THE CLI_Handler SHALL apply both the brightness value and the gamma value to that monitor regardless of the order the two options appear in.
3. IF the gamma value is not a valid integer in the range [0, 100], THEN THE CLI_Handler SHALL write an error message to standard error that includes the rejected value and the valid range [0, 100], and continue processing remaining monitor commands.
4. WHEN only `--gamma` is specified without `--brightness` for a monitor, THE CLI_Handler SHALL apply the gamma value without issuing any brightness command to that monitor.
5. WHEN only `--brightness` is specified without `--gamma` for a monitor, THE CLI_Handler SHALL apply the brightness value without issuing any gamma command to that monitor.
6. IF `--monitor <id>` is specified without either `--brightness` or `--gamma`, THEN THE CLI_Handler SHALL write an error message to standard error indicating that at least one of --brightness or --gamma is required and return exit code 1.
7. IF any monitor command fails (invalid value, unresolved monitor identifier, or hardware error), THEN THE CLI_Handler SHALL attempt all remaining monitor commands, write each failure to standard error, and return exit code 1.

### Requirement 6: Profile Gamma Storage

**User Story:** As a user, I want profiles to store both brightness and gamma settings per monitor, so that I can save and restore my complete display configuration with one action.

#### Acceptance Criteria

1. THE Profile SHALL store a mapping of monitor device paths to integer gamma values (0–100) alongside the existing brightness mapping, where both mappings reference the same set of monitor device paths.
2. WHEN a profile is created, THE Profile_Manager SHALL capture both the current brightness and current gamma integer values for each selected monitor and store them in the profile's brightness and gamma mappings respectively.
3. WHEN a profile is updated, THE Profile_Manager SHALL replace both the brightness and gamma mappings with the provided monitor values for all monitors included in the update.
4. IF a profile's gamma value for a monitor is outside the integer range [0, 100], THEN THE Profile_Manager SHALL reject the profile with an error message indicating the invalid gamma value and the affected monitor device path.
5. WHEN a profile is applied, THE Profile_Manager SHALL set both the brightness and gamma values on each currently connected monitor that is mapped in the profile, skipping monitors that are not connected.
6. IF a profile is applied and none of the profile's mapped monitors are currently connected, THEN THE Profile_Manager SHALL return a failure result with an error message indicating that no mapped monitors are connected.

### Requirement 7: Profile Gamma Application

**User Story:** As a user, I want applying a profile to restore both brightness and gamma on all mapped monitors, so that I get my full display configuration back in one step.

#### Acceptance Criteria

1. WHEN a profile is applied, THE Profile_Manager SHALL set the brightness value (integer 0–100) and the gamma value (integer 0–100) for each mapped monitor that is currently connected.
2. WHEN a profile is applied and a mapped monitor is not currently connected, THE Profile_Manager SHALL skip that monitor without failing the overall operation.
3. IF setting brightness or gamma fails on one monitor during profile application, THEN THE Profile_Manager SHALL continue applying settings to remaining monitors and return a failure result containing an error message that identifies each monitor and setting that failed.
4. WHEN a profile is applied via CLI using `--profile <name>`, THE CLI_Handler SHALL delegate to the Profile_Manager to apply both brightness and gamma values from the profile, write failure details to standard error, and return exit code 1 if any monitor setting failed or exit code 0 if all succeeded.
5. WHEN a profile is applied and both brightness and gamma are set on the same monitor, THE Profile_Manager SHALL attempt both operations independently so that a failure in one does not prevent the other from being applied.

### Requirement 8: Backward-Compatible Profile Deserialization

**User Story:** As a user, I want my existing profiles (which only contain brightness) to continue working after the update, so that I do not lose my saved presets.

#### Acceptance Criteria

1. WHEN the Settings_Store loads a profile whose JSON object contains no gamma mapping property, THE Settings_Store SHALL deserialize the profile with the gamma mapping set to null rather than substituting a default gamma value.
2. WHEN a profile with a null gamma mapping is applied, THE Profile_Manager SHALL apply only the brightness values from the profile's MonitorBrightnessMap and SHALL NOT send any gamma adjustment commands to the monitors.
3. WHEN the Settings_Store saves an AppSettings that contains profiles with both brightness and gamma mappings, THE Settings_Store SHALL serialize both mappings to the settings JSON file.
4. WHEN the Settings_Store saves an AppSettings that contains a profile with a null gamma mapping, THE Settings_Store SHALL serialize that profile without a gamma mapping property, preserving its brightness-only format.
5. IF the Settings_Store encounters a profile whose gamma mapping property is present but contains values outside the valid gamma range, THEN THE Settings_Store SHALL treat the gamma mapping as null for that profile and load the profile successfully with only its brightness mapping.

### Requirement 9: CLI Usage Help

**User Story:** As a user, I want the CLI usage text to document the new gamma option, so that I can discover how to use it.

#### Acceptance Criteria

1. THE CLI_Handler SHALL include a `--gamma <value>` line in the usage help text that describes the option as setting gamma with a valid range of 0-100 for a monitor, placed immediately after the existing `--brightness` line.
2. THE CLI_Handler SHALL include in the usage help text that `--gamma` is an optional parameter within a `--monitor` command group (i.e., `--monitor <id> --brightness <value> --gamma <value>`).
3. THE CLI_Handler SHALL display a usage example in the help text showing a single monitor command that combines both `--brightness` and `--gamma` options with sample numeric values.
