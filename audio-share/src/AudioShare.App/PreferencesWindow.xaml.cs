using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using AudioShare.Core;

namespace AudioShare.App;

public partial class PreferencesWindow : Window
{
    public PreferencesWindow(Window owner, FlowCastPreferences preferences)
    {
        InitializeComponent();
        Owner = owner;
        ExcludedPrograms = new ObservableCollection<string>(preferences.ExcludedProcesses.Order(StringComparer.OrdinalIgnoreCase));
        ReduceMotionCheckBox.IsChecked = preferences.ReduceMotion;
        RestoreLocalPlaybackCheckBox.IsChecked = preferences.RestoreLocalPlayback;
        EndSharingSoundCheckBox.IsChecked = preferences.EndSharingSoundEnabled;
        DisconnectNotificationsCheckBox.IsChecked = preferences.DisconnectNotificationsEnabled;
        StartCountdownComboBox.SelectedValue = preferences.StartCountdownSeconds.ToString();
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
        var countdown = StartCountdownComboBox.SelectedValue is string value && int.TryParse(value, out var parsed)
            ? parsed
            : 3;
        UpdatedPreferences = new FlowCastPreferences(
            ExcludedPrograms,
            ReduceMotionCheckBox.IsChecked == true,
            countdown,
            RestoreLocalPlaybackCheckBox.IsChecked != false,
            EndSharingSoundCheckBox.IsChecked != false,
            DisconnectNotificationsCheckBox.IsChecked != false);
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
