using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;

namespace AudioShare.App;

public sealed class UpdateProgressWindow : Window
{
    private readonly TextBlock messageText;
    private readonly TextBlock percentageText;
    private readonly ProgressBar progressBar;
    private bool closeRequestedByApplication;

    public UpdateProgressWindow(Window owner)
    {
        Owner = owner;
        Title = "FlowCast 更新";
        Width = 390;
        Height = 180;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Closing += UpdateProgressWindow_Closing;

        messageText = new TextBlock
        {
            Text = "正在准备更新",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        };
        percentageText = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 0, 0),
        };
        progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Height = 18,
        };

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children = { messageText, progressBar, percentageText },
        };
    }

    public void CloseFromApplication()
    {
        closeRequestedByApplication = true;
        if (IsVisible)
        {
            Close();
        }
    }

    public void Update(UpdateProgress progress)
    {
        progressBar.IsIndeterminate = progress.Percentage is null;
        progressBar.Value = progress.Percentage ?? 0;
        messageText.Text = progress.Message;
        percentageText.Text = progress.Percentage is int percentage ? $"{percentage}%" : string.Empty;
    }

    private void UpdateProgressWindow_Closing(object? sender, CancelEventArgs e) =>
        e.Cancel = !closeRequestedByApplication;
}
