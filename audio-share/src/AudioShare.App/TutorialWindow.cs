using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace AudioShare.App;

public sealed class TutorialWindow : Window
{
    private static readonly Brush PrimaryBrush = new SolidColorBrush(Color.FromRgb(29, 95, 191));
    private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(36, 50, 74));
    private static readonly Brush MutedBrush = new SolidColorBrush(Color.FromRgb(82, 97, 118));
    private static readonly Brush RedBrush = new SolidColorBrush(Color.FromRgb(203, 44, 44));

    public TutorialWindow(Window owner)
    {
        Owner = owner;
        Title = "FlowCast - 使用教程";
        Width = 860;
        Height = 760;
        MinWidth = 620;
        MinHeight = 520;
        Background = new SolidColorBrush(Color.FromRgb(245, 247, 250));
        FontFamily = new FontFamily("Microsoft YaHei UI");
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var content = new StackPanel { Margin = new Thickness(26) };
        content.Children.Add(new TextBlock
        {
            Text = "FlowCast 使用教程",
            FontSize = 25,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(22, 32, 51)),
        });
        content.Children.Add(new TextBlock
        {
            Text = "按顺序设置。红色箭头标出需要看的位置。",
            Margin = new Thickness(0, 5, 0, 18),
            Foreground = MutedBrush,
        });

        foreach (var step in TutorialContent.Steps)
        {
            content.Children.Add(BuildStepCard(step));
        }

        content.Children.Add(BuildInfoCard("使用规则", TutorialContent.Rules, Color.FromRgb(232, 242, 255), PrimaryBrush));
        content.Children.Add(BuildInfoCard(
            "常见问题",
            [
                "听不到电脑声音：检查 Banana 的 A1 是否还是你的耳机。",
                "朋友听不到音乐：确认程序已勾选，并确认该程序正在播放声音。",
                "朋友听到自己的声音：确认 Discord 扬声器是 Voicemeeter AUX Input，且 Discord 没有被勾选分享。",
                "程序不在列表：先让程序开始播放，再点击“重新检测”。",
            ],
            Color.FromRgb(255, 247, 226),
            new SolidColorBrush(Color.FromRgb(151, 96, 10))));

        var closeButton = new Button
        {
            Content = "我知道了",
            Padding = new Thickness(16, 8, 16, 8),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        closeButton.Click += (_, _) => Close();
        content.Children.Add(closeButton);

        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = content,
        };
    }

    private static UIElement BuildStepCard(TutorialStep step)
    {
        var card = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        card.Children.Add(new TextBlock
        {
            Text = step.Title,
            FontWeight = FontWeights.SemiBold,
            Foreground = PrimaryBrush,
        });
        card.Children.Add(new TextBlock
        {
            Text = step.Body,
            Margin = new Thickness(0, 5, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 22,
            Foreground = TextBrush,
        });
        card.Children.Add(BuildDiagram(step.DiagramNodes));
        card.Children.Add(BuildKeyRules(step.KeyRules));

        if (step.Screenshot is not null)
        {
            card.Children.Add(BuildScreenshot(step.Screenshot));
        }

        return new Border
        {
            Padding = new Thickness(14, 12, 14, 10),
            Margin = new Thickness(0, 0, 0, 4),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 227, 236)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = card,
        };
    }

    private static UIElement BuildDiagram(IReadOnlyList<string> nodes)
    {
        var diagram = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };

        for (var index = 0; index < nodes.Count; index++)
        {
            diagram.Children.Add(new Border
            {
                Padding = new Thickness(10, 5, 10, 5),
                Background = new SolidColorBrush(Color.FromRgb(238, 246, 255)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(169, 199, 237)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Child = new TextBlock { Text = nodes[index], Foreground = PrimaryBrush },
            });

            if (index < nodes.Count - 1)
            {
                diagram.Children.Add(new TextBlock
                {
                    Text = "→",
                    Margin = new Thickness(8, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = MutedBrush,
                });
            }
        }

        return diagram;
    }

    private static UIElement BuildKeyRules(IReadOnlyList<string> keyRules)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var rule in keyRules)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"重点：{rule}",
                Foreground = new SolidColorBrush(Color.FromRgb(22, 111, 78)),
                FontWeight = FontWeights.SemiBold,
            });
        }

        return panel;
    }

    private static UIElement BuildScreenshot(TutorialScreenshot screenshot)
    {
        var resourceUri = new Uri(
            $"pack://application:,,,/{screenshot.ResourceUri.TrimStart('/')}",
            UriKind.Absolute);
        var bitmap = new BitmapImage(resourceUri);
        var canvas = new Canvas
        {
            Width = bitmap.PixelWidth,
            Height = bitmap.PixelHeight,
            ClipToBounds = true,
            Background = Brushes.Black,
        };
        canvas.Children.Add(new Image
        {
            Source = bitmap,
            Width = bitmap.PixelWidth,
            Height = bitmap.PixelHeight,
            Stretch = Stretch.Fill,
        });

        foreach (var callout in screenshot.Callouts)
        {
            AddCallout(canvas, callout, bitmap.PixelWidth, bitmap.PixelHeight);
        }

        return new Border
        {
            Margin = new Thickness(0, 12, 0, 0),
            BorderBrush = new SolidColorBrush(Color.FromRgb(192, 203, 217)),
            BorderThickness = new Thickness(1),
            Child = new Viewbox
            {
                MaxHeight = 440,
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                Child = canvas,
            },
        };
    }

    private static void AddCallout(Canvas canvas, TutorialCallout callout, double width, double height)
    {
        var labelX = callout.X * width;
        var labelY = callout.Y * height;
        var targetX = callout.TargetX * width;
        var targetY = callout.TargetY * height;
        const double badgeSize = 34;

        var line = new Line
        {
            X1 = labelX + badgeSize / 2,
            Y1 = labelY + badgeSize / 2,
            X2 = targetX,
            Y2 = targetY,
            Stroke = RedBrush,
            StrokeThickness = 4,
        };
        canvas.Children.Add(line);

        var direction = new Vector(targetX - line.X1, targetY - line.Y1);
        direction.Normalize();
        var perpendicular = new Vector(-direction.Y, direction.X);
        var arrowBase = new Point(targetX - direction.X * 16, targetY - direction.Y * 16);
        canvas.Children.Add(new Polygon
        {
            Fill = RedBrush,
            Points =
            [
                new Point(targetX, targetY),
                new Point(arrowBase.X + perpendicular.X * 8, arrowBase.Y + perpendicular.Y * 8),
                new Point(arrowBase.X - perpendicular.X * 8, arrowBase.Y - perpendicular.Y * 8),
            ],
        });

        var labelPanel = new StackPanel { Orientation = Orientation.Horizontal };
        labelPanel.Children.Add(new TextBlock
        {
            Text = callout.Number,
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var badge = new Border
        {
            Width = badgeSize,
            Height = badgeSize,
            Background = RedBrush,
            CornerRadius = new CornerRadius(badgeSize / 2),
            Child = labelPanel,
        };
        canvas.Children.Add(badge);
        Canvas.SetLeft(badge, labelX);
        Canvas.SetTop(badge, labelY);

        var label = new Border
        {
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(7, 4, 7, 4),
            Background = Brushes.White,
            BorderBrush = RedBrush,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(4),
            Child = new TextBlock
            {
                Text = callout.Label,
                Foreground = RedBrush,
                FontWeight = FontWeights.SemiBold,
            },
        };
        canvas.Children.Add(label);
        Canvas.SetLeft(label, labelX + badgeSize);
        Canvas.SetTop(label, labelY);
    }

    private static UIElement BuildInfoCard(string title, IReadOnlyList<string> rows, Color background, Brush titleBrush)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            Foreground = titleBrush,
        });
        foreach (var row in rows)
        {
            content.Children.Add(new TextBlock
            {
                Text = $"• {row}",
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Foreground = TextBrush,
            });
        }

        return new Border
        {
            Padding = new Thickness(14),
            Margin = new Thickness(0, 8, 0, 0),
            Background = new SolidColorBrush(background),
            BorderBrush = titleBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = content,
        };
    }
}
