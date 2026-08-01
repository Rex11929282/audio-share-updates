using System.Windows;
using System.Windows.Controls;

namespace AudioShare.App;

public sealed class UpdateAvailableDialog : Window
{
    public UpdateAvailableDialog(Window owner, Version version)
    {
        Owner = owner;
        Title = "FlowCast 更新";
        Width = 390;
        Height = 180;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var updateButton = new Button
        {
            Content = "下载更新",
            IsDefault = true,
            MinWidth = 100,
            Margin = new Thickness(0, 0, 8, 0),
        };
        updateButton.Click += (_, _) => DialogResult = true;

        var laterButton = new Button
        {
            Content = "稍后",
            IsCancel = true,
            MinWidth = 72,
        };

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock
                {
                    Text = $"发现新版本 {version}。下载、验证并安装后，FlowCast 将自动重新启动。",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 20),
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { updateButton, laterButton },
                },
            },
        };
    }
}
