# MochiDock installer (beta)

Inno Setup 6 script that packages the **self-contained** win-x64 publish. The target PC needs no .NET install.

## Build

```bat
scripts\build_installer.bat
```

This runs `scripts\publish.ps1 -Installer` (output `Publish-Installer\`, no elevation, no Desktop shortcut, the app is never launched), then compiles `installer\MochiDock.iss` with `ISCC.exe` from `%ProgramFiles(x86)%\Inno Setup 6\`. If Inno Setup is not installed the script says so and stops after the publish. Install it from https://jrsoftware.org/isinfo.php.

Result: `installer\Output\MochiDock-Setup-<version>.exe`. Override the version with `ISCC /DMyAppVersion=0.2.0 installer\MochiDock.iss`.

## What the installer does

- Admin install to `%ProgramFiles%\MochiDock`, Start-menu shortcut, fixed AppId (upgrades install over the old copy; a running MochiDock from that folder is closed gracefully, then killed).
- Optional tasks: Desktop shortcut; "Start MochiDock at logon" (runs `install-startup.ps1`, creating the elevated per-user task `MochiDock-Startup-<SID>` for the **installing admin account**); Downloads tools.
- Finish page has an unchecked "Launch MochiDock".
- Uninstall runs `uninstall-startup.ps1`, then asks whether to delete `%AppData%\MochiDock` and `%LocalAppData%\MochiDock` (default **No**). The WhatsApp session in `%LocalAppData%\MochiDock\GOWA` is never packaged.

## Optional Downloads tools

Place `ffmpeg.exe` and/or `yt-dlp.exe` in `installer\redist\` (git-ignored) before compiling; they are installed to `{app}\Tools` and a "Downloads tools" task appears. Without them the app still works: yt-dlp is downloaded and checksum-verified on first use, and MP3 extraction shows a clear "ffmpeg is required" message until `ffmpeg.exe` is in `{app}\Tools`, `%LocalAppData%\MochiDock\Tools` or on PATH. If you redistribute ffmpeg, follow its licence (see `THIRD-PARTY-NOTICES.txt`).

## Not bundled (fresh-PC prerequisites)

- **PawnIO** (optional): kernel driver that lets the hardware library read CPU temperature. It is not bundled because it is a proprietary (freeware) signed kernel driver with its own licence. The setup has an unchecked "Install the PawnIO driver" task that runs `winget install --id namazso.PawnIO --exact --source winget` (winget verifies the package hash); if winget is missing or fails, the user is pointed to https://pawnio.eu. Without PawnIO the CPU temperature is an estimate and the power total uses Windows' Energy Meter counters.
- NVIDIA driver (provides `nvml.dll` for extra GPU detail; usage works on any GPU), classic Outlook or Mailspring (Email card; IMAP accounts need neither), Discord (notification card; whether Windows exposes its toasts to an unpackaged app is not established).

## Signing

The installer and `MochiDock.exe` are unsigned, so SmartScreen will warn. Sign both with `signtool` before wider distribution.

## Not yet verified

The installer has not been run on a clean machine. Test install, upgrade, startup task, the PawnIO task and uninstall on a clean VM.
