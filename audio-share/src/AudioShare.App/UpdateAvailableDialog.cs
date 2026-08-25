using System.Windows;
using System.Windows.Controls;
using AudioShare.Core;

namespace AudioShare.App;

public sealed class UpdateAvailableDialog : FlowCastDialogWindow
{
    public UpdateAvailableDialog(Window owner, Version currentVersion, ReleaseUpdate update)
    {
        Owner = owner;
        Title = "FlowCast 更新";
        Tone = FlowCastWindowTone.Update;
        Width = 440;
        Height = 300;
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
            Content = "稍後更新",
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
                    Text = "發現可用的 FlowCast 新版本",
                    FontSize = 20,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 8),
                },
                new TextBlock
                {
                    Text = $"當前版本：{currentVersion}\n可用版本：{update.Version}",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 12),
                },
                new TextBlock
                {
                    Text = update.Notes is { Count: > 0 }
                        ? "本次更新：\n• " + string.Join("\n• ", update.Notes)
                        : "本次更新包含穩定性與體驗改進。",
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
