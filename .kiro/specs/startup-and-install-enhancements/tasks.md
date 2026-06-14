# Implementation Plan: Startup and Install Enhancements

## Overview

This plan implements three interrelated features: reliable auto-start with Windows (via refactored `StartupRegistration`), a default startup profile applied on GUI launch, and a "Proper Install" button that copies the app to Program Files with UAC elevation. The implementation proceeds infrastructure-first (interfaces, models), then core logic, then UI wiring, and finally integration.

## Tasks

- [x] 1. Define interfaces and extend data models
  - [x] 1.1 Create `IStartupRegistration` interface
    - Create file `MonitorBrightnessController/Interfaces/IStartupRegistration.cs`
    - Define methods: `SetStartWithWindows(bool)`, `EnsureRegistration(bool)`, `UpdateRegisteredPath(string)`, `IsRegistered()`
    - All mutating methods return `Result<Unit>`
    - _Requirements: 1.1, 1.2, 1.4, 1.5, 1.6, 5.1, 5.2_

  - [x] 1.2 Create `IApplicationInstaller` interface
    - Create file `MonitorBrightnessController/Interfaces/IApplicationInstaller.cs`
    - Define methods: `IsInstalledInProgramFiles()` returning `bool`, `InstallToProgramFiles()` returning `Result<string>`
    - _Requirements: 4.1, 4.2, 4.9_

  - [x] 1.3 Add `DefaultStartupProfileName` property to `AppSettings`
    - Modify `MonitorBrightnessController/Models/AppSettings.cs`
    - Add `public string? DefaultStartupProfileName { get; init; }` with null default
    - Backward-compatible: missing JSON property deserializes to null
    - _Requirements: 2.1_

  - [x] 1.4 Create `IRegistryKeyWrapper` interface for testability
    - Create file `MonitorBrightnessController/Interfaces/IRegistryKeyWrapper.cs`
    - Wrap registry operations: `OpenSubKey`, `SetValue`, `DeleteValue`, `GetValue`
    - Enables mocking registry access in unit/property tests without touching the real registry
    - _Requirements: 1.5, 1.6_

- [x] 2. Implement refactored StartupRegistration
  - [x] 2.1 Implement `StartupRegistration` as instance class implementing `IStartupRegistration`
    - Refactor `MonitorBrightnessController/Infrastructure/StartupRegistration.cs` from static to instance-based
    - Inject `IRegistryKeyWrapper` for testability
    - `SetStartWithWindows(true)`: validate `Environment.ProcessPath` is non-null/empty (Req 1.6), write quoted path to registry (Req 1.1)
    - `SetStartWithWindows(false)`: remove value from registry (Req 1.2)
    - Use registry value name `"MonitorBrightnessController"` (Req 1.3)
    - Return `Result<Unit>.Failure(...)` when registry key cannot be opened (Req 1.5)
    - Return `Result<Unit>.Failure(...)` when exe path is null/empty (Req 1.6)
    - _Requirements: 1.1, 1.2, 1.3, 1.5, 1.6_

  - [x] 2.2 Implement `EnsureRegistration` method
    - When `startWithWindowsEnabled` is false: do nothing, return success
    - When entry is missing and enabled: create with quoted current exe path (Req 5.4)
    - When entry differs (case-insensitive): update to quoted current exe path (Req 5.1, 5.2)
    - When entry matches (case-insensitive): leave unchanged
    - _Requirements: 1.4, 5.1, 5.2, 5.4_

  - [x] 2.3 Implement `UpdateRegisteredPath` method
    - Used by installer to point registry to new installed path
    - Writes quoted path to registry value
    - _Requirements: 5.3_

  - [x] 2.4 Write property test for registration path quoting (Property 1)
    - Create file `MonitorBrightnessController.Tests/Properties/StartupRegistrationProperties.cs`
    - **Property 1: Registration path quoting**
    - For any valid Windows file path, `SetStartWithWindows(true)` writes the path wrapped in double quotes
    - Use custom `Arbitrary` for valid Windows file paths
    - **Validates: Requirements 1.1**

  - [x] 2.5 Write property test for EnsureRegistration reconciliation (Property 2)
    - Add to `MonitorBrightnessController.Tests/Properties/StartupRegistrationProperties.cs`
    - **Property 2: EnsureRegistration reconciliation**
    - For any (currentExePath, existingRegistryValue|absent, startWithWindows enabled/disabled), verify: does nothing when disabled; creates quoted path when missing+enabled; updates when mismatch; leaves unchanged when match
    - **Validates: Requirements 1.4, 5.1, 5.2, 5.4**

  - [x] 2.6 Write unit tests for StartupRegistration error handling
    - Create file `MonitorBrightnessController.Tests/StartupRegistrationTests.cs`
    - Test registry key open failure returns `Result<Unit>.Failure`
    - Test null/empty exe path returns `Result<Unit>.Failure`
    - Test successful register/unregister scenarios
    - _Requirements: 1.5, 1.6_

- [x] 3. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Implement Default Startup Profile logic
  - [x] 4.1 Extend `StartupCoordinator.Decide` to support `DefaultStartupProfileName` and CLI override
    - Modify `MonitorBrightnessController/Presentation/StartupCoordinator.cs`
    - Add `bool isCliOverride` parameter to `Decide`
    - When `isCliOverride` is true: skip profile application regardless of settings (Req 2.5)
    - When `DefaultStartupProfileName` is set and profile exists: apply it (Req 2.4)
    - When `DefaultStartupProfileName` references non-existent profile: return "missing profile" decision with profile name in notice (Req 2.6)
    - Update `StartupAction` enum if needed (add `ApplyDefaultProfile`, `DefaultProfileMissing`)
    - _Requirements: 2.4, 2.5, 2.6_

  - [x] 4.2 Extend `StartupCoordinator.Run` to handle default startup profile
    - Call `IStartupRegistration.EnsureRegistration` when `StartWithWindows` is enabled
    - Determine `isCliOverride` by checking if args contain `--monitor` or `--profile`
    - Apply the startup profile via `ProfileManager.ApplyProfile` (Req 2.4)
    - Handle apply failure (disconnected monitors) gracefully — log and continue (Req 2.7)
    - Update `LastAppliedProfileName` on success (Req 2.8)
    - _Requirements: 1.4, 2.4, 2.5, 2.6, 2.7, 2.8_

  - [x] 4.3 Write property test for startup decision correctness (Property 4)
    - Create file `MonitorBrightnessController.Tests/Properties/StartupDecisionProperties.cs`
    - **Property 4: Startup decision correctness**
    - For any (DefaultStartupProfileName, list of profile names, CLI override flag), verify: skip when CLI override; apply when profile exists and no CLI override; "missing profile" when profile doesn't exist and no CLI override
    - **Validates: Requirements 2.4, 2.5, 2.6**

  - [x] 4.4 Write property test for settings round-trip preserving DefaultStartupProfileName (Property 3)
    - Add to `MonitorBrightnessController.Tests/Properties/StartupDecisionProperties.cs`
    - **Property 3: Settings round-trip preserves DefaultStartupProfileName**
    - For any valid AppSettings with random DefaultStartupProfileName (null or valid string), serialize to JSON and deserialize back, verify equivalence
    - **Validates: Requirements 2.1**

  - [x] 4.5 Write unit tests for StartupCoordinator.Run with default startup profile
    - Modify `MonitorBrightnessController.Tests/StartupBehaviorTests.cs`
    - Test CLI override skips profile application
    - Test successful default profile apply updates LastAppliedProfileName
    - Test disconnected monitors produce notice but don't crash
    - Test missing profile produces notice with profile name
    - _Requirements: 2.4, 2.5, 2.6, 2.7, 2.8_

- [x] 5. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Implement ApplicationInstaller
  - [x] 6.1 Implement `ApplicationInstaller` class
    - Create file `MonitorBrightnessController/Infrastructure/ApplicationInstaller.cs`
    - Implement `IApplicationInstaller`
    - `IsInstalledInProgramFiles()`: compare current exe path to `%ProgramFiles%\MonitorBrightnessController\MonitorBrightnessController.exe` using case-insensitive, path-normalized comparison (Req 4.1, 4.9)
    - `InstallToProgramFiles()`: launch elevated helper process (`cmd /c copy` with `runas` verb) to copy exe to Program Files (Req 4.2, 4.5)
    - Create target directory if missing (Req 4.4)
    - Return installed path on success (Req 4.8)
    - Handle UAC denial → return failure with cancellation message (Req 4.6)
    - Handle copy failure → return failure with error details (Req 4.7)
    - _Requirements: 4.1, 4.2, 4.4, 4.5, 4.6, 4.7, 4.8, 4.9_

  - [x] 6.2 Write property test for install directory detection (Property 8)
    - Add to `MonitorBrightnessController.Tests/Properties/StartupRegistrationProperties.cs`
    - **Property 8: Install directory detection**
    - For any Windows file path, `IsInstalledInProgramFiles` returns true iff the path matches `%ProgramFiles%\MonitorBrightnessController\MonitorBrightnessController.exe` (case-insensitive, normalized)
    - Use custom `Arbitrary` for valid Windows paths plus known positive cases
    - **Validates: Requirements 4.1, 4.9**

  - [x] 6.3 Write unit tests for ApplicationInstaller
    - Create file `MonitorBrightnessController.Tests/ApplicationInstallerTests.cs`
    - Test `IsInstalledInProgramFiles` with various path formats (trailing slash, mixed case, short paths)
    - Test install flow with mocked process launcher
    - _Requirements: 4.1, 4.9_

- [x] 7. Implement Settings UI for Default Startup Profile and Install button
  - [x] 7.1 Add ViewModel properties for Default Startup Profile dropdown
    - Modify `MonitorBrightnessController/Presentation/MainWindowViewModel.cs`
    - Add `AvailableProfilesForStartup` as `ObservableCollection<string>` with "None" as first entry followed by profile names in store order (Req 3.1)
    - Add `SelectedStartupProfile` property bound to dropdown selection
    - When selection changes: persist `DefaultStartupProfileName` (null for "None") without separate save action (Req 3.3, 3.4)
    - On persist failure: revert dropdown to previous value, show error (Req 3.6)
    - When profile is deleted that was set as default: set to null, update dropdown to "None" (Req 3.5)
    - Refresh dropdown list when profiles are created/deleted (Req 3.7)
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7_

  - [x] 7.2 Add ViewModel properties for Proper Install button
    - Modify `MonitorBrightnessController/Presentation/MainWindowViewModel.cs`
    - Add `IsProperlyInstalled` property (drives button enabled state) (Req 4.9)
    - Add `InstallStatusText` for label when already installed (Req 4.9)
    - Add `ProperInstallCommand` (ICommand) that calls `IApplicationInstaller.InstallToProgramFiles()`
    - On success: update `IStartupRegistration` if `StartWithWindows` is enabled (Req 4.3), show confirmation (Req 4.8), inform about restart (Req 4.10)
    - On failure: show error message (Req 4.6, 4.7)
    - _Requirements: 4.1, 4.3, 4.6, 4.7, 4.8, 4.9, 4.10_

  - [x] 7.3 Write property test for startup profile dropdown list construction (Property 5)
    - Create file `MonitorBrightnessController.Tests/Properties/StartupProfileDropdownProperties.cs`
    - **Property 5: Startup profile dropdown list construction**
    - For any list of profile names, the `AvailableProfilesForStartup` collection has "None" as first element followed by all profile names in store order, with total length = count + 1
    - **Validates: Requirements 3.1**

  - [x] 7.4 Write property test for startup profile dropdown selection resolution (Property 6)
    - Add to `MonitorBrightnessController.Tests/Properties/StartupProfileDropdownProperties.cs`
    - **Property 6: Startup profile dropdown selection resolution**
    - For any `DefaultStartupProfileName` that is null or does not match any name in the profile list (case-insensitive), the effective selected value resolves to "None"
    - **Validates: Requirements 3.2**

  - [x] 7.5 Write property test for default profile cleanup on deletion (Property 7)
    - Add to `MonitorBrightnessController.Tests/Properties/StartupProfileDropdownProperties.cs`
    - **Property 7: Default profile cleanup on deletion**
    - For any profile set as `DefaultStartupProfileName`, when that profile is deleted, `DefaultStartupProfileName` becomes null
    - **Validates: Requirements 3.5**

  - [x] 7.6 Write unit tests for ViewModel startup profile dropdown behavior
    - Create file `MonitorBrightnessController.Tests/StartupProfileDropdownTests.cs`
    - Test dropdown populated with "None" + all profiles
    - Test selecting profile persists setting
    - Test selecting "None" sets setting to null
    - Test persist failure reverts selection
    - Test profile deletion updates default to null
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7_

- [x] 8. Add Settings Tab XAML for new controls
  - [x] 8.1 Add Default Startup Profile dropdown to Settings tab
    - Modify the Settings section XAML (likely `MainWindow.xaml` settings area)
    - Add `ComboBox` bound to `AvailableProfilesForStartup` with `SelectedItem` bound to `SelectedStartupProfile`
    - Place below existing settings toggles
    - _Requirements: 3.1, 3.2, 3.3, 3.4_

  - [x] 8.2 Add Proper Install button and status label to Settings tab
    - Add `Button` with Content "Proper Install" bound to `ProperInstallCommand`, enabled when `!IsProperlyInstalled`
    - Add `TextBlock` label visible when `IsProperlyInstalled` indicating app is already properly installed (Req 4.9)
    - _Requirements: 4.1, 4.8, 4.9, 4.10_

- [x] 9. Wire startup registration into application entry point
  - [x] 9.1 Integrate `IStartupRegistration.EnsureRegistration` into GUI startup path
    - Modify `Program.cs` or `App.xaml.cs` to instantiate `StartupRegistration` and call `EnsureRegistration` on startup when `StartWithWindows` is enabled
    - Pass CLI override detection (presence of `--monitor` or `--profile`) to `StartupCoordinator`
    - Wire `IStartupRegistration` and `IApplicationInstaller` into DI/constructor chains
    - Update `StartWithWindows` toggle in ViewModel to call `IStartupRegistration.SetStartWithWindows` (replacing old static call)
    - _Requirements: 1.4, 2.5, 5.1, 5.2, 5.4_

- [x] 10. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- The `IRegistryKeyWrapper` abstraction enables testing registry logic without touching the real Windows registry
- The existing `StartupCoordinator.Decide` signature changes (adding `isCliOverride`) — existing tests in `StartupBehaviorTests.cs` will need updating in task 4.1

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2", "1.3", "1.4"] },
    { "id": 1, "tasks": ["2.1", "6.1"] },
    { "id": 2, "tasks": ["2.2", "2.3", "2.4", "6.2", "6.3"] },
    { "id": 3, "tasks": ["2.5", "2.6", "4.1"] },
    { "id": 4, "tasks": ["4.2", "4.3", "4.4"] },
    { "id": 5, "tasks": ["4.5", "7.1", "7.2"] },
    { "id": 6, "tasks": ["7.3", "7.4", "7.5", "7.6", "8.1", "8.2"] },
    { "id": 7, "tasks": ["9.1"] }
  ]
}
```
