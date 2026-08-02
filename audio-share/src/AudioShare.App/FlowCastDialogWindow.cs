using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AudioShare.App;

public enum FlowCastWindowTone
{
    Soft,
    Share,
    Settings,
    Update,
    Error,
}

public class FlowCastDialogWindow : Window
{
    public FlowCastWindowTone Tone { get; set; } = FlowCastWindowTone.Soft;

    private bool shellApplied;

    public FlowCastDialogWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        Background = new SolidColorBrush(Color.FromRgb(244, 248, 252));
        Loaded += (_, _) => ApplyShell();
    }

    private void ApplyShell()
    {
        if (shellApplied || Content is not UIElement originalContent)
        {
            return;
        }

        shellApplied = true;
        Content = null;

        var shell = new Grid();
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { Background = new SolidColorBrush(HeaderColor()) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.MouseLeftButtonDown += Header_MouseLeftButtonDown;

        header.Children.Add(new TextBlock
        {
            Text = Title,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            IsHitTestVisible = false,
        });

        var closeButton = new Button
        {
            Content = "×",
            Width = 42,
            Height = 32,
            Margin = new Thickness(0, 5, 7, 5),
            Padding = new Thickness(0),
            FontSize = 19,
            Foreground = Brushes.White,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            ToolTip = "关闭",
        };
        closeButton.Click += (_, _) => Close();
        Grid.SetColumn(closeButton, 1);
        header.Children.Add(closeButton);

        var body = new Border
        {
            Background = Background,
            Child = originalContent,
        };
        Grid.SetRow(body, 1);
        shell.Children.Add(header);
        shell.Children.Add(body);
        Content = shell;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private Color HeaderColor() => Tone switch
    {
        FlowCastWindowTone.Share => Color.FromRgb(39, 109, 209),
        FlowCastWindowTone.Settings => Color.FromRgb(72, 98, 122),
        FlowCastWindowTone.Update => Color.FromRgb(35, 145, 128),
        FlowCastWindowTone.Error => Color.FromRgb(193, 75, 77),
        _ => Color.FromRgb(38, 151, 164),
    };
}
