using System.IO;
using Microsoft.Web.WebView2.Core;

namespace AudioShare.Lyrics;

internal static class LyricsWebViewEnvironment
{
    private static readonly Lazy<Task<CoreWebView2Environment>> EnvironmentTask =
        new(CreateAsync);

    internal static Task<CoreWebView2Environment> GetAsync() => EnvironmentTask.Value;

    private static async Task<CoreWebView2Environment> CreateAsync()
    {
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FlowCast Lyrics",
            "WebView2");
        Directory.CreateDirectory(userDataFolder);
        return await CoreWebView2Environment.CreateAsync(null, userDataFolder);
    }
}
