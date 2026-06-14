using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MonitorBrightnessController.Application;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;

namespace MonitorBrightnessController.Presentation;

/// <summary>
/// View model backing the <see cref="ProfilePanel"/>. Surfaces the list of stored profiles
/// and coordinates CRUD + apply operations through an injected <see cref="IProfileManager"/>.
/// </summary>
public sealed class ProfilePanelViewModel : ViewModelBase
{
    private readonly IProfileManager _profileManager;
    private readonly IMonitorService? _monitorService;
    private readonly Func<IReadOnlyDictionary<string, int>> _captureBrightnessMap;

    private string _newProfileName = string.Empty;
    private string? _validationError;
    private string? _statusMessage;
    private string? _selectedProfileName;

    public ProfilePanelViewModel(
        IProfileManager profileManager,
        Func<IReadOnlyDictionary<string, int>>? captureBrightnessMap = null,
        IMonitorService? monitorService = null)
    {
        _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
        _captureBrightnessMap = captureBrightnessMap ?? (() => new Dictionary<string, int>());
        _monitorService = monitorService;
        Refresh();
    }

    public ObservableCollection<string> ProfileNames { get; } = new();

    public string NewProfileName
    {
        get => _newProfileName;
        set
        {
            value ??= string.Empty;
            if (_newProfileName == value) return;
            _newProfileName = value;
            OnPropertyChanged();
            ValidationError = ValidateName(value);
            StatusMessage = null;
        }
    }

    public string? SelectedProfileName
    {
        get => _selectedProfileName;
        set
        {
            if (_selectedProfileName == value) return;
            _selectedProfileName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanDelete));
        }
    }

    public string? ValidationError
    {
        get => _validationError;
        private set
        {
            if (_validationError == value) return;
            _validationError = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasValidationError));
        }
    }

    public bool HasValidationError => !string.IsNullOrEmpty(_validationError);

    public string? StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage == value) return;
            _statusMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasStatusMessage));
        }
    }

    public bool HasStatusMessage => !string.IsNullOrEmpty(_statusMessage);

    public bool CanDelete => !string.IsNullOrEmpty(_selectedProfileName);

    /// <summary>Sets a status message (used by the code-behind for shortcut creation feedback).</summary>
    public void SetStatus(string message) => StatusMessage = message;

    /// <summary>Sets a validation/error message (used by the code-behind for shortcut errors).</summary>
    public void SetError(string message) => ValidationError = message;

    public void Refresh()
    {
        string? previousSelection = _selectedProfileName;
        ProfileNames.Clear();
        foreach (Profile profile in _profileManager.GetAllProfiles())
        {
            ProfileNames.Add(profile.Name);
        }
        SelectedProfileName = ProfileNames.Contains(previousSelection ?? string.Empty)
            ? previousSelection
            : null;
    }

    /// <summary>Creates a new profile with the current brightness values.</summary>
    public void CreateProfile()
    {
        string name = _newProfileName;
        string? validation = ValidateName(name);
        if (validation is not null)
        {
            ValidationError = validation;
            StatusMessage = null;
            return;
        }

        Result<Unit> result = _profileManager.CreateProfile(name, _captureBrightnessMap(), null);
        if (!result.IsSuccess)
        {
            ValidationError = result.Error;
            StatusMessage = null;
            return;
        }

        ValidationError = null;
        NewProfileName = string.Empty;
        Refresh();
        SelectedProfileName = name;
        StatusMessage = $"Profile '{name}' created with current brightness levels.";
    }

    /// <summary>Applies the selected profile to all connected monitors.</summary>
    public void ApplySelectedProfile()
    {
        string? name = _selectedProfileName;
        if (string.IsNullOrEmpty(name) || _monitorService is null)
        {
            return;
        }

        Result<Unit> result = _profileManager.ApplyProfile(name, _monitorService);
        if (!result.IsSuccess)
        {
            ValidationError = result.Error;
            StatusMessage = null;
            return;
        }

        ValidationError = null;
        StatusMessage = $"Profile '{name}' applied.";
    }

    /// <summary>Updates the selected profile with the current brightness values.</summary>
    public void UpdateSelectedProfile()
    {
        string? name = _selectedProfileName;
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var map = _captureBrightnessMap();
        Result<Unit> result = _profileManager.UpdateProfile(name, map, null);
        if (!result.IsSuccess)
        {
            ValidationError = result.Error;
            StatusMessage = null;
            return;
        }

        ValidationError = null;
        StatusMessage = $"Profile '{name}' updated with current brightness levels.";
    }

    /// <summary>Deletes the selected profile.</summary>
    public void DeleteSelectedProfile()
    {
        string? name = _selectedProfileName;
        if (string.IsNullOrEmpty(name)) return;

        Result<Unit> result = _profileManager.DeleteProfile(name);
        if (!result.IsSuccess)
        {
            ValidationError = result.Error;
            StatusMessage = null;
            return;
        }

        ValidationError = null;
        Refresh();
        StatusMessage = $"Profile '{name}' deleted.";
    }

    private string? ValidateName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "Enter a profile name.";
        if (!ProfileManager.IsValidProfileName(name))
        {
            return $"Profile names must be {ProfileManager.MinNameLength}-{ProfileManager.MaxNameLength} "
                + "characters using only letters, digits, hyphens, and underscores.";
        }
        bool duplicate = ProfileNames.Any(
            existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase));
        if (duplicate) return $"A profile named '{name}' already exists.";
        return null;
    }
}
