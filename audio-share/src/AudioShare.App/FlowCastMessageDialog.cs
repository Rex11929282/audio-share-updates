using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AudioShare.App;

internal sealed class FlowCastMessageDialog : FlowCastDialogWindow
{
    private FlowCastMessageDialog(
        Window? owner,
        string title,
        string message,
        FlowCastWindowTone tone,
        string confirmText,
        string? cancelText)
    {
        Owner = owner;
        Title = title;
        Tone = tone;
        Width = 460;
        Height = 250;
        MinWidth = Width;
        MaxWidth = Width;
        MinHeight = Height;
        MaxHeight = Height;
        WindowStartupLocation = owner is null
            ? WindowStartupLocation.CenterScreen
            : WindowStartupLocation.CenterOwner;

        var content = new Grid { Margin = new Thickness(26, 22, 26, 20) };
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var messageScrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 8, 10),
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 23,
                FontSize = 15,
                Foreground = new SolidColorBrush(Color.FromRgb(42, 65, 88)),
            },
        };
        content.Children.Add(messageScrollViewer);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        if (!string.IsNullOrWhiteSpace(cancelText))
        {
            buttons.Children.Add(CreateButton(cancelText, false, (_, _) => DialogResult = false));
        }

        buttons.Children.Add(CreateButton(confirmText, true, (_, _) => DialogResult = true));
        Grid.SetRow(buttons, 1);
        content.Children.Add(buttons);
        Content = content;
    }

    public static bool Confirm(
        Window? owner,
        string title,
        string message,
        string confirmText = "继续",
        string cancelText = "取消",
        FlowCastWindowTone tone = FlowCastWindowTone.Share) =>
        new FlowCastMessageDialog(owner, title, message, tone, confirmText, cancelText).ShowDialog() == true;

    public static void Show(
        Window? owner,
        string title,
        string message,
        FlowCastWindowTone tone = FlowCastWindowTone.Soft) =>
        new FlowCastMessageDialog(owner, title, message, tone, "知道了", null).ShowDialog();

    private static Button CreateButton(string text, bool primary, RoutedEventHandler click)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 88,
            Height = 38,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(14, 0, 14, 0),
            Foreground = primary ? Brushes.White : new SolidColorBrush(Color.FromRgb(48, 74, 101)),
            Background = primary
                ? new SolidColorBrush(Color.FromRgb(38, 123, 228))
                : new SolidColorBrush(Color.FromRgb(246, 250, 253)),
            BorderBrush = primary
                ? new SolidColorBrush(Color.FromRgb(101, 168, 250))
                : new SolidColorBrush(Color.FromRgb(205, 220, 234)),
            BorderThickness = new Thickness(1),
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        button.Click += click;
        return button;
    }
}
