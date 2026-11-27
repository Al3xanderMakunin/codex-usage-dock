# Codex Usage Dock

[Русская версия](README.md)

Track the main Codex 5-hour and weekly subscription limits in the PowerToys Command Palette Dock. The ring gauges turn amber at 80% and red at 95%. Click a gauge for the reset time and the age of the latest reading.

## Install on Windows

You need PowerToys with Command Palette and its Dock enabled, plus a local Codex installation that records subscription limits.

- For a signed GitHub release, run the repository's `install.ps1` in PowerShell. It downloads the package for your architecture and prompts to trust the release certificate if needed.
- To build and install from this checkout, run `./pack.ps1 -SelfSign -Install` in PowerShell 7 on Windows with the .NET 10 SDK.

After installation, reload Command Palette or restart PowerToys. Right-click the Dock, choose **Edit**, and add **Codex Usage Dock**. Send a message in Codex if the gauges are waiting for data. You can also open **Codex usage** directly from Command Palette.

## How it works

Every 30 seconds the extension checks local `rollout-*.jsonl` files under `%USERPROFILE%\.codex\sessions` or `%CODEX_HOME%\sessions`. It uses the newest main Codex quota reading in `token_count` events from session files modified in the last eight days. It identifies the two windows by their duration and shows whichever are available.

The Dock marks readings older than one hour with `~`. After a window resets, the gauge shows a dash until a new reading arrives. The counts update when Codex writes a new reading to a local session, so activity elsewhere may appear with a delay.

For a custom Codex home, set `CODEX_HOME` in the Windows environment inherited by PowerToys and restart PowerToys. If Codex runs in WSL, the extension needs a Windows-accessible path to that Codex home. The extension reads the session files in place and works without a separate sign-in or status-line setup.

## Build and package

Run on Windows with the .NET 10 SDK and PowerShell 7. The Windows packaging tools come from NuGet.

```powershell
./pack.ps1 -Register                         # Register unpacked layout in Developer Mode
./pack.ps1 -SelfSign -Install                 # Sign and install locally
./pack.ps1 -Platform arm64 -SelfSign -Install # Build for ARM64
```

Packages are written to `out/<platform>/`. For a release signed with your own certificate, its subject must match the publisher in `CodexUsage/Package.appxmanifest`. For a Store build, pass `-Store` with the identity, publisher and publisher display name assigned in Partner Center.

## License

Unofficial community project distributed under the [MIT license](LICENSE).
