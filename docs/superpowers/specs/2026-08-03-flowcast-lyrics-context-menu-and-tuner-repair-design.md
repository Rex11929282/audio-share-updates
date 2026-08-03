# FlowCast Lyrics context menu and tuner repair

## Confirmed interaction

Right-clicking the compact Lyrics capsule opens a small native WPF context menu. It does not open the tuner immediately. The menu contains exactly:

- `Adjust Liquid Glass…`
- `Close FlowCast Lyrics`

Selecting `Adjust Liquid Glass…` opens the existing single normal tuner window, or brings that existing window forward. Selecting `Close FlowCast Lyrics` closes the Lyrics companion. Reset remains inside the tuner; no additional menu actions are introduced.

## Scope

The capsule remains small, borderless, topmost, and left-draggable. The six real `liquid-glass-react` parameters and their Saved/Draft behavior remain unchanged. This work does not change FlowCast audio routing, Radmin discovery, lyric transport, or `AudioShare.App/MainWindow.xaml.cs`.

## Tuner loading failure

The published frontend assets are present, and the normal overlay already loads the same virtual-host site. The tuner currently catches every initialization exception and replaces it with the generic `Unable to load the Liquid Glass tuner.` text, which hides the failing boundary. The repair must first make the failing initialization stage observable and covered by a failing test, then correct the identified WebView2 host/asset initialization issue. A user-facing failure must remain concise and must not expose filesystem paths or a stack trace.

## Implementation boundaries

Use a native WPF `ContextMenu` for the right-click menu because it is the smallest mechanism that matches the requested option bar. Keep the tuner as the existing React/WebView2 window only after explicit selection. Tests must prove right-click opens the menu rather than the tuner, each command has the right behavior, and the underlying tuner load failure cannot regress unnoticed.

The unrelated solution failure in `LyricsOverlayPresenterTests.cs` is in scope only to restore the missing `IslandMode` reference or its intended equivalent. It must not change production FlowCast behavior.

## Verification

Run focused Lyrics tests, the full solution test suite, a fresh frontend test/build, and a Debug self-contained publish. Manually verify right-click menu, opening the tuner through its first command, and successful WebView2 rendering before treating the repair as complete.
