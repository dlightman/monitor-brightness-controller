# Implementation Plan: UI Consolidation

## Overview

This plan consolidates the Monitor Brightness Controller UI from a 4-tab to a 3-tab layout. Implementation proceeds bottom-up: assembly metadata and model changes first, then new ViewModels and XAML, followed by wiring, cleanup, and tests. Each step builds incrementally on the previous to ensure no orphaned code.

## Tasks

- [x] 1. Assembly metadata and build-date setup
  - [x] 1.1 Add MSBuild build-date generation to .csproj
    - Add `<BuildDate>` property and `AssemblyMetadataAttribute` ItemGroup to `MonitorBrightnessController.csproj` so that `[assembly: AssemblyMetadata("BuildDate", "yyyy-MM-dd")]` is emitted at compile time
    - _Requirements: 7.4, 7.5, 7.6_

  - [x] 1.2 Add `AppVersion` and `BuildDate` properties to MainWindowViewModel
    - Read version from `Assembly.GetEntryAssembly().GetName().Version` formatted as "Major.Minor.Patch"
    - Read build date from `AssemblyMetadataAttribute` with key "BuildDate"
    - _Requirements: 7.3, 7.4, 7.5_

- [x] 2. ProfileStripViewModel implementation
  - [x] 2.1 Create `ProfileStripViewModel.cs`
    - Implement `ObservableCollection<string> ProfileNames` (sorted case-insensitive alphabetical)
    - Implement `SelectedProfileName` property with change notification
    - Implement `CanApply`, `CanUpdate`, `CanDelete` computed properties (false when no selection)
    - Implement `ApplyCommand`, `UpdateCommand`, `DeleteCommand`, `SaveAsNewCommand` as RelayCommands
    - Wire `OnProfileSelected` callback for slider preview
    - Wire `CaptureBrightnessMap` / `CaptureGammaMap` funcs for save/update operations
    - Implement `RefreshProfiles()` to reload and sort profile names from `IProfileManager`
    - _Requirements: 3.2, 3.3, 3.4, 3.6, 3.8, 3.9, 3.14_

  - [x] 2.2 Write property test for profile dropdown alphabetical ordering
    - **Property 6: Profile dropdown alphabetical ordering**
    - **Validates: Requirements 3.2, 5.2**

  - [x] 2.3 Write property test for profile name validation and creation
    - **Property 8: Profile name validation and creation persistence**
    - **Validates: Requirements 3.11, 3.12**

  - [x] 2.4 Write property test for profile deletion
    - **Property 9: Profile deletion removes from store and dropdown**
    - **Validates: Requirements 3.8**

  - [x] 2.5 Write property test for profile update
    - **Property 15: Profile update overwrites with current values**
    - **Validates: Requirements 3.6**

- [x] 3. Slider synchronization logic in MainWindowViewModel
  - [x] 3.1 Implement `PreviewProfile(string? profileName)` method
    - For each monitor VM: if the profile's MonitorBrightnessMap contains the monitor's DevicePath, set brightness slider to profile value; otherwise retain current value
    - For each monitor VM: if the profile's MonitorGammaMap is non-null and contains the monitor's DevicePath, set gamma slider to profile value; otherwise retain current value
    - Handle legacy profiles (null MonitorGammaMap) by retaining gamma slider values
    - _Requirements: 2.1, 2.2, 2.3_

  - [x] 3.2 Implement `RestoreHardwareValues()` method
    - Query each monitor via MonitorService for current hardware brightness/gamma
    - For monitors where hardware read succeeds: set sliders to hardware values
    - For monitors where hardware read fails: retain last displayed value
    - _Requirements: 2.4, 2.5_

  - [x] 3.3 Implement startup slider sync in initialization
    - When no valid startup profile applies: set each slider to hardware-reported value
    - When startup profile applies: use PreviewProfile logic for mapped monitors, hardware values for unmapped
    - On DDC/CI read failure: set slider to midpoint (50) and show error indicator
    - _Requirements: 1.1, 1.2, 1.3, 1.4_

  - [x] 3.4 Write property test for startup slider sync without profile
    - **Property 1: Startup slider sync without applicable profile**
    - **Validates: Requirements 1.1, 1.3**

  - [x] 3.5 Write property test for startup slider sync with profile
    - **Property 2: Startup slider sync with profile application**
    - **Validates: Requirements 1.2**

  - [x] 3.6 Write property test for hardware read failure midpoint default
    - **Property 3: Hardware read failure defaults to midpoint**
    - **Validates: Requirements 1.4**

  - [x] 3.7 Write property test for profile selection mapped/unmapped retention
    - **Property 4: Profile selection updates mapped monitors and retains unmapped**
    - **Validates: Requirements 2.1, 2.2, 2.3**

  - [x] 3.8 Write property test for clearing profile selection
    - **Property 5: Clearing profile selection restores hardware values**
    - **Validates: Requirements 2.4, 2.5**

- [x] 4. Checkpoint - Verify core logic
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Startup Profile section logic
  - [x] 5.1 Add startup profile properties to MainWindowViewModel
    - Add `AutoApplyOnStartup` property (persisted via SettingsStore)
    - Add `ObservableCollection<string> StartupProfileOptions` — "Last Used" as first item, then alphabetical profile names
    - Add `SelectedStartupProfileName` property — maps to `DefaultStartupProfileName` (null = "Last Used")
    - Disable dropdown when `AutoApplyOnStartup` is false
    - Persist selection immediately on change
    - Handle profile deletion: if the deleted profile was selected, reset to "Last Used" and persist
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.8, 6.9_

  - [x] 5.2 Update `StartupCoordinator` for "Last Used" semantics
    - When `DefaultStartupProfileName` is null and `AutoApplyOnStartup` is true: apply `LastAppliedProfileName` if it exists and refers to a valid profile
    - When `DefaultStartupProfileName` is a specific name and profile exists: apply that profile
    - When profile not found: skip application, reset to "Last Used", persist
    - When "Last Used" selected but `LastAppliedProfileName` is null: skip, no error
    - _Requirements: 6.6, 6.7, 6.10, 6.11_

  - [x] 5.3 Write property test for startup dropdown ordering
    - **Property 10: Startup dropdown lists "Last Used" first then alphabetical profiles**
    - **Validates: Requirements 6.5**

  - [x] 5.4 Write property test for startup profile application correctness
    - **Property 11: Startup profile application correctness**
    - **Validates: Requirements 6.6, 6.7**

  - [x] 5.5 Write property test for startup profile persistence
    - **Property 12: Startup profile selection persistence**
    - **Validates: Requirements 6.8**

  - [x] 5.6 Write property test for deleted startup profile reset
    - **Property 13: Deleted startup profile resets to "Last Used"**
    - **Validates: Requirements 6.9**

- [x] 6. Create Shortcut section logic
  - [x] 6.1 Add shortcut creation properties and command to MainWindowViewModel
    - Add `ObservableCollection<string> ShortcutProfileOptions` — all profile names alphabetical
    - Add `SelectedShortcutProfile` property (null by default)
    - Add `CanCreateShortcut` computed property (true when SelectedShortcutProfile is not null)
    - Add `CreateShortcutCommand` relay command
    - Add `ShortcutStatusMessage` property for success/error feedback
    - _Requirements: 5.1, 5.2, 5.5, 5.6_

  - [x] 6.2 Implement shortcut creation logic in code-behind
    - Use `WScript.Shell` COM to create .lnk file
    - Present save-file dialog defaulting to Desktop with filename "Brightness - {profileName}.lnk"
    - Set target to application executable path, arguments to `--profile <name>`, working directory to executable's parent folder
    - On success: set ShortcutStatusMessage to indicate created file name
    - On failure: show error message, ensure no partial file left
    - _Requirements: 5.3, 5.4, 5.6, 5.7_

  - [x] 6.3 Write property test for shortcut arguments
    - **Property 14: Shortcut arguments correctly formed**
    - **Validates: Requirements 5.4**

  - [x] 6.4 Write property test for profile apply sends correct values
    - **Property 7: Profile apply sends correct values to mapped monitors**
    - **Validates: Requirements 3.4**

- [x] 7. Checkpoint - Verify ViewModel logic
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. XAML restructure — Monitors tab with Profile Strip
  - [x] 8.1 Create `ProfileStrip.xaml` UserControl
    - Layout: single-row horizontal StackPanel/DockPanel with ComboBox and 4 buttons
    - Bind ComboBox ItemsSource to `ProfileNames`, SelectedItem to `SelectedProfileName`
    - Bind button IsEnabled to `CanApply`, `CanUpdate`, `CanDelete` (Save As New always enabled)
    - Bind button Commands to the respective relay commands
    - _Requirements: 3.1, 3.2, 3.3, 3.9, 3.14_

  - [x] 8.2 Create `ProfileStrip.xaml.cs` code-behind
    - Implement confirmation dialog for Delete button
    - Implement popup input dialog for Save As New (1–64 chars, `[a-zA-Z0-9_-]` validation, duplicate check)
    - Display error messages for validation failures, max profile count
    - _Requirements: 3.7, 3.10, 3.11, 3.13_

  - [x] 8.3 Integrate ProfileStrip into Monitors tab in `MainWindow.xaml`
    - Place ProfileStrip below the monitor slider controls
    - Wire DataContext to ProfileStripViewModel instance
    - _Requirements: 3.1_

- [x] 9. XAML restructure — Settings tab
  - [x] 9.1 Add Startup Profile section to Settings tab XAML
    - Add "Startup Profile" group with checkbox "Auto apply profile on start" and Startup_Profile_Dropdown
    - Bind checkbox to `AutoApplyOnStartup`
    - Bind dropdown ItemsSource to `StartupProfileOptions`, SelectedItem to `SelectedStartupProfileName`
    - Set dropdown IsEnabled bound to `AutoApplyOnStartup`
    - Style "Last Used" item in italic
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5_

  - [x] 9.2 Add Create Shortcut section to Settings tab XAML
    - Add "Create Shortcut" group with profile dropdown and "Create Shortcut" button
    - Bind dropdown to `ShortcutProfileOptions` / `SelectedShortcutProfile`
    - Bind button IsEnabled to `CanCreateShortcut`, Command to `CreateShortcutCommand`
    - Add TextBlock for `ShortcutStatusMessage`
    - _Requirements: 5.1, 5.2, 5.5, 5.6_

- [x] 10. XAML restructure — About tab and tab order
  - [x] 10.1 Create About tab content in MainWindow.xaml
    - Add third TabItem with Header "About"
    - Display version bound to `AppVersion`
    - Display build date bound to `BuildDate`
    - Add Hyperlink to `https://github.com/dlightman/monitor-brightness-controller` with click handler to open in default browser
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.6_

  - [x] 10.2 Remove Profiles tab and Help tab, set tab order
    - Remove the "Profiles" TabItem entirely from MainWindow.xaml
    - Remove the "Help" TabItem entirely from MainWindow.xaml
    - Ensure TabControl contains exactly 3 tabs: "Monitors", "Settings", "About"
    - Set `SelectedIndex="0"` on TabControl so Monitors tab is selected on launch
    - _Requirements: 4.1, 4.2, 4.3, 8.1, 8.2, 8.3_

- [x] 11. Wiring and code-behind updates
  - [x] 11.1 Update `MainWindow.xaml.cs` to wire ProfileStripViewModel
    - Remove old `WireProfilePanel()` and `PopulateHelp()` methods
    - Add `WireProfileStrip()` that connects ProfileStripViewModel callbacks:
      - `OnProfileSelected` → `MainWindowViewModel.PreviewProfile`
      - `CaptureBrightnessMap` / `CaptureGammaMap` → reads from MonitorControlViewModels
    - Wire shortcut creation command to code-behind COM logic
    - Wire `RefreshAllProfileDropdowns()` after profile create/delete in ProfileStripViewModel
    - _Requirements: 3.4, 3.6, 3.8, 3.12_

  - [x] 11.2 Remove old ProfilePanel and ProfilePanelViewModel files
    - Delete `ProfilePanel.xaml`, `ProfilePanel.xaml.cs`, `ProfilePanelViewModel.cs`
    - Remove any references/usings in MainWindow or App.xaml
    - _Requirements: 4.1_

- [x] 12. Checkpoint - Full build and UI verification
  - Ensure all tests pass, ask the user if questions arise.

- [x] 13. Unit and integration tests
  - [x] 13.1 Write unit tests for tab structure
    - Verify exactly 3 tabs with headers "Monitors", "Settings", "About" in order
    - Verify no "Profiles" or "Help" tab exists
    - Verify Monitors tab is selected by default
    - _Requirements: 4.1, 4.2, 4.3, 8.1, 8.2, 8.3_

  - [x] 13.2 Write unit tests for button enabled/disabled states
    - Verify Apply, Update, Delete disabled when no profile selected
    - Verify Create Shortcut button disabled when no profile selected
    - Verify Save As New always enabled
    - _Requirements: 3.14, 5.5, 3.9_

  - [x] 13.3 Write unit tests for startup profile section behavior
    - Verify dropdown disabled when AutoApplyOnStartup is unchecked
    - Verify dropdown enabled when AutoApplyOnStartup is checked
    - Verify "Last Used" with null LastAppliedProfileName skips application
    - Verify missing startup profile resets to "Last Used" and persists
    - _Requirements: 6.3, 6.4, 6.10, 6.11_

  - [x] 13.4 Write unit tests for About tab content
    - Verify version format "Major.Minor.Patch"
    - Verify build date format "yyyy-MM-dd"
    - Verify hyperlink URL is correct
    - _Requirements: 7.2, 7.3, 7.4_

  - [x] 13.5 Write integration test for assembly metadata
    - Verify assembly version and BuildDate metadata are readable at runtime
    - _Requirements: 7.5, 7.6_

- [x] 14. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties using FsCheck with xUnit
- Unit tests validate specific examples and edge cases
- The project uses WPF/.NET 8, C#, MVVM pattern, xUnit + FsCheck.Xunit for testing
- ProfileStripViewModel replaces the old ProfilePanelViewModel entirely
- COM interop for shortcut creation stays in code-behind (not ViewModel-testable)

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "2.1"] },
    { "id": 1, "tasks": ["1.2", "2.2", "2.3", "2.4", "2.5", "3.1"] },
    { "id": 2, "tasks": ["3.2", "3.3", "5.1"] },
    { "id": 3, "tasks": ["3.4", "3.5", "3.6", "3.7", "3.8", "5.2"] },
    { "id": 4, "tasks": ["5.3", "5.4", "5.5", "5.6", "6.1"] },
    { "id": 5, "tasks": ["6.2", "6.3", "6.4"] },
    { "id": 6, "tasks": ["8.1", "8.2", "9.1", "9.2"] },
    { "id": 7, "tasks": ["8.3", "10.1", "10.2"] },
    { "id": 8, "tasks": ["11.1", "11.2"] },
    { "id": 9, "tasks": ["13.1", "13.2", "13.3", "13.4", "13.5"] }
  ]
}
```
