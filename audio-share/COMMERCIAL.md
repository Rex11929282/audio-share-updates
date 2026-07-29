# Proprietary Notice

The Audio Share application code and Audio Share-provided assets are proprietary. All rights reserved.

These proprietary restrictions do not apply to bundled third-party components. Every published ZIP includes these notice files:

- `ThirdPartyNotices.txt` for NAudio 2.2.1.
- `DotNetRuntimeLicense.txt` for the bundled Microsoft .NET runtime license terms.
- `DotNetRuntimeThirdPartyNotices.txt` for third-party notices supplied with the bundled Microsoft .NET runtime.

This list identifies the notice files included with this release; it does not describe components or licenses that are not bundled in the ZIP.

## Phase-One Boundary

This build contains the tested selected-source mixer core. It does not install a virtual microphone driver and does not yet capture live application audio. It cannot be used as a Discord input until the separately signed driver and Windows process-loopback adapter are released.

Audio Share does not change an application's Windows output device; the user must choose the device manually in Windows Volume Mixer.

Distribution, resale, copying, or modification of the Audio Share code or Audio Share-provided assets requires prior permission from the copyright holder. This release process does not include a code-signing certificate yet.
