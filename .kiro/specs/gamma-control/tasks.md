# Implementation Plan: Gamma Control

## Overview

Extend the Monitor Brightness Controller with per-monitor gamma control via DDC/CI VCP code 0x12. The implementation follows the existing brightness architecture: interop layer reads/writes the VCP register, application service orchestrates state and validation, CLI accepts `--gamma` arguments, profiles persist gamma alongside brightness, and smooth transitions animate gamma changes. All changes are incremental and maintain backward compatibility with existing brightness-only profiles.

## Tasks

- [x] 1. Extend models and interfaces for gamma support
  - [x] 1.1 Add `CurrentGamma` property to `MonitorState`
    - Add `public int? CurrentGamma { get; init; }` to the `MonitorState` record in `Models/MonitorState.cs`
    - Position it after `CurrentBrightness` with an XML doc comment matching the existing pattern
    - _Requirements: 1.3_

  - [x] 1.2 Add `MonitorGammaMap` property to `Profile`
    - Add `public IReadOnlyDictionary<string, int>? MonitorGammaMap { get; init; }` to the `Profile` record in `Models/Profile.cs`
    - Apply `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` attribute for backward-compatible serialization
    - _Requirements: 6.1, 8.4_

  - [x] 1.3 Add `VcpGamma` constant to `NativeMethods`
    - Add `internal const byte VcpGamma = 0x12;` to `Infrastructure/NativeMethods.cs`
    - _Requirements: 1.1_

  - [x] 1.4 Extend `IMonitorInterop` with gamma methods
    - Add `Result<int> GetGamma(IntPtr physicalMonitorHandle)` method signature
    - Add `Result<Unit> SetGamma(IntPtr physicalMonitorHandle, int value)` method signature
    - Include XML doc comments following the existing brightness method pattern
    - _Requirements: 1.1, 2.1_

  - [x] 1.5 Extend `IMonitorService` with gamma methods
    - Add `Result<Unit> SetGamma(int monitorIndex, int gammaValue)` method signature
    - Add `Result<int> GetGamma(int monitorIndex)` method signature
    - Include XML doc comments following the existing brightness method pattern
    - _Requirements: 1.3, 2.1, 2.2, 2.3, 2.4, 2.5_

  - [x] 1.6 Extend `IProfileManager` with gamma parameters
    - Modify `CreateProfile` signature to accept an additional `IReadOnlyDictionary<string, int>? gammaMap` parameter
    - Modify `UpdateProfile` signature to accept an additional `IReadOnlyDictionary<string, int>? gammaMap` parameter
    - Update XML doc comments to document gamma handling
    - _Requirements: 6.2, 6.3_

  - [x] 1.7 Refactor `MonitorBrightnessCommand` to `MonitorCommand`
    - Rename the record to `MonitorCommand` in `Application/CliHandler.cs`
    - Change properties to `string Identifier`, `string? BrightnessRaw`, `string? GammaRaw`
    - Update `ParsedCliArguments` to reference `IReadOnlyList<MonitorCommand> MonitorCommands`
    - _Requirements: 5.1, 5.2, 5.4, 5.5_

- [x] 2. Implement infrastructure layer gamma support
  - [x] 2.1 Implement `GetGamma` in `MonitorInterop`
    - Call `NativeMethods.GetVCPFeatureAndVCPFeatureReply` with `VcpGamma` (0x12)
    - Normalize raw value to 0–100 percentage using the same formula as `GetBrightness`
    - Clamp result to [0, 100] and return as integer
    - Return failure result if DDC/CI read fails
    - _Requirements: 1.1, 1.4_

  - [x] 2.2 Implement `SetGamma` in `MonitorInterop`
    - Read VCP maximum for code 0x12, scale percentage to raw register value
    - Call `NativeMethods.SetVCPFeature` with `VcpGamma` and the scaled raw value
    - Return failure result if DDC/CI write fails
    - Validate value range [0, 100] before issuing hardware command
    - _Requirements: 2.1_

  - [x] 2.3 Write property test for gamma normalization
    - **Property 1: Gamma normalization produces valid percentage**
    - Generate random (current, max) uint pairs with max > 0, verify output is always in [0, 100]
    - **Validates: Requirements 1.1**

- [x] 3. Implement application layer gamma support in MonitorService
  - [x] 3.1 Implement `SetGamma` in `MonitorService`
    - Validate gamma value is in [0, 100]; return failure with descriptive message if out of range
    - Validate monitor index exists; return failure with "not found" message if invalid
    - Delegate to `IMonitorInterop.SetGamma` with the monitor's physical handle
    - On success: update `MonitorState.CurrentGamma` and clear `ErrorMessage`
    - On failure: preserve existing gamma value, set `ErrorMessage` on monitor state, return failure
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5_

  - [x] 3.2 Implement `GetGamma` in `MonitorService`
    - Validate monitor index exists; return failure if invalid
    - Delegate to `IMonitorInterop.GetGamma` with the monitor's physical handle
    - On success: update `MonitorState.CurrentGamma` and return the value
    - On failure: set `MonitorState.CurrentGamma` to null, set `ErrorMessage`, return failure
    - _Requirements: 1.2, 1.3, 1.4_

  - [x] 3.3 Read gamma during monitor detection
    - In the `DetectMonitors` method, call `GetGamma` for each DDC/CI-capable monitor after reading brightness
    - Set `CurrentGamma` to null with appropriate error message if read fails or VCP 0x12 not supported
    - _Requirements: 1.1, 1.2, 1.3, 1.4_

  - [x] 3.4 Write property tests for MonitorService gamma validation
    - **Property 2: Out-of-range gamma values are rejected**
    - Generate integers outside [0, 100], verify SetGamma returns failure without DDC/CI call
    - **Property 3: Successful gamma set updates monitor state**
    - Generate valid gamma values [0, 100] with mock success, verify state update
    - **Property 4: Non-existent monitor index returns failure**
    - Generate indices not in detected list, verify failure without DDC/CI call
    - **Validates: Requirements 2.2, 2.3, 2.5**

- [x] 4. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Extend CLI with gamma argument support
  - [x] 5.1 Refactor CLI parser for `MonitorCommand`
    - Update `ParseArguments` to recognize `--gamma <value>` within a `--monitor` group
    - Support both `--brightness` and `--gamma` in any order after `--monitor <id>`
    - Require at least one of `--brightness` or `--gamma` per `--monitor` group
    - Return parse error if `--monitor <id>` has neither `--brightness` nor `--gamma`
    - Add `GammaOption` constant (`"--gamma"`)
    - _Requirements: 5.1, 5.2, 5.4, 5.5, 5.6_

  - [x] 5.2 Implement CLI gamma execution logic
    - In `ExecuteMonitorCommands`, parse and validate gamma value (integer 0–100) when `GammaRaw` is present
    - Call `MonitorService.SetGamma` for gamma-only or combined commands
    - Write descriptive error to stderr if gamma value is invalid (include rejected value and valid range)
    - Continue processing remaining commands on failure (partial failure semantics)
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.7_

  - [x] 5.3 Update CLI usage help text
    - Add `--gamma <value>` line immediately after existing `--brightness` line
    - Document that `--gamma` is optional within a `--monitor` command group
    - Add example showing combined `--monitor <id> --brightness <value> --gamma <value>`
    - _Requirements: 9.1, 9.2, 9.3_

  - [x] 5.4 Write property tests for CLI gamma parsing
    - **Property 5: CLI parsing extracts gamma regardless of argument order**
    - Generate valid --monitor commands with --brightness and --gamma in both orders, verify same result
    - **Property 6: CLI single-setting commands invoke only that setting**
    - Generate gamma-only and brightness-only commands, verify isolation
    - **Property 7: CLI partial failure processes all commands**
    - Generate mixed success/fail command sequences, verify all attempted
    - **Property 18: --monitor without any setting is a parse error**
    - Generate bare --monitor args without --brightness or --gamma, verify error
    - **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7**

- [x] 6. Extend ProfileManager with gamma support
  - [x] 6.1 Update `CreateProfile` to capture gamma
    - Accept the new `gammaMap` parameter
    - Validate all gamma values are in [0, 100]; reject profile with error if any value is out of range
    - Store gamma map alongside brightness map in the profile
    - _Requirements: 6.2, 6.4_

  - [x] 6.2 Update `UpdateProfile` to replace gamma
    - Accept the new `gammaMap` parameter
    - Validate gamma values are in [0, 100]; reject update with error if any value is out of range
    - Replace both brightness and gamma mappings for all monitors in the update
    - _Requirements: 6.3, 6.4_

  - [x] 6.3 Update `ApplyProfile` for gamma application
    - For each connected mapped monitor: apply brightness and gamma independently
    - If gamma map is null (legacy profile): skip all gamma commands, apply only brightness
    - If no mapped monitors are connected: return failure result
    - On partial failure: continue applying to remaining monitors, accumulate error messages identifying each failed monitor and setting
    - A failure in SetBrightness on a monitor does NOT prevent SetGamma on that same monitor (and vice versa)
    - _Requirements: 6.5, 6.6, 7.1, 7.2, 7.3, 7.5, 8.2_

  - [x] 6.4 Update CLI profile execution for gamma
    - Ensure `--profile <name>` delegates to ProfileManager which now applies both brightness and gamma
    - Write failure details to stderr, return exit code 1 on any failure, 0 on full success
    - _Requirements: 7.4_

  - [x] 6.5 Write property tests for profile gamma operations
    - **Property 8: Profile serialization round-trip preserves both mappings**
    - Generate profiles with both maps (values in [0, 100]), serialize/deserialize, verify equality
    - **Property 9: Legacy profile deserializes with null gamma map**
    - Generate brightness-only JSON, verify MonitorGammaMap is null
    - **Property 10: Null gamma map omitted from serialized JSON**
    - Generate null-gamma profiles, verify JSON has no gamma mapping key
    - **Property 11: Out-of-range gamma values in JSON yield null gamma map on load**
    - Generate invalid gamma JSON, verify gamma map treated as null, brightness preserved
    - **Property 12: Profile apply targets only connected monitors with both settings**
    - Generate profiles + connected sets, verify targeting
    - **Property 13: Profile apply partial failure reports all errors**
    - Generate failure scenarios, verify error accumulation
    - **Property 14: Legacy profile apply sends no gamma commands**
    - Generate null-gamma profiles, verify no SetGamma calls
    - **Property 15: Brightness and gamma applied independently per monitor**
    - Generate per-monitor failures, verify independence
    - **Validates: Requirements 6.1, 6.5, 7.1, 7.2, 7.3, 7.5, 8.1, 8.2, 8.3, 8.4, 8.5**

- [x] 7. Implement backward-compatible profile deserialization
  - [x] 7.1 Handle missing gamma mapping on deserialization
    - Ensure `System.Text.Json` deserializes absent `monitorGammaMap` property as null (default behavior for nullable reference types)
    - Verify `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` on `MonitorGammaMap` omits null from output
    - _Requirements: 8.1, 8.3, 8.4_

  - [x] 7.2 Sanitize out-of-range gamma values on load
    - In `SettingsStore` deserialization logic, check all values in a profile's gamma map are [0, 100]
    - If any value is out of range, set the entire gamma map to null for that profile
    - Preserve the brightness mapping unchanged
    - _Requirements: 8.5_

- [x] 8. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 9. Implement smooth gamma transitions
  - [x] 9.1 Create `TransitionRunner` helper class
    - Create `TransitionRunner` as an internal sealed class (shared by brightness and gamma)
    - Implement `RunTransitionAsync(int from, int to, int durationMs, Func<int, Result<Unit>> applyStep, CancellationToken ct)`
    - Calculate intermediate integer values distributed evenly over the duration
    - Support cooperative cancellation via CancellationToken
    - Ensure the final applied value always equals the target
    - _Requirements: 4.1, 4.3_

  - [x] 9.2 Integrate gamma transitions in the UI/service layer
    - When `SmoothTransition` is enabled: use `TransitionRunner` to animate gamma from current to target
    - When `SmoothTransition` is disabled: apply gamma in a single DDC/CI call
    - On new gamma change request during transition: cancel current transition, start new from last applied value
    - On DDC/CI failure during transition: stop, retain last successful value, surface error
    - Transitions run independently per setting per monitor (brightness and gamma don't block each other)
    - _Requirements: 4.1, 4.2, 4.3, 4.4_

  - [x] 9.3 Write property tests for smooth transitions
    - **Property 16: Smooth transition interpolation reaches target**
    - Generate (from, to, duration) tuples, verify final applied value equals target
    - **Property 17: Transition cancellation starts from last applied value**
    - Generate interrupts at random points, verify new transition starts from last applied intermediate value
    - **Validates: Requirements 4.1, 4.3**

- [x] 10. Implement GUI gamma slider
  - [x] 10.1 Add gamma slider and text input to `MonitorControlGroup`
    - Add a gamma slider with range [0, 100] and integer step 1, positioned below the brightness slider
    - Add a numeric text input for gamma beside the slider (matching brightness pattern)
    - Bind slider/input to a `GammaValue` property via two-way data binding
    - Disable slider and make text input non-editable when monitor is not DDC/CI-capable
    - _Requirements: 3.1, 3.8_

  - [x] 10.2 Wire gamma slider to MonitorService
    - On slider drag/release: send integer gamma value to `MonitorService.SetGamma`
    - On text input commit (Enter/focus lost): validate as integer [0, 100], send to MonitorService
    - On invalid text input: reject, revert displayed value to last known gamma
    - On DDC/CI failure: display error message, revert slider to last successfully read gamma value
    - _Requirements: 3.2, 3.3, 3.4, 3.5_

  - [x] 10.3 Read and display gamma on startup and re-enumeration
    - During monitor detection: read and display `CurrentGamma` for each controllable monitor
    - On window focus with `RefreshOnFocus` enabled: re-read gamma from hardware within 2 seconds
    - _Requirements: 3.6, 3.7_

- [x] 11. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- The implementation language is C# targeting .NET 8 with WPF, consistent with the existing project
- FsCheck.Xunit is used for property-based testing in the existing test project

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2", "1.3", "1.7"] },
    { "id": 1, "tasks": ["1.4", "1.5", "1.6"] },
    { "id": 2, "tasks": ["2.1", "2.2", "3.1", "3.2"] },
    { "id": 3, "tasks": ["2.3", "3.3", "3.4"] },
    { "id": 4, "tasks": ["5.1", "6.1", "6.2", "7.1"] },
    { "id": 5, "tasks": ["5.2", "5.3", "6.3", "6.4", "7.2"] },
    { "id": 6, "tasks": ["5.4", "6.5"] },
    { "id": 7, "tasks": ["9.1"] },
    { "id": 8, "tasks": ["9.2", "9.3"] },
    { "id": 9, "tasks": ["10.1"] },
    { "id": 10, "tasks": ["10.2", "10.3"] }
  ]
}
```
