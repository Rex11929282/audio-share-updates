namespace AudioShare.Core.Tests;

public sealed class FlowCastLyricsStartupTests
{
    [Fact]
    public void App_UsesMainWindowAsItsStartupWindow()
    {
        var app = new global::AudioShare.Lyrics.App();

        Assert.Equal(new Uri("MainWindow.xaml", UriKind.Relative), app.StartupUri);
    }
}
