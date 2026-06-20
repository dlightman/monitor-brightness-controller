namespace MonitorBrightnessController.Models;

/// <summary>
/// Describes the outcome of a startup registry synchronization check.
/// Used by <see cref="Infrastructure.StartupRegistration.EnsureRegistration"/> to communicate
/// whether the SettingsStore needs updating after registry state reconciliation.
/// </summary>
/// <param name="SettingsNeedSync">
/// True when an external registry entry was detected (e.g. created by the installer)
/// while the SettingsStore had StartWithWindows set to false. The caller should persist
/// StartWithWindows = true.
/// </param>
/// <param name="PathWasUpdated">
/// True when the registry entry existed but with a different executable path and was
/// overwritten with the current path.
/// </param>
public readonly record struct RegistrySyncResult(bool SettingsNeedSync, bool PathWasUpdated);
