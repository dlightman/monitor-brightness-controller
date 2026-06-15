using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;

namespace MonitorBrightnessController.Presentation;

/// <summary>
/// View model backing the compact profile strip on the Monitors tab. Surfaces the list of
/// stored profiles sorted alphabetically, coordinates CRUD + apply operations through
/// <see cref="IProfileManager"/>, and signals the main view model for slider preview on selection.
/// </summary>
public sealed class ProfileStripViewModel : ViewModelBase
{
    private readonly IProfileManager _profileManager;
    private readonly IMonitorService _monitorService;

    private string? _selectedProfileName;

    public ProfileStripViewModel(
        IProfileManager profileManager,
        IMonitorService monitorService)
    {
        _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
        _monitorService = monitorService ?? throw new ArgumentNullException(nameof(monitorService));

        ApplyCommand = new RelayCommand(ApplySelectedProfile, () => CanApply);
        UpdateCommand = new RelayCommand(UpdateSelectedProfile, () => CanUpdate);
        DeleteCommand = new RelayCommand(DeleteSelectedProfile, () => CanDelete);
        SaveAsNewCommand = new RelayCommand(SaveAsNewProfile);

        RefreshProfiles();
    }

    /// <summary>
    /// All saved profile names sorted case-insensitive alphabetically.
    /// </summary>
    public ObservableCollection<string> ProfileNames { get; } = new();

    /// <summary>
    /// The currently selected profile name, or null when no profile is selected.
    /// </summary>
    public string? SelectedProfileName
    {
        get => _selectedProfileName;
        set
        {
            if (_selectedProfileName == value) return;
            _selectedProfileName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanApply));
            OnPropertyChanged(nameof(CanUpdate));
            OnPropertyChanged(nameof(CanDelete));
            ApplyCommand.RaiseCanExecuteChanged();
            UpdateCommand.RaiseCanExecuteChanged();
            DeleteCommand.RaiseCanExecuteChanged();
            OnProfileSelected?.Invoke(value);
        }
    }

    /// <summary>True when a profile is selected and can be applied.</summary>
    public bool CanApply => !string.IsNullOrEmpty(_selectedProfileName);

    /// <summary>True when a profile is selected and can be updated.</summary>
    public bool CanUpdate => !string.IsNullOrEmpty(_selectedProfileName);

    /// <summary>True when a profile is selected and can be deleted.</summary>
    public bool CanDelete => !string.IsNullOrEmpty(_selectedProfileName);

    /// <summary>Command to apply the selected profile to monitors.</summary>
    public RelayCommand ApplyCommand { get; }

    /// <summary>Command to update the selected profile with current slider values.</summary>
    public RelayCommand UpdateCommand { get; }

    /// <summary>Command to delete the selected profile.</summary>
    public RelayCommand DeleteCommand { get; }

    /// <summary>Command to save a new profile (always enabled).</summary>
    public RelayCommand SaveAsNewCommand { get; }

    /// <summary>
    /// Callback invoked when the selected profile changes, enabling the main view model
    /// to preview the selected profile's slider values. Passes null when selection is cleared.
    /// </summary>
    public Action<string?>? OnProfileSelected { get; set; }

    /// <summary>
    /// Captures the current brightness slider values from the monitor controls.
    /// Returns a map of device path to brightness value.
    /// </summary>
    public Func<Dictionary<string, int>>? CaptureBrightnessMap { get; set; }

    /// <summary>
    /// Captures the current gamma slider values from the monitor controls.
    /// Returns a map of device path to gamma value.
    /// </summary>
    public Func<Dictionary<string, int>>? CaptureGammaMap { get; set; }

    /// <summary>
    /// Callback invoked after a profile is created or deleted, allowing the main view model
    /// to refresh all profile-related dropdowns (startup profile, shortcut profile, etc.).
    /// </summary>
    public Action? OnProfilesChanged { get; set; }

    /// <summary>
    /// Reloads profile names from the profile manager and sorts them case-insensitive alphabetically.
    /// Preserves the current selection if it still exists after refresh.
    /// </summary>
    public void RefreshProfiles()
    {
        string? previousSelection = _selectedProfileName;

        ProfileNames.Clear();

        var sortedNames = _profileManager.GetAllProfiles()
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (string name in sortedNames)
        {
            ProfileNames.Add(name);
        }

        // Restore selection if still valid, otherwise clear it
        if (previousSelection is not null &&
            ProfileNames.Contains(previousSelection, StringComparer.OrdinalIgnoreCase))
        {
            // Find the exact-case name from the refreshed list
            SelectedProfileName = ProfileNames.FirstOrDefault(
                n => string.Equals(n, previousSelection, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            SelectedProfileName = null;
        }
    }

    /// <summary>Applies the selected profile to all connected mapped monitors.</summary>
    private void ApplySelectedProfile()
    {
        if (string.IsNullOrEmpty(_selectedProfileName)) return;

        _profileManager.ApplyProfile(_selectedProfileName, _monitorService);
    }

    /// <summary>Updates the selected profile with the current slider brightness and gamma values.</summary>
    private void UpdateSelectedProfile()
    {
        if (string.IsNullOrEmpty(_selectedProfileName)) return;

        var brightnessMap = CaptureBrightnessMap?.Invoke() ?? new Dictionary<string, int>();
        var gammaMap = CaptureGammaMap?.Invoke();

        _profileManager.UpdateProfile(
            _selectedProfileName,
            brightnessMap,
            gammaMap is not null ? gammaMap : null);
    }

    /// <summary>
    /// Deletes the selected profile. The code-behind intercepts this command to show a
    /// confirmation dialog, then calls <see cref="ConfirmDeleteSelectedProfile"/> on confirmation.
    /// </summary>
    private void DeleteSelectedProfile()
    {
        // The code-behind handles the confirmation dialog; this is a no-op placeholder.
        // The code-behind will call ConfirmDeleteSelectedProfile() after user confirms.
    }

    /// <summary>
    /// Saves a new profile. The actual name input and validation is handled by the code-behind
    /// (popup dialog), which calls <see cref="CreateProfile"/> with the validated name.
    /// </summary>
    private void SaveAsNewProfile()
    {
        // The code-behind handles the dialog; this is a no-op placeholder.
        // The code-behind will call CreateProfile(name) after dialog confirmation.
    }

    /// <summary>
    /// Creates a new profile with the given name using current slider values.
    /// Called by the code-behind after the user confirms a valid name in the dialog.
    /// </summary>
    /// <param name="name">The validated profile name.</param>
    /// <returns>The result of the creation operation.</returns>
    public Result<Unit> CreateProfile(string name)
    {
        var brightnessMap = CaptureBrightnessMap?.Invoke() ?? new Dictionary<string, int>();
        var gammaMap = CaptureGammaMap?.Invoke();

        Result<Unit> result = _profileManager.CreateProfile(name, brightnessMap, gammaMap);
        if (result.IsSuccess)
        {
            RefreshProfiles();
            SelectedProfileName = name;
            OnProfilesChanged?.Invoke();
        }

        return result;
    }

    /// <summary>
    /// Deletes the selected profile. Called by the code-behind after the user confirms deletion.
    /// </summary>
    /// <returns>The result of the deletion operation.</returns>
    public Result<Unit> ConfirmDeleteSelectedProfile()
    {
        if (string.IsNullOrEmpty(_selectedProfileName))
            return Result<Unit>.Failure("No profile selected.");

        Result<Unit> result = _profileManager.DeleteProfile(_selectedProfileName);
        if (result.IsSuccess)
        {
            RefreshProfiles();
            OnProfilesChanged?.Invoke();
        }

        return result;
    }
}
