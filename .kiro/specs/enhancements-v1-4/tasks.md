# Implementation Plan: Enhancements v1.4

## Overview

This plan implements six enhancements for Monitor Brightness Controller v1.4: silent startup mode, startup registration with `--silent`, monitors tab initial state display, Help tab with documentation, auto-update notification, and auto-update check setting. Each task builds incrementally on the existing layered architecture, wiring new components into the existing dependency graph.

## Tasks

- [x] 1. Extend CLI parsing and models for silent mode
  - [x] 1.1 Add `Silent` property to `ParsedCliArguments` and update `CliHandler.ParseArguments` to recognize `--silent`
    - Add `public bool Silent { get; init; }` to the `ParsedCliArguments` record in `Application/CliHandler.cs`
    - Modify `ParseArguments` to consume `--silent` at any position (before, between, or after other args), setting `Silent = true`
    - When `--silent` is the only argument, return a successful parse with no commands and no profile (not an error)
    - Update `ParsedCliArguments.Success` factory to accept a `silent` parameter
    - Update `UsageText` to document `--silent`
    - _Requirements: 1.1, 1.7_

  - [x] 1.2 Write property test: silent flag parsing preserves coexisting arguments
    - **Property 1: Silent flag parsing preserves coexisting arguments**
    - Generate random valid arg arrays with `--silent` inserted at random positions; verify `Silent == true` and other fields match the non-silent parse
    - Create `CliParserSilentPropertyTests.cs` in the test project
    - Use FsCheck.Xunit `[Property]` attribute with minimum 100 iterations
    - **Validates: Requirements 1.7**

  - [x] 1.3 Write unit tests for `--silent` CLI parsing
    - Test `--silent` alone → valid, `Silent = true`, no commands, no profile
    - Test `--silent` combined with `--profile Night` → both parsed correctly
    - Test `--silent` combined with `--monitor` commands → both parsed correctly
    - Test `--silent` at different positions (first, middle, last)
    - Test `--silent` with unknown args → error
    - _Requirements: 1.1, 1.4, 1.7_

- [x] 2. Implement silent startup dispatch in Program.Main
  - [x] 2.1 Modify `Program.Main` to handle `--silent` argument
    - Update `IsCliInvocation` to not treat `--silent`-only invocation as CLI mode (only `--monitor`/`--profile` trigger CLI mode)
    - After determining non-CLI invocation, parse args to check for `Silent` flag
    - When `--silent` is present: load settings, optionally apply startup profile, create App and MainWindow but do not call `window.Show()`, initialize system tray immediately
    - When `--silent` is combined with `--monitor`/`--profile`: execute CLI commands first, then enter silent mode (create hidden window + tray)
    - Set MainWindow `Visibility` to `Collapsed` in silent mode
    - Handle profile auto-apply failure: remain in tray, log error for later viewing
    - _Requirements: 1.1, 1.2, 1.3, 1.5, 1.6, 1.7_

  - [x] 2.2 Write unit tests for silent startup logic
    - Test silent mode creates hidden window with tray icon
    - Test silent mode with AutoApply applies startup profile
    - Test silent mode with failed profile remains in tray without error window
    - Test non-silent mode without CLI args shows window normally
    - _Requirements: 1.1, 1.2, 1.4, 1.6_

- [x] 3. Update StartupRegistration to include `--silent`
  - [x] 3.1 Modify `StartupRegistration.SetStartWithWindows` and `EnsureRegistration` to use `--silent` format
    - In `SetStartWithWindows(true)`: change registry value from `"<exePath>"` to `"<exePath>" --silent`
    - In `EnsureRegistration`: change expected value to `"<currentExePath>" --silent` and overwrite if mismatch
    - Update `UpdateRegisteredPath` to include `--silent` in the value
    - _Requirements: 2.1, 2.2, 2.3, 2.4_

  - [x] 3.2 Write property test: startup registration value format
    - **Property 2: Startup registration value format**
    - Generate random valid Windows executable paths; verify `SetStartWithWindows(true)` writes `"<path>" --silent`
    - Create `StartupRegistrationPropertyTests.cs` in the test project
    - Use NSubstitute to mock `IRegistryKeyWrapper`
    - **Validates: Requirements 2.1**

  - [x] 3.3 Write property test: EnsureRegistration corrects mismatched values
    - **Property 3: EnsureRegistration corrects mismatched values**
    - Generate random paths and random existing registry values that don't match expected format; verify correction to `"<current_exe_path>" --silent`
    - Add to `StartupRegistrationPropertyTests.cs`
    - **Validates: Requirements 2.3**

  - [x] 3.4 Write unit tests for StartupRegistration `--silent` behavior
    - Test enable writes correct `"<path>" --silent` format
    - Test disable removes entry, succeeds even if entry missing
    - Test EnsureRegistration overwrites value missing `--silent`
    - Test EnsureRegistration overwrites value with wrong path
    - Test EnsureRegistration does nothing when value is correct
    - Test registry inaccessible returns failure result
    - _Requirements: 2.1, 2.2, 2.3, 2.4_

- [x] 4. Checkpoint - Ensure CLI and startup registration tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Add `CheckForUpdatesOnStartup` setting to AppSettings and SettingsStore
  - [x] 5.1 Extend `AppSettings` with `CheckForUpdatesOnStartup` property
    - Add `public bool CheckForUpdatesOnStartup { get; init; } = true;` to `Models/AppSettings.cs`
    - The `= true` default handles new installs (no file) and upgrades (missing property deserialized as default)
    - _Requirements: 6.1, 6.4, 6.5_

  - [x] 5.2 Write property test: AppSettings CheckForUpdatesOnStartup round-trip
    - **Property 6: AppSettings CheckForUpdatesOnStartup round-trip**
    - Generate random `AppSettings` instances; serialize to JSON and deserialize back; verify `CheckForUpdatesOnStartup` value is preserved
    - Create `SettingsRoundTripPropertyTests.cs` in the test project
    - **Validates: Requirements 6.1**

  - [x] 5.3 Write unit tests for AppSettings deserialization
    - Test deserialization of JSON missing `CheckForUpdatesOnStartup` → defaults to `true`
    - Test deserialization with explicit `false` → preserves `false`
    - Test deserialization with explicit `true` → preserves `true`
    - _Requirements: 6.4, 6.5_

- [x] 6. Implement UpdateChecker and GitHubReleaseClient
  - [x] 6.1 Create `IUpdateChecker` interface and `UpdateCheckResult` model
    - Create `Interfaces/IUpdateChecker.cs` with `Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken)`
    - Create `Models/UpdateCheckResult.cs` as `record UpdateCheckResult(bool IsUpdateAvailable, string? LatestVersion, string? ReleaseUrl)`
    - _Requirements: 5.1, 5.2_

  - [x] 6.2 Create `IGitHubReleaseClient` interface and `GitHubRelease` model
    - Create `Interfaces/IGitHubReleaseClient.cs` with `Task<GitHubRelease?> GetLatestReleaseAsync(CancellationToken)`
    - Create `Models/GitHubRelease.cs` as `record GitHubRelease(string TagName, string HtmlUrl)`
    - _Requirements: 5.1_

  - [x] 6.3 Implement `GitHubReleaseClient` in Infrastructure
    - Create `Infrastructure/GitHubReleaseClient.cs` implementing `IGitHubReleaseClient`
    - Use `HttpClient` to GET `https://api.github.com/repos/dlightman/monitor-brightness-controller/releases/latest`
    - Set User-Agent header (required by GitHub API)
    - Deserialize JSON response to extract `tag_name` and `html_url`
    - Handle exceptions gracefully (return null on failure)
    - _Requirements: 5.1, 5.5_

  - [x] 6.4 Implement `UpdateChecker` in Application
    - Create `Application/UpdateChecker.cs` implementing `IUpdateChecker`
    - Accept `IGitHubReleaseClient` and `Version currentVersion` in constructor
    - Strip leading 'v' and pre-release suffixes from `tag_name`, parse as `System.Version`
    - Compare major.minor.patch only to determine if newer
    - Set HttpClient timeout to 10 seconds
    - Return `UpdateCheckResult` with `IsUpdateAvailable`, `LatestVersion`, `ReleaseUrl`
    - On any exception (network, parse, timeout): return `IsUpdateAvailable = false`
    - _Requirements: 5.1, 5.4, 5.5, 5.6, 5.7_

  - [x] 6.5 Write property test: semantic version comparison
    - **Property 5: Semantic version comparison**
    - Generate random semver triples (major.minor.patch) with optional pre-release suffixes; verify comparison uses only numeric components and ignores suffixes
    - Create `VersionComparisonPropertyTests.cs` in the test project
    - **Validates: Requirements 5.2, 5.6**

  - [x] 6.6 Write unit tests for UpdateChecker
    - Test newer version available → `IsUpdateAvailable = true` with correct version and URL
    - Test same version → `IsUpdateAvailable = false`
    - Test older version on server → `IsUpdateAvailable = false`
    - Test network failure → graceful `IsUpdateAvailable = false`
    - Test timeout (>10s) → graceful `IsUpdateAvailable = false`
    - Test invalid JSON response → graceful `IsUpdateAvailable = false`
    - Test version with 'v' prefix stripped correctly
    - Test version with pre-release suffix ignored
    - Use NSubstitute to mock `IGitHubReleaseClient`
    - _Requirements: 5.1, 5.2, 5.5, 5.6, 5.7_

- [x] 7. Checkpoint - Ensure update checker and settings tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. Implement Monitors Tab initial state display
  - [x] 8.1 Update `MainWindowViewModel.Load()` to populate monitor values on first load
    - If AutoApplyOnStartup is true and DefaultStartupProfileName references a valid profile that was applied: use profile values for matched monitors, DDC/CI reads for unmatched
    - If no profiles exist or AutoApply is off: read live brightness/gamma from each monitor via `MonitorService.DetectMonitors()` and populate slider values
    - Ensure `MonitorControlGroup` sliders are initialized from actual values, not defaults of 0
    - Handle DDC/CI failure: show error indicator for that monitor's entry
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6_

  - [x] 8.2 Write property test: monitor initial state resolution
    - **Property 4: Monitor initial state resolution**
    - Generate random profiles (brightness/gamma maps) and random sets of connected monitors; verify resolution: profile value for matched monitors, live DDC/CI value for unmatched
    - Create `MonitorInitStatePropertyTests.cs` in the test project
    - **Validates: Requirements 3.1, 3.6**

  - [x] 8.3 Write unit tests for monitors tab initialization
    - Test with applied profile → uses profile values for matched monitors
    - Test no profiles → reads live DDC/CI values
    - Test AutoApply=false → reads live DDC/CI values
    - Test DDC/CI failure → shows error indicator
    - Test profile applied but monitor not in profile → uses live DDC/CI value
    - Use NSubstitute to mock `IMonitorService`
    - _Requirements: 3.1, 3.2, 3.3, 3.5, 3.6_

- [x] 9. Implement Help Tab
  - [x] 9.1 Create `HelpTab` UserControl with scrollable documentation sections
    - Create `Presentation/HelpTab.xaml` as a UserControl with `ScrollViewer` wrapping a `StackPanel`
    - Create `Presentation/HelpTab.xaml.cs` code-behind
    - Add documentation sections with bold headings and wrapped body text for: Monitor Brightness & Gamma Control, Profiles, Smooth Transitions, System Tray Behavior, Startup Settings, CLI Usage, Silent Startup Mode, Auto-Update Notifications, Shortcut Creation, Proper Install
    - Ensure accessible, readable formatting with consistent section structure
    - _Requirements: 4.2, 4.3, 4.4, 4.6_

  - [x] 9.2 Add Help tab to MainWindow TabControl
    - Insert `<TabItem Header="Help"><p:HelpTab /></TabItem>` in `MainWindow.xaml` after the Settings tab and before the About tab
    - _Requirements: 4.1_

  - [x] 9.3 Write unit tests for Help tab content
    - Verify all 10 required documentation sections are present in the HelpTab
    - Verify each section has a heading and description
    - _Requirements: 4.2, 4.4_

- [x] 10. Implement auto-update notification UI and wiring
  - [x] 10.1 Add update notification banner to MainWindow
    - Add a `Border` element at the top of the main `Grid` (above the TabControl) for the update notification
    - Bind visibility to `IsUpdateAvailable` ViewModel property
    - Display `LatestVersionText` (e.g., "Version X.Y.Z is available")
    - Include a clickable `Hyperlink` bound to `UpdateReleaseUrl` that opens in default browser
    - Include a dismiss button bound to `DismissUpdateCommand`
    - Banner is non-modal, doesn't block UI interaction
    - _Requirements: 5.2, 5.3, 5.4_

  - [x] 10.2 Add update-related properties and commands to MainWindowViewModel
    - Add `IsUpdateAvailable` (bool), `LatestVersionText` (string), `UpdateReleaseUrl` (string) bindable properties
    - Add `DismissUpdateCommand` (ICommand) that sets `IsUpdateAvailable = false`
    - Add `OpenReleaseUrlCommand` that opens the URL in default browser via `Process.Start`
    - _Requirements: 5.2, 5.3_

  - [x] 10.3 Wire UpdateChecker into MainWindowViewModel startup
    - In `MainWindowViewModel.Load()`, after UI initialization: check `settings.CheckForUpdatesOnStartup`
    - If enabled, call `IUpdateChecker.CheckForUpdateAsync()` asynchronously (fire-and-forget, no UI blocking)
    - On result: if `IsUpdateAvailable`, populate notification properties
    - Ensure only one check per launch
    - _Requirements: 5.1, 5.7_

  - [x] 10.4 Add "Check for updates on startup" checkbox to Settings tab
    - Add a `CheckBox` in the Settings tab after existing startup-related settings
    - Bind to `CheckForUpdatesOnStartup` property on ViewModel
    - On toggle: persist new value immediately via SettingsStore
    - _Requirements: 6.2, 6.3_

  - [x] 10.5 Write unit tests for update notification UI logic
    - Test CheckForUpdatesOnStartup=true triggers update check
    - Test CheckForUpdatesOnStartup=false skips update check
    - Test newer version found sets `IsUpdateAvailable=true` with correct text
    - Test dismiss command hides notification
    - Test only one check per launch
    - _Requirements: 5.1, 5.2, 5.7, 6.2_

- [x] 11. Update project version and integrate all components
  - [x] 11.1 Update project version to 1.4.0
    - Update `Version`, `AssemblyVersion`, and `FileVersion` in `MonitorBrightnessController.csproj` to 1.4.0
    - Wire `IUpdateChecker` and `IGitHubReleaseClient` into the dependency graph in `Program.Main`
    - Pass `IUpdateChecker` to `MainWindowViewModel` constructor
    - Ensure silent mode, update check, help tab, and monitors tab initialization all work together in the full startup flow
    - _Requirements: 5.1, 5.2_

  - [x] 11.2 Update external documentation (README/docs) with v1.4 features
    - Document silent startup mode, auto-update notifications, Help tab, and new settings
    - Cover the same topics as the Help tab content
    - _Requirements: 4.5_

- [x] 12. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- NSubstitute is used for mocking interfaces (`IRegistryKeyWrapper`, `IMonitorService`, `IGitHubReleaseClient`)
- No real network calls in unit/property tests — mock HttpClient via `IGitHubReleaseClient`
- FsCheck.Xunit with `[Property]` attribute, minimum 100 iterations per property test

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "5.1", "6.1", "6.2"] },
    { "id": 1, "tasks": ["1.2", "1.3", "5.2", "5.3", "6.3", "6.4"] },
    { "id": 2, "tasks": ["2.1", "3.1", "6.5", "6.6"] },
    { "id": 3, "tasks": ["2.2", "3.2", "3.3", "3.4", "8.1"] },
    { "id": 4, "tasks": ["8.2", "8.3", "9.1"] },
    { "id": 5, "tasks": ["9.2", "9.3", "10.1", "10.2"] },
    { "id": 6, "tasks": ["10.3", "10.4"] },
    { "id": 7, "tasks": ["10.5", "11.1"] },
    { "id": 8, "tasks": ["11.2"] }
  ]
}
```
