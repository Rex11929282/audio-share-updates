using System.Windows;
using System.Windows.Controls;

namespace AudioShare.App;

public sealed class UpdateAvailableDialog : Window
{
    public UpdateAvailableDialog(Window owner, Version version)
    {
        Owner = owner;
        Title = "Audio Share 更新";
        Width = 390;
        Height = 170;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var updateButton = new Button
        {
            Content = "立即更新",
            IsDefault = true,
            MinWidth = 100,
            Margin = new Thickness(0, 0, 8, 0),
        };
        updateButton.Click += (_, _) => DialogResult = true;

        var laterButton = new Button
        {
            Content = "稍後",
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
                    Text = $"發現新版本 {version}。更新將在您確認後下載並驗證。",
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
