using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using AudioShare.Core;

namespace AudioShare.App;

public partial class PreferencesWindow : FlowCastDialogWindow
{
    private readonly FlowCastPreferences originalPreferences;

    public PreferencesWindow(Window owner, FlowCastPreferences preferences)
    {
        InitializeComponent();
        Owner = owner;
        originalPreferences = preferences;
        ExcludedPrograms = new ObservableCollection<string>(preferences.ExcludedProcesses.Order(StringComparer.OrdinalIgnoreCase));
        ReduceMotionCheckBox.IsChecked = preferences.ReduceMotion;
        EndSharingSoundCheckBox.IsChecked = preferences.EndSharingSoundEnabled;
        DisconnectNotificationsCheckBox.IsChecked = preferences.DisconnectNotificationsEnabled;
        GlobalHotkeysCheckBox.IsChecked = preferences.GlobalHotkeysEnabled;
        DataContext = this;
    }

    public ObservableCollection<string> ExcludedPrograms { get; }

    public FlowCastPreferences UpdatedPreferences { get; private set; } = FlowCastPreferences.Empty;

    private void ExcludedProgramsList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RestoreButton.IsEnabled = ExcludedProgramsList.SelectedItem is string;

    private void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (ExcludedProgramsList.SelectedItem is not string processName)
        {
            return;
        }

        ExcludedPrograms.Remove(processName);
        RestoreButton.IsEnabled = false;
    }

    private void DoneButton_Click(object sender, RoutedEventArgs e)
    {
        UpdatedPreferences = new FlowCastPreferences(
            ExcludedPrograms,
            ReduceMotionCheckBox.IsChecked == true,
            true,
            EndSharingSoundCheckBox.IsChecked != false,
            DisconnectNotificationsCheckBox.IsChecked != false,
            originalPreferences.QuickStartCompleted,
            GlobalHotkeysCheckBox.IsChecked != false);
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
