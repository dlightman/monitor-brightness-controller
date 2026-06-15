using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using MonitorBrightnessController.Models;

namespace MonitorBrightnessController.Presentation;

/// <summary>
/// A simple input dialog for entering a new profile name. Performs inline validation:
/// 1–64 characters, only [a-zA-Z0-9_-], no duplicates (case-insensitive), and max
/// profile count not exceeded.
/// </summary>
public partial class ProfileNameDialog : Window
{
    private static readonly Regex ValidNamePattern = new(@"^[a-zA-Z0-9_-]+$", RegexOptions.Compiled);

    private readonly ProfileStripViewModel _viewModel;

    public ProfileNameDialog(ProfileStripViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        NameInput.Focus();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        string name = NameInput.Text.Trim();

        // Validate length
        if (string.IsNullOrEmpty(name) || name.Length < 1 || name.Length > 64)
        {
            ShowError("Profile name must be 1 to 64 characters.");
            return;
        }

        // Validate characters
        if (!ValidNamePattern.IsMatch(name))
        {
            ShowError("Profile name can only contain letters, digits, hyphens, and underscores.");
            return;
        }

        // Check for duplicate (case-insensitive)
        bool isDuplicate = _viewModel.ProfileNames
            .Any(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase));

        if (isDuplicate)
        {
            ShowError($"A profile named '{name}' already exists.");
            return;
        }

        // Attempt to create the profile (this also checks max count in ProfileManager)
        Result<Unit> result = _viewModel.CreateProfile(name);

        if (!result.IsSuccess)
        {
            ShowError(result.Error ?? "Failed to create profile.");
            return;
        }

        // Success — close the dialog
        DialogResult = true;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
