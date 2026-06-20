# Implementation Plan: enhancements-v1-5

## Overview

This plan implements the three-wave enhancement for Monitor Brightness Controller v1.5. Wave 1 fixes startup behavior, profile selection preview, and registry management. Wave 2 introduces an Inno Setup installer. Wave 3 integrates the installer into the build pipeline and updates documentation/versioning. Tasks are ordered so each step builds incrementally on prior work, with property-based tests placed close to the code they validate.

## Tasks

- [x] 1. Wave 1 — Fix Manual Launch Startup Behavior
  - [x] 1.1 Refactor Program.Main to separate manual vs silent launch paths
    - Modify `Program.Main` (or equivalent entry point) so that manual launches (no `--silent` flag) do NOT call `StartupCoordinator.Run()` with auto-apply
    - Manual launch path: create MainWindow, call `Load()` which reads hardware values only
    - Silent launch path: preserve existing behavior calling `ProfileManager.ApplyProfile`
    - Ensure CLI override flags (`--monitor`, `--profile`) skip auto-apply and execute CLI commands only
    - _Requirements: 1.2, 1.3, 2.9_

  - [x] 1.2 Update MainWindowViewModel.Load() for manual launch hardware read
    - On manual launch, `Load()` calls `DetectMonitors()` and populates sliders from `CurrentBrightness` / `CurrentGamma` hardware values
    - Set profile dropdown to null (no selection) on manual launch
    - On DDC/CI read failure for a monitor: set slider to 50, display "unknown", disable slider controls, show error indicator on monitor panel
    - _Requirements: 1.1, 1.4, 1.5_

  - [x] 1.3 Write property test: Manual launch performs no hardware writes (Property 1)
    - **Property 1: Manual launch performs no hardware writes**
    - Generate random AppSettings and random monitor lists; assert no `SetBrightness` or `SetGamma` calls are made on MonitorService during manual launch flow
    - Use FsCheck.Xunit with NSubstitute mocks for MonitorService
    - **Validates: Requirements 1.2, 1.3**

- [x] 2. Wave 1 — Fix Silent Launch Startup Profile Logic
  - [x] 2.1 Update StartupCoordinator.Decide logic for v1.5 requirements
    - Ensure `Decide` returns `ApplyDefaultProfile` when `AutoApplyOnStartup=true` and `DefaultStartupProfileName` exists in profile list
    - Fall back to `ApplyLastProfile` when `DefaultStartupProfileName` is null/empty but `LastAppliedProfileName` exists
    - Return `AutoApplyDisabled` when `AutoApplyOnStartup=false` OR both profile names are null/empty
    - Return `DefaultProfileMissing` with notice when `DefaultStartupProfileName` references a deleted profile
    - Return `CliOverride` when `isCliOverride=true` regardless of other settings
    - _Requirements: 2.2, 2.3, 2.4, 2.5, 2.8, 2.9_

  - [x] 2.2 Implement silent launch execution in Program.RunSilentMode
    - On `ApplyDefaultProfile` or `ApplyLastProfile`: invoke `ProfileManager.ApplyProfile` within 10 seconds of process start
    - On failure: log via Trace, store user-facing notice for next window show, remain in system tray
    - On `DefaultProfileMissing`: reset `DefaultStartupProfileName` to null, persist change, do not apply
    - Start with main window hidden, system tray icon visible only, no taskbar entry
    - _Requirements: 2.1, 2.6, 2.7, 2.8_

  - [x] 2.3 Write property test: Startup decision prioritizes DefaultStartupProfileName (Property 2)
    - **Property 2: Startup decision prioritizes DefaultStartupProfileName**
    - Generate random AppSettings with `AutoApplyOnStartup=true` and existing default profile name; assert `Decide` returns `ApplyDefaultProfile`
    - **Validates: Requirements 2.2**

  - [x] 2.4 Write property test: Startup decision falls back to LastAppliedProfileName (Property 3)
    - **Property 3: Startup decision falls back to LastAppliedProfileName**
    - Generate random AppSettings with null/empty `DefaultStartupProfileName` and existing `LastAppliedProfileName`; assert `Decide` returns `ApplyLastProfile`
    - **Validates: Requirements 2.3**

  - [x] 2.5 Write property test: Startup decision skips apply when disabled or unresolvable (Property 4)
    - **Property 4: Startup decision skips apply when disabled or unresolvable**
    - Generate random AppSettings where `AutoApplyOnStartup=false` or both names are null/empty; assert `Decide` returns `AutoApplyDisabled`
    - **Validates: Requirements 2.4, 2.5**

  - [x] 2.6 Write property test: Startup decision detects missing default profile (Property 5)
    - **Property 5: Startup decision detects missing default profile**
    - Generate random AppSettings with non-existent `DefaultStartupProfileName`; assert `Decide` returns `DefaultProfileMissing` with non-null notice
    - **Validates: Requirements 2.8**

  - [x] 2.7 Write property test: CLI override always skips startup apply (Property 6)
    - **Property 6: CLI override always skips startup apply**
    - Generate random AppSettings and profile lists with `isCliOverride=true`; assert `Decide` returns `CliOverride`
    - **Validates: Requirements 2.9**

- [x] 3. Wave 1 — Fix Registry Management for Start With Windows
  - [x] 3.1 Update StartupRegistration for v1.5 registry sync and overwrite logic
    - On app startup: if registry entry exists but `StartWithWindows` is false in SettingsStore → set to true and persist (sync external installer-created entry)
    - `SetStartWithWindows(true)`: overwrite existing entry if path differs (use current exe path)
    - `SetStartWithWindows(false)`: tolerant of missing entry (already uses `throwOnMissingValue: false`)
    - On registry write failure: return failure result, display error to user, do NOT revert SettingsStore value
    - Compare registry entry value against current exe path on startup and update if paths differ
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 7.1, 7.2, 7.3, 7.4, 7.5_

  - [x] 3.2 Write unit tests for StartupRegistration registry sync behavior
    - Test: registry entry exists, SettingsStore has false → syncs to true
    - Test: enable with different path → overwrites registry entry
    - Test: disable with missing entry → completes without error
    - Test: registry write failure → returns failure, preserves SettingsStore value
    - Test: startup path comparison → updates entry when paths differ
    - Use NSubstitute for IRegistryKeyWrapper, FluentAssertions for assertions
    - _Requirements: 3.1, 3.3, 3.5, 3.6, 3.7, 7.2, 7.4_

- [x] 4. Checkpoint — Wave 1 Verification
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Wave 1 — Fix Profile Selection Preview Behavior
  - [x] 5.1 Implement profile dropdown selection triggers PreviewProfile (UI-only)
    - Profile dropdown `SelectionChanged` → call `PreviewProfile(name)` which loads brightness/gamma slider values without hardware commands
    - Legacy profiles (null `MonitorGammaMap`): leave gamma sliders unchanged
    - "Apply" button → call `ProfileManager.ApplyProfile(name, monitorService)` to send values to hardware
    - Deselect profile (clear selection) → call `RestoreHardwareValues()` to restore sliders from hardware
    - On hardware read failure during deselect: leave sliders at last position, show error message
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6_

  - [x] 5.2 Write property test: Profile preview loads values without hardware commands (Property 7)
    - **Property 7: Profile preview loads values without hardware commands**
    - Generate random profiles with valid brightness/gamma maps and random monitors; assert `PreviewProfile` updates sliders without `SetBrightness`/`SetGamma` calls
    - **Validates: Requirements 4.1, 4.2**

  - [x] 5.3 Write property test: Legacy profile preview preserves gamma sliders (Property 8)
    - **Property 8: Legacy profile preview preserves gamma sliders**
    - Generate random legacy profiles (null `MonitorGammaMap`) and random initial gamma values; assert gamma sliders remain unchanged after `PreviewProfile`
    - **Validates: Requirements 4.3**

  - [x] 5.4 Write property test: Profile deselect restores hardware values (Property 9)
    - **Property 9: Profile deselect restores hardware values**
    - Generate random monitors with hardware values and random profiles; after preview, call `RestoreHardwareValues` and assert sliders match hardware-reported values
    - **Validates: Requirements 4.5**

- [x] 6. Checkpoint — Wave 1 Complete
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Wave 2 — Create Inno Setup Installer Script
  - [x] 7.1 Create MonitorBrightnessControllerSetup.iss at repository root
    - Define `[Setup]` section: AppId (stable GUID), AppName, AppVersion (from `/DMyAppVersion` define), DefaultDirName `{autopf}\MonitorBrightnessController`, OutputDir, OutputBaseFilename `MonitorBrightnessControllerSetup-{#MyAppVersion}`, UninstallDisplayName
    - Define `[Files]` section: include published single-file exe from `dotnet publish` output directory
    - Define `[Icons]` section: Start Menu shortcut (conditional on task), Desktop shortcut (conditional on task)
    - Define `[Tasks]` section: checkboxes for Start Menu shortcut (default checked), Desktop shortcut (default unchecked), "Start with Windows" (default unchecked)
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 8.3, 8.4_

  - [x] 7.2 Add installer uninstall, upgrade, and registry integration logic
    - Define `[Run]`/`[UninstallRun]` or `[Registry]` sections for Auto_Start_Registry_Entry management based on "Start with Windows" task checkbox
    - Register uninstaller in Programs and Features (Inno Setup automatic via `[Setup]` AppId)
    - Uninstall routine removes installed files, shortcuts, and registry entry if present
    - Add `[Code]` section with Pascal Script: CloseApplications (send termination, wait 5s, prompt if still running), upgrade detection via AppId, preserve settings.json at %LOCALAPPDATA%
    - Pre-fill installation directory from previous install, preserve previous shortcut/startup checkbox states
    - _Requirements: 5.7, 5.8, 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7, 7.1_

- [x] 8. Checkpoint — Wave 2 Installer Script Complete
  - Ensure all tests pass, ask the user if questions arise.

- [x] 9. Wave 3 — Build Pipeline Integration
  - [x] 9.1 Update publish/build script to invoke ISCC.exe after dotnet publish
    - After `dotnet publish` completes, invoke `ISCC.exe MonitorBrightnessControllerSetup.iss` with `/DMyAppVersion={version}` sourced from .csproj `Version` property
    - Output installer to `builds/v{VERSION}/` directory
    - Ensure only the installer executable ends up in the builds folder (no raw binaries or PDBs)
    - On ISCC.exe not found or compilation failure: fail the build with clear error message
    - _Requirements: 9.1, 9.2, 9.4, 9.5, 8.1, 8.2, 8.5_

  - [x] 9.2 Verify existing pre-build-docs-check hook continues to work
    - Ensure the existing pre-build documentation/changelog/version validation hook is not broken by new build steps
    - _Requirements: 9.3_

- [x] 10. Wave 3 — Versioning and Documentation Updates
  - [x] 10.1 Bump version to 1.5.0 in .csproj
    - Set `Version` to `1.5.0`, `AssemblyVersion` to `1.5.0.0`, `FileVersion` to `1.5.0.0`
    - _Requirements: 10.2_

  - [x] 10.2 Update CHANGELOG.md with v1.5.0 release section
    - Add `## [1.5.0]` section with release date
    - Organize into subsections: Fixed (Requirements 1–4 bug fixes), Added (installer, build pipeline), Changed (distribution model, registry behavior)
    - _Requirements: 10.1_

  - [x] 10.3 Update README.md Installation and Features sections
    - Update Installation section: document Inno Setup installer as primary method with download instructions
    - Retain "Build from source" subsection and portable single-exe option mention
    - Update Features list and Usage sections to describe new capabilities from v1.5
    - _Requirements: 10.3, 10.5_

  - [x] 10.4 Update in-app Help tab content
    - Add sections for installer features (Requirements 5–9) not already covered
    - Update existing sections whose behavior changed in v1.5.0
    - _Requirements: 10.4_

- [x] 11. Final Checkpoint — All Waves Complete
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation between waves
- Property tests validate universal correctness properties from the design document (Properties 1–9)
- Unit tests validate specific examples, edge cases, and error conditions
- Wave 2 (installer) and Wave 3 (build/docs) tasks involve file creation rather than C# code and are not covered by property tests
- FsCheck.Xunit 2.16.6 is already referenced in the test project; no new dependencies needed

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "2.1", "3.1"] },
    { "id": 1, "tasks": ["1.2", "2.2", "3.2"] },
    { "id": 2, "tasks": ["1.3", "2.3", "2.4", "2.5", "2.6", "2.7"] },
    { "id": 3, "tasks": ["5.1"] },
    { "id": 4, "tasks": ["5.2", "5.3", "5.4"] },
    { "id": 5, "tasks": ["7.1"] },
    { "id": 6, "tasks": ["7.2"] },
    { "id": 7, "tasks": ["9.1", "10.1"] },
    { "id": 8, "tasks": ["9.2", "10.2", "10.3", "10.4"] }
  ]
}
```
