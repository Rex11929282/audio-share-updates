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
        Background = new SolidColorBrush(Color.FromRgb(247, 249, 253));
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
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { Background = new SolidColorBrush(Color.FromArgb(236, 255, 255, 255)) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.MouseLeftButtonDown += Header_MouseLeftButtonDown;

        header.Children.Add(new TextBlock
        {
            Text = Title,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(18, 0, 0, 0),
            Foreground = new SolidColorBrush(HeaderColor()),
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            IsHitTestVisible = false,
        });

        var closeButton = new Button
        {
            Content = "×",
            Width = 38,
            Height = 30,
            Margin = new Thickness(0, 8, 10, 8),
            Padding = new Thickness(0),
            FontSize = 19,
            Foreground = new SolidColorBrush(HeaderColor()),
            Background = new SolidColorBrush(Color.FromArgb(0, 255, 255, 255)),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Template = CreateHeaderButtonTemplate(),
            ToolTip = "關閉",
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

    private static ControlTemplate CreateHeaderButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var surface = new FrameworkElementFactory(typeof(Border));
        surface.Name = "HeaderButtonSurface";
        surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
        surface.SetValue(Border.BackgroundProperty, Brushes.Transparent);

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetValue(ContentPresenter.ContentSourceProperty, "Content");
        surface.AppendChild(presenter);
        template.VisualTree = surface;

        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(
            Border.BackgroundProperty,
            new SolidColorBrush(Color.FromArgb(26, 24, 70, 112)),
            "HeaderButtonSurface"));
        template.Triggers.Add(hover);
        return template;
    }
}
