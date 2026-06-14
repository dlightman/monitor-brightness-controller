# Implementation Plan: Monitor Brightness Controller

## Overview

Implement a Windows 11 desktop application (C# / .NET 8 / WPF) that controls external monitor brightness via DDC/CI. The implementation follows a three-layer architecture (Presentation, Application, Infrastructure) and provides both GUI and CLI interfaces. Tasks are ordered to build from the bottom up: shared models and infrastructure first, then application logic, then presentation, and finally integration wiring.

## Tasks

- [x] 1. Set up project structure and shared types
  - [x] 1.1 Create solution and project scaffolding
    - Create `MonitorBrightnessController.sln` with two projects: `MonitorBrightnessController` (WPF app, net8.0-windows) and `MonitorBrightnessController.Tests` (xUnit test project, net8.0-windows)
    - Add NuGet references: `H.NotifyIcon.Wpf`, `System.Text.Json` to the app project; `xunit`, `FsCheck.Xunit`, `FluentAssertions`, `NSubstitute` to the test project
    - Configure `PublishSingleFile`, `RuntimeIdentifier=win-x64`, `SelfContained=false` in the app project file
    - _Requirements: 7.1, 7.2, 7.3, 7.4_

  - [x] 1.2 Define core data models and Result type
    - Create `Models/MonitorState.cs` record with `MonitorIndex`, `MonitorName`, `DevicePath`, `PhysicalHandle`, `CurrentBrightness`, `IsControllable`, `ErrorMessage`
    - Create `Models/Profile.cs` record with `Name` and `MonitorBrightnessMap` dictionary
    - Create `Models/AppSettings.cs` record with `Profiles`, `AutoApplyOnStartup`, `LastAppliedProfileName`
    - Create `Models/Result.cs` generic struct with `IsSuccess`, `Value`, `Error`, and static factory methods
    - _Requirements: 1.1, 4.2, 4.3, 5.1_

  - [x] 1.3 Define service interfaces
    - Create `Interfaces/IMonitorInterop.cs` with `EnumerateMonitors`, `GetBrightness`, `SetBrightness`, `ReleaseMonitors`
    - Create `Interfaces/IMonitorService.cs` with `DetectMonitors`, `SetBrightness`, `GetBrightness`, `FindMonitor`
    - Create `Interfaces/IProfileManager.cs` with `GetAllProfiles`, `GetProfile`, `CreateProfile`, `UpdateProfile`, `DeleteProfile`, `ApplyProfile`
    - Create `Interfaces/ISettingsStore.cs` with `Load`, `Save`
    - Create `Interfaces/ICliHandler.cs` with `Execute`
    - _Requirements: 1.1, 2.1, 3.1, 4.1, 5.1_

- [x] 2. Implement Infrastructure Layer
  - [x] 2.1 Implement DDC/CI P/Invoke interop
    - Create `Infrastructure/NativeMethods.cs` with P/Invoke declarations for `EnumDisplayMonitors`, `GetNumberOfPhysicalMonitorsFromHMONITOR`, `GetPhysicalMonitorsFromHMONITOR`, `GetVCPFeatureAndVCPFeatureReply`, `SetVCPFeature`, `DestroyPhysicalMonitors`, `EnumDisplayDevices`
    - Create `Infrastructure/MonitorInterop.cs` implementing `IMonitorInterop`: enumerate monitors, retrieve EDID name, read/write VCP code 0x10 for brightness
    - Handle error cases: return `Result.Failure` for DDC/CI communication errors
    - _Requirements: 1.1, 1.2, 1.3, 1.4_

  - [x] 2.2 Implement SettingsStore (JSON persistence)
    - Create `Infrastructure/SettingsStore.cs` implementing `ISettingsStore`
    - Store at `%LOCALAPPDATA%\MonitorBrightnessController\settings.json`
    - Handle missing file (create defaults), corrupted JSON (log warning, create defaults), locked file (retry once after 100ms), disk full (return failure result)
    - Use `System.Text.Json` with indented formatting
    - _Requirements: 5.1, 5.7_

  - [x] 2.3 Write property test: Settings Serialization Round-Trip
    - **Property 13: Settings Serialization Round-Trip**
    - Create custom `Arbitrary<AppSettings>` generating random profiles (valid names, valid brightness maps)
    - Assert: serialize then deserialize produces equal object
    - **Validates: Requirements 5.1**

- [x] 3. Implement Application Layer — Monitor Service
  - [x] 3.1 Implement MonitorService
    - Create `Application/MonitorService.cs` implementing `IMonitorService`
    - `DetectMonitors`: call interop, sort by device path, assign indices starting at 1, apply name fallback for null/empty/whitespace names
    - `SetBrightness`: validate value 0–100, delegate to interop
    - `GetBrightness`: delegate to interop, update in-memory state
    - `FindMonitor`: match by index (numeric string) or case-insensitive name
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 2.6, 3.2_

  - [x] 3.2 Write property test: Deterministic Monitor Index Assignment
    - **Property 1: Deterministic Monitor Index Assignment**
    - Generate random lists of distinct device path strings, verify index assignment is the same regardless of input order
    - **Validates: Requirements 1.1**

  - [x] 3.3 Write property test: Monitor Name Fallback
    - **Property 2: Monitor Name Fallback**
    - Generate random monitor index + nullable/empty/whitespace name strings, verify fallback to "Monitor N"
    - **Validates: Requirements 1.2**

  - [x] 3.4 Write property test: DDC/CI Support Filtering
    - **Property 3: DDC/CI Support Filtering**
    - Generate lists of (devicePath, supportsDdc) tuples, verify controllable list matches only supported ones
    - **Validates: Requirements 1.3**

  - [x] 3.5 Write property test: Brightness Value Validation
    - **Property 5: Brightness Value Validation**
    - Generate random strings (non-numeric, negative, >100, floats), verify all are rejected; generate integers 0–100, verify accepted
    - **Validates: Requirements 2.7, 3.5**

  - [x] 3.6 Write property test: Case-Insensitive Monitor Identifier Resolution
    - **Property 6: Case-Insensitive Monitor Identifier Resolution**
    - Generate random monitor names with case variants, verify same monitor resolved
    - **Validates: Requirements 3.2**

- [x] 4. Checkpoint — Core infrastructure and monitor service verified
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Implement Application Layer — Profile Manager
  - [x] 5.1 Implement ProfileManager
    - Create `Application/ProfileManager.cs` implementing `IProfileManager`
    - Name validation: 1–64 chars, `[a-zA-Z0-9_-]` only
    - Enforce max 50 profiles, case-insensitive uniqueness
    - `ApplyProfile`: resolve monitors by device path, skip disconnected, set brightness on connected, fail if all disconnected
    - On successful apply, update `LastAppliedProfileName` in settings
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 5.5_

  - [x] 5.2 Write property test: Profile Name Validation
    - **Property 10: Profile Name Validation**
    - Generate random strings of varying length and charset, verify acceptance iff 1–64 chars and matches `[a-zA-Z0-9_-]`
    - **Validates: Requirements 4.3**

  - [x] 5.3 Write property test: Profile Count Limit
    - **Property 9: Profile Count Limit**
    - Pre-fill 50 profiles, attempt to create one more, verify rejection and count remains 50
    - **Validates: Requirements 4.2**

  - [x] 5.4 Write property test: Case-Insensitive Profile Name Uniqueness
    - **Property 12: Case-Insensitive Profile Name Uniqueness**
    - Generate existing profile name + case variants, verify duplicate detection
    - **Validates: Requirements 4.7**

  - [x] 5.5 Write property test: Profile Application Skips Disconnected Monitors
    - **Property 11: Profile Application Skips Disconnected Monitors**
    - Generate profile mapping N monitors, random subset connected, verify brightness set on connected, skipped on disconnected, success if C non-empty
    - **Validates: Requirements 4.5**

  - [x] 5.6 Write property test: Last Applied Profile Tracking
    - **Property 14: Last Applied Profile Tracking**
    - Generate random valid profile name, apply, verify `lastAppliedProfileName` updated
    - **Validates: Requirements 5.5**

- [x] 6. Implement CLI Handler
  - [x] 6.1 Implement CliHandler
    - Create `Application/CliHandler.cs` implementing `ICliHandler`
    - Parse `--monitor <id> --brightness <value>` pairs (repeatable)
    - Parse `--profile <name>` argument
    - Validate arguments, report errors to stderr, return appropriate exit codes
    - Implement partial failure: attempt all operations, report errors individually, exit 1 if any fail
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 4.1, 4.4, 4.6_

  - [x] 6.2 Write property test: Multi-Pair CLI Argument Parsing
    - **Property 7: Multi-Pair CLI Argument Parsing**
    - Generate N random valid `--monitor <id> --brightness <value>` pairs, verify N command objects produced with correct identifiers and values in order
    - **Validates: Requirements 3.3**

  - [x] 6.3 Write property test: Partial Failure Attempts All Monitors
    - **Property 8: Partial Failure Attempts All Monitors**
    - Generate random sets with success/failure flags, verify all operations attempted and individual results correct
    - **Validates: Requirements 3.7**

- [x] 7. Checkpoint — Application layer verified
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. Implement Presentation Layer — Main Window and Controls
  - [x] 8.1 Implement MainWindow and MonitorControlGroup
    - Create `Presentation/MainWindow.xaml` and code-behind with `ItemsControl` binding to detected monitors
    - Create `Presentation/MonitorControlGroup.xaml` UserControl with: label (index + name), slider (0–100, integer), text input, validation error display
    - Bind slider and text input bidirectionally, commit brightness on slider release or text commit
    - Handle DDC/CI failure: revert to last known value, show error
    - Display "no controllable monitors" when list is empty
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 2.8, 2.9_

  - [x] 8.2 Write property test: Bidirectional Brightness Control Sync
    - **Property 4: Bidirectional Brightness Control Sync**
    - Generate random integers [0,100], verify slider↔text input sync logic produces matching values
    - **Validates: Requirements 2.4, 2.5**

  - [x] 8.3 Implement ProfilePanel UI
    - Create `Presentation/ProfilePanel.xaml` UserControl for creating, editing, and deleting profiles
    - Validate profile name on creation (1–64 chars, valid charset, case-insensitive uniqueness)
    - Display validation errors inline
    - Wire to `IProfileManager` for CRUD operations
    - _Requirements: 4.3, 4.7_

- [x] 9. Implement System Tray Integration
  - [x] 9.1 Implement SystemTrayManager
    - Create `Presentation/SystemTrayManager.cs` using `H.NotifyIcon.Wpf`
    - Minimize → hide window, show tray icon with tooltip
    - Close button → hide to tray (not terminate)
    - Double-click tray icon → restore window
    - Right-click context menu → Restore, Exit
    - Exit → save state, remove icon, terminate
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5_

- [x] 10. Wire entry point and startup logic
  - [x] 10.1 Implement Program.Main entry point
    - Create `Program.cs` with `Main` method
    - Detect CLI args: if `--monitor` or `--profile` present, run `CliHandler` and exit
    - Otherwise launch WPF application with `MainWindow`
    - Register DI: wire `IMonitorInterop`, `IMonitorService`, `IProfileManager`, `ISettingsStore`, `ICliHandler`
    - _Requirements: 3.1, 4.1_

  - [x] 10.2 Implement startup behavior and auto-apply
    - On GUI startup: load settings, check `AutoApplyOnStartup` toggle
    - If enabled and last profile exists: apply it
    - If enabled but profile missing: skip, show notice, read current values
    - If disabled: read current brightness values without modifying
    - Provide toggle in GUI for auto-apply setting
    - _Requirements: 5.2, 5.3, 5.4, 5.6, 5.7_

- [x] 11. Build configuration and publish verification
  - [x] 11.1 Configure single-file publish and verify build
    - Ensure `.csproj` has `<PublishSingleFile>true</PublishSingleFile>`, `<RuntimeIdentifier>win-x64</RuntimeIdentifier>`, `<SelfContained>false</SelfContained>`
    - Verify `dotnet publish` from repository root produces a single `.exe`
    - Ensure the application runs from any filesystem location
    - _Requirements: 7.1, 7.2, 7.5, 7.6_

- [x] 12. Final checkpoint — Full integration verified
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- The DDC/CI interop layer (`MonitorInterop`) cannot be unit-tested without real hardware; it is tested via integration tests or manual verification
- All property-based tests use FsCheck with `[Property(MaxTest = 100)]`

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["1.2", "1.3"] },
    { "id": 2, "tasks": ["2.1", "2.2"] },
    { "id": 3, "tasks": ["2.3", "3.1"] },
    { "id": 4, "tasks": ["3.2", "3.3", "3.4", "3.5", "3.6", "5.1"] },
    { "id": 5, "tasks": ["5.2", "5.3", "5.4", "5.5", "5.6", "6.1"] },
    { "id": 6, "tasks": ["6.2", "6.3"] },
    { "id": 7, "tasks": ["8.1"] },
    { "id": 8, "tasks": ["8.2", "8.3", "9.1"] },
    { "id": 9, "tasks": ["10.1"] },
    { "id": 10, "tasks": ["10.2"] },
    { "id": 11, "tasks": ["11.1"] }
  ]
}
```
