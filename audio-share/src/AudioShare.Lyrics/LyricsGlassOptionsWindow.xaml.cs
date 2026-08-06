using System.Windows;
using System.Windows.Controls;

namespace AudioShare.Lyrics;

public partial class LyricsGlassOptionsWindow : Window
{
    private readonly LyricsGlassOptionsSession session;

    internal LyricsGlassOptionsWindow(LyricsGlassSettings initial)
    {
        InitializeComponent();
        session = new LyricsGlassOptionsSession(initial);
        ApplySettings(session.Current);
    }

    internal LyricsGlassSettings? Settings { get; private set; }

    private void ApplySettings(LyricsGlassSettings settings)
    {
        CornerRadiusSlider.Value = settings.CornerRadiusFraction;
        BlurRadiusSlider.Value = settings.BlurRadiusDp;
        RefractionHeightSlider.Value = settings.RefractionHeightFraction;
        RefractionAmountSlider.Value = settings.RefractionAmountFraction;
        ChromaticAberrationCheckBox.IsChecked = settings.ChromaticAberration;
    }

    private void CornerRadiusSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> eventArgs) =>
        session.Current = session.Current with { CornerRadiusFraction = eventArgs.NewValue };

    private void BlurRadiusSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> eventArgs) =>
        session.Current = session.Current with { BlurRadiusDp = eventArgs.NewValue };

    private void RefractionHeightSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> eventArgs) =>
        session.Current = session.Current with { RefractionHeightFraction = eventArgs.NewValue };

    private void RefractionAmountSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> eventArgs) =>
        session.Current = session.Current with { RefractionAmountFraction = eventArgs.NewValue };

    private void ChromaticAberrationCheckBox_OnChanged(object sender, RoutedEventArgs eventArgs) =>
        session.Current = session.Current with { ChromaticAberration = ChromaticAberrationCheckBox.IsChecked == true };

    private void ResetButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        session.Reset();
        ApplySettings(session.Current);
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        session.Cancel();
        DialogResult = false;
    }

    private void CompleteButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        Settings = session.Complete();
        DialogResult = true;
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        session.Cancel();
        DialogResult = false;
    }
}
