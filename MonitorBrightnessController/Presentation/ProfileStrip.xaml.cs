using System.Windows;
using System.Windows.Controls;
using MonitorBrightnessController.Models;

namespace MonitorBrightnessController.Presentation;

/// <summary>
/// Code-behind for the ProfileStrip UserControl. Handles the confirmation dialog for
/// the Delete button and the popup input dialog for Save As New with validation.
/// </summary>
public partial class ProfileStrip : UserControl
{
    public ProfileStrip()
    {
        InitializeComponent();
    }

    private ProfileStripViewModel? ViewModel => DataContext as ProfileStripViewModel;

    /// <summary>
    /// Handles the Delete button click by showing a confirmation dialog before deleting.
    /// </summary>
    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null || string.IsNullOrEmpty(vm.SelectedProfileName))
            return;

        string profileName = vm.SelectedProfileName;

        MessageBoxResult confirm = MessageBox.Show(
            $"Are you sure you want to delete profile '{profileName}'?",
            "Delete Profile",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
            return;

        Result<Unit> result = vm.ConfirmDeleteSelectedProfile();
        if (!result.IsSuccess)
        {
            MessageBox.Show(
                result.Error ?? "Failed to delete profile.",
                "Delete Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Handles the Save As New button click by showing an input dialog with validation.
    /// </summary>
    private void SaveAsNewButton_Click(object sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null)
            return;

        var dialog = new ProfileNameDialog(vm)
        {
            Owner = Window.GetWindow(this)
        };

        dialog.ShowDialog();
    }
}
