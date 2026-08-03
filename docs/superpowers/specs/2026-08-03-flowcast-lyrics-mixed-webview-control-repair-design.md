# FlowCast Lyrics mixed WebView control repair

## Confirmed evidence

The tuner fails before settings, mapping, or navigation. The local record from a clean probe is:

```text
tuner-webview stage=initialization System.Runtime.InteropServices.COMException: ... (0x8007139F)
```

`0x8007139F` is `HRESULT_FROM_WIN32(ERROR_INVALID_STATE)`. Microsoft documents this result for WebView creation when currently running WebViews that share a user-data folder have incompatible creation options or controller state.

The probe was run after all FlowCast Lyrics test processes and FlowCast-owned WebView2 browser processes had stopped, so it reproduces inside one new Lyrics process rather than depending on a stale earlier process.

## Comparison

| Initialization property | Working overlay | Failing tuner |
| --- | --- | --- |
| WPF control | `WebView2CompositionControl` | `WebView2` |
| Initial visibility | `Hidden` | `Hidden` |
| Explicit environment | `LyricsWebViewEnvironment.GetAsync()` | `LyricsWebViewEnvironment.GetAsync()` |
| User-data folder | `%LocalAppData%\\FlowCast Lyrics\\WebView2` | same process-local environment |
| Controller options | default | default |

The visible/hidden hypothesis is ruled out because both controls begin hidden. The supported remaining difference is the controller backend: composition versus standard WPF hosting.

## Decision under test

Replace only the tuner XAML element with `WebView2CompositionControl`, matching the already working overlay. Microsoft documents that it is a drop-in replacement implementing the same WPF `IWebView2` API. No code-behind API change is required.

This is a focused hypothesis, not a stack of fixes: if a fresh probe still writes `ERROR_INVALID_STATE`, stop and investigate the next concrete WebView2 option boundary instead of adding retries or changing user-data folders.

## Scope

Keep the small native right-click option menu, draggable capsule, existing liquid-glass-react tuner, six parameters, Radmin discovery, lyric transport, and audio routing unchanged. Do not modify `AudioShare.App/MainWindow.xaml.cs`.

The composition control has a lower rendering ceiling than standard WPF WebView2, but the tuner is a small, occasional settings surface rather than an audio/video renderer. It is the minimum compatible choice because the overlay already needs composition hosting.
