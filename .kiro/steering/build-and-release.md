---
inclusion: always
---

# Build, Versioning, and Release Rules

These rules MUST be followed for every feature, bug fix, or change that results in a new build.

## Versioning

- The current version is tracked in the `.csproj` file via `<Version>`, `<AssemblyVersion>`, and `<FileVersion>` properties.
- The next build version is **1.0.3**. After that, increment the patch version (1.0.4, 1.0.5, etc.) for each subsequent build unless instructed otherwise.
- The version in the `.csproj` MUST match the build output folder name and the CHANGELOG entry.

## Build Output

- All release builds MUST be published to `./builds/v{VERSION}/MonitorBrightnessController.exe`.
- The build command is:
  ```
  dotnet publish MonitorBrightnessController/MonitorBrightnessController.csproj -c Release -o ./builds/v{VERSION}
  ```
- The exe filename is always `MonitorBrightnessController.exe` (never renamed with a version suffix).
- The Windows file properties (right-click → Properties → Details) MUST show the correct version. This is accomplished by the `<Version>`, `<AssemblyVersion>`, and `<FileVersion>` properties in the `.csproj`.

## Documentation Requirements (BEFORE building)

Before creating any new build, documentation MUST be updated in **both** places:

### 1. External Documentation (README.md and docs/)
- New features or changed behavior must be reflected in `README.md`.
- Update the relevant sections (Features, Usage, Settings, CLI, Troubleshooting, etc.).
- Add or update screenshots in `docs/screenshots/` if UI changed.

### 2. In-App Help (Presentation/MainWindow.xaml.cs → PopulateHelp())
- The `PopulateHelp()` method in `MainWindow.xaml.cs` contains the in-app Help tab content.
- Any new feature or behavioral change visible to the user MUST be documented there.
- Keep the format consistent: bold section headers, bullet-point descriptions.

## CHANGELOG

- A `CHANGELOG.md` file is maintained at the repository root.
- It follows the [Keep a Changelog](https://keepachangelog.com/) format.
- Every new build MUST have a corresponding entry in the CHANGELOG with:
  - The version number and date: `## [x.y.z] - YYYY-MM-DD`
  - Sections as needed: `### Added`, `### Changed`, `### Fixed`, `### Removed`
- The CHANGELOG entry MUST be written BEFORE the build is produced.

## Build Process (in order)

1. Implement the feature or fix.
2. Update external documentation (README.md, docs/).
3. Update in-app help (PopulateHelp() in MainWindow.xaml.cs).
4. Add a CHANGELOG entry for the new version.
5. Update version properties in the `.csproj` (`<Version>`, `<AssemblyVersion>`, `<FileVersion>`).
6. Run `dotnet publish` to `./builds/v{VERSION}/`.
7. Verify the build runs and file properties show the correct version.
