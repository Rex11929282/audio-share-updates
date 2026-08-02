# FlowCast Commercial Notice

The FlowCast application code and FlowCast-provided assets are proprietary. All rights reserved.

These proprietary restrictions do not apply to bundled third-party components. Every published FlowCast installer includes the applicable notice files:

- `ThirdPartyNotices.txt` for NAudio 2.2.1 and the external routing helper dependencies: winappaudiorouter 1.1.1, comtypes 1.4.16, psutil 7.2.2, and pycaw 20251023.
- `router-helper/PythonLicense.txt` for the bundled Python 3.12.10 embedded runtime, including its complete license and bundled-component notices.
- `DotNetRuntimeLicense.txt` for the bundled Microsoft .NET runtime license terms.
- `DotNetRuntimeThirdPartyNotices.txt` for third-party notices supplied with the bundled Microsoft .NET runtime.

The external routing helper changes only supported, selected application routes. It keeps `Voicemeeter AUX Input` local-only, keeps protected communication and audio tools excluded, and never downloads routing code while FlowCast is running.

## Current Boundary

FlowCast provides selected-application routing through Voicemeeter Banana. The separately tested selected-source mixer core is not yet connected to a live Windows process-loopback output adapter. Independent music-only sharing volume and fade controls are therefore not included in this release: changing the shared Voicemeeter B1 bus would also affect the microphone path.

Distribution, resale, copying, or modification of FlowCast code or FlowCast-provided assets requires prior permission from the copyright holder. This release process does not include a code-signing certificate.
