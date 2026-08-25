using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AudioShare.App;

internal enum CloseFlowCastAction
{
    Cancel,
    Minimize,
    Exit,
}

internal sealed class CloseFlowCastDialog : FlowCastDialogWindow
{
    public CloseFlowCastDialog(Window owner)
    {
        Owner = owner;
        Title = "關閉 FlowCast";
        Tone = FlowCastWindowTone.Settings;
        Width = 430;
        Height = 220;
        MinWidth = Width;
        MaxWidth = Width;
        MinHeight = Height;
        MaxHeight = Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        Background = new SolidColorBrush(Color.FromRgb(247, 251, 255));

        var panel = new StackPanel { Margin = new Thickness(26) };
        panel.Children.Add(new TextBlock
        {
            Text = "要如何關閉 FlowCast？",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(23, 45, 69)),
        });
        panel.Children.Add(new TextBlock
        {
            Text = "最小化會繼續在系統托盤運行；結束會先停止分享，再關閉程序。",
            Margin = new Thickness(0, 9, 0, 20),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(91, 113, 137)),
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        buttons.Children.Add(CreateButton("取消", (_, _) => Close(), false));
        buttons.Children.Add(CreateButton("最小化到系統托盤", (_, _) => Complete(CloseFlowCastAction.Minimize), false));
        buttons.Children.Add(CreateButton("結束 FlowCast", (_, _) => Complete(CloseFlowCastAction.Exit), true));
        panel.Children.Add(buttons);
        Content = panel;
    }

    public CloseFlowCastAction Action { get; private set; }

    private Button CreateButton(string text, RoutedEventHandler click, bool isPrimary)
    {
        var button = new Button
        {
            Content = text,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(12, 7, 12, 7),
            MinHeight = 34,
            Foreground = isPrimary ? Brushes.White : new SolidColorBrush(Color.FromRgb(41, 66, 93)),
            Background = isPrimary
                ? new SolidColorBrush(Color.FromRgb(25, 118, 243))
                : Brushes.White,
            BorderBrush = isPrimary
                ? new SolidColorBrush(Color.FromRgb(90, 167, 255))
                : new SolidColorBrush(Color.FromRgb(200, 218, 232)),
            BorderThickness = new Thickness(1),
        };
        button.Click += click;
        return button;
    }

    private void Complete(CloseFlowCastAction action)
    {
        Action = action;
        DialogResult = true;
    }
}
