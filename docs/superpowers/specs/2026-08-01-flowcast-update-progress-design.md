# FlowCast Update Progress Design

## Goal

When the user accepts a FlowCast update, show its download progress and automatically restart into the replacement version. A failed update must leave the installed version unchanged.

## Flow

1. The existing update dialog asks whether to update.
2. After confirmation, a non-dismissible progress window shows the current stage and a determinate percentage.
3. The ZIP download reports byte progress when the server provides a content length. If it does not, the window uses an indeterminate bar and still reports the current stage.
4. After download, the window reports checksum verification and package extraction.
5. Once the package passes validation, FlowCast reports that it is restarting, starts the existing replacement helper, closes itself, and the helper starts the new executable after replacement.

## Boundaries

- The update UI does not modify audio routing. Normal FlowCast close handling continues to restore local-only audio before the process exits.
- The existing checksum validation and replacement retry script remain the authority for safe installation and rollback.
- The update button remains hidden when no newer release exists.

## Components

- `UpdateProgress`: immutable stage, message, optional percentage, and indeterminate state.
- `UpdateService.DownloadAndStageAsync`: accepts `IProgress<UpdateProgress>` and reports download, checksum, and extraction stages.
- `UpdateProgressWindow`: owned WPF window that renders the stage message and progress bar while blocking duplicate update actions.
- `App.CheckForUpdatesAsync`: opens the progress window after user confirmation and closes FlowCast immediately after the replacement helper is started.

## Verification

- Unit tests verify the service reports a download percentage and the final validation stage.
- Existing tests continue to verify checksum rejection, full-package replacement, retry, rollback, and restart.
- Build and test the complete solution before release.
