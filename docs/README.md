# MochiDock developer documentation

Short developer reference: layout, build commands, verification harnesses, feature status and the human runtime checklist. The detailed project guide is [`../AGENTS.md`](../AGENTS.md); user-facing information is in the [root README](../README.md) and the [user guide](USER_GUIDE.md). Implementation status is recorded separately from runtime verification: "not runtime-verified" means built and unit-tested, but never observed in the running app.

## Project layout

| Path | Contents |
| --- | --- |
| `MochiDock/` | WPF application (`net8.0-windows10.0.19041.0`): `Views/`, `ViewModels/`, `Services/`, `Models/`, `Controls/`, `Scripts/` (logon-task scripts copied next to the exe) |
| `MochiDock.Core/` | Pure logic with no WPF or I/O dependencies (policies, parsers, validation, Outlook COM reader) |
| `MochiDock.OutlookHelper/` | Non-elevated helper process that reads Outlook Classic over a named pipe |
| `MochiDock.Tests/` | xUnit regression tests |
| `GOWA/` | Bundled WhatsApp engine binary (never package its session storage) |
| `installer/` | Inno Setup script (`MochiDock.iss`) and notes |
| `scripts/` | Portable publish and installer build scripts (`build_and_publish.bat`, `build_installer.bat`, `publish.ps1`) and `release-notes.ps1`; run from anywhere |
| `.github/workflows/` | `ci.yml` (build and unit tests on push and pull request) and `release.yml` (installer and draft release on a `v*` tag) |
| `tools/` | Render harnesses (`verify/verify-*.ps1`), icon/capture scripts, GOWA server and relay helpers |
| `docs/` | This file, README images (`images/`) and GIFs (`media/`) |

## Build, test and publish

Run from the repository root.

```powershell
dotnet build MochiDock/MochiDock.csproj -c Release
dotnet test MochiDock.Tests/MochiDock.Tests.csproj -c Release
.\scripts\build_and_publish.bat      # portable build to Publish\ (elevates, never launches the app; /nopause for scripts)
.\scripts\build_installer.bat        # self-contained Publish-Installer\ then installer\Output\MochiDock-Setup-<version>.exe (Inno Setup 6)
```

- `Publish\` needs the .NET 8 Windows Desktop Runtime and must be kept together as a folder. The installer payload is self-contained.
- Publish logs are under `MochiDock/obj/publish-logs/`. See `installer/README.md` for installer details.
- Do not launch `MochiDock.exe` from automated tools: it needs Administrator and holds a global single-instance mutex.

## Render harnesses

The scripts in `tools/verify/` render real controls and check layout, fonts, contrast and DPI (100/125/150/200%) without launching the app, reading settings or contacting services. Run them **after** the Release build finishes and **one at a time**: loaded assemblies lock the build output.

```powershell
pwsh -NoProfile -STA -File tools/verify/verify-typography.ps1
```

| Harness | Checks |
| --- | --- |
| `verify-typography.ps1` | Shared styles, bundled fonts, header, Hardware, Prayer, Media, FX, slider templates |
| `verify-card-design.ps1` | Every card layout, tray rows, glyph faces, DPI manifest (also runs `verify-header-animation.ps1`) |
| `verify-navigation.ps1` | Tab order and deliberate card/Stats selection retention |
| `verify-island-chrome.ps1` | Capsule and expanded body chrome in both themes and docking modes |
| `verify-compact-typography.ps1` | Compact capsule modes, digit centering and role transitions |
| `verify-clock-weather-balance.ps1` | Resting clock + weather capsule: clock centred, equal gaps to Mochi and weather, no clipping |
| `verify-prayer-capsule.ps1` | Prayer reminder capsule widths, gaps and state handling |
| `verify-games-readout.ps1` | Gaming readout contrast, layout and view-model getters |
| `verify-email-theme.ps1` | Email card controls across theme switches |
| `verify-apps-card.ps1` | Apps card (other apps' notifications) in both themes at four scales, with and without app logos |
| `verify-header-animation.ps1` | Header selection pill animation and the former NaN crash |
| `verify-theme-contrast.ps1`, `verify-color-icons.ps1` | Palette contrast and colored icon resources |
| `verify-theme-redraw.ps1` | Code-drawn views repaint after a theme switch (visible now, hidden when shown) |
| `verify-core-bars.ps1` | Stats per-core grid: every thread a cell, equal whole-pixel cells for 8, 32, 64 and a 6P+8E CPU (P and E in rows of their own) under interface zooms from 100 to 200 % |
| `verify-interface-scale.ps1`, `verify-text-hint.ps1` | Interface zoom and text box hint alignment |
| `verify-mascot.ps1` | Renders every Mochi mood (output in `MochiDock/obj/mascot`) |
| `verify-device-battery.ps1`, `tools/gowa-relay/test-gowa-relay.ps1` | Wireless battery read probe; GOWA relay fan-out on loopback |
| `make-icon.ps1` | Icon regeneration |

Captures and reports are written under `MochiDock/obj/`. Capturing the README screenshots is described in [`images/README.md`](images/README.md).

## Feature status

| Feature | Status | Notes |
| --- | --- | --- |
| Dock, capsule, hover and navigation | Implemented | Mutually exclusive capsule content; manual card selection persists |
| Themes, typography, Advanced Settings window | Implemented | Default, Hojicha and Matcha; settings saved atomically; window not runtime-verified; Hojicha render-verified only |
| Date format (Automatic, year/day/month first, ISO) | Implemented | `DateDisplay` unit-tested across regions; header, Today, tasks, prayer, power and FX dates follow it |
| Interface size (80-200%) | Not runtime-verified | `InterfaceScalePercent` |
| Startup (logon task) and publish/installer scripts | Implemented | Installer not run on a clean machine; unsigned |
| Media, GSMTC controls, seeking | Implemented | Real-player seek behavior not runtime-verified |
| Lyrics (LRCLIB, offset, romanization) | Implemented | Race guards and cache unit-tested; live latency unmeasured |
| WhatsApp (GOWA local and shared, multi-account) | Not runtime-verified | Read sync and relay tested on loopback only; own mute state not readable |
| Email (Outlook Classic, Mailspring) | Partial | Outlook scan needs a human check; Mailspring is read only (Mark Read is hidden for its mail) |
| Email (IMAP, app password) | Not runtime-verified | Logic and reconnect covered by fake-session tests; never run against a real server |
| Discord toasts | Not runtime-verified | Listener unproven for unpackaged installs |
| Apps card (other apps' toasts) | Not runtime-verified | Filter unit-tested and card rendered; shares the Discord listener, so the same access caveat applies |
| Rain soon notice | Not runtime-verified | Policy unit-tested; the Open-Meteo 15-minute fields were checked against the live API |
| Stats 24-hour history and sustained-heat alert | Not runtime-verified | History, chart series and heat policy unit-tested; the 24 h chart is rendered by `verify-card-design.ps1` with synthetic lines; the light sampler's cost is unmeasured |
| Mochi day faces (meeting, birthday, all tasks done, up late) and hover glance | Not runtime-verified | Rules unit-tested; faces rendered by `verify-mascot.ps1` |
| Stats (CPU, GPU, memory, drives, network, processes) | Partial | CPU temperature is an estimate (real value needs PawnIO); some fallbacks remain |
| Battery page and wireless device batteries | Not runtime-verified | One mouse verified on hardware; Logitech HID++ untested |
| Gaming readout, profiles, summaries, ping | Not runtime-verified | Needs admin for ETW FPS; PUBG API and League local API untested in real games |
| Display resolution hotkey (default Ctrl+Alt+R) | Not runtime-verified | Conflicts with other programs are reported in Settings |
| Window-size shortcuts (Ctrl+Alt+1-4, flip Ctrl+Alt+F) | Not runtime-verified | Core rules unit-tested; Settings rows not rendered by a harness |
| Prayer (Aladhan, timeline, reminders, Tone/Silent) | Implemented | Audio and lock/resume checks are manual |
| Weather | Implemented | Open-Meteo |
| Today, calendar (iCal), tasks, reminders | Not runtime-verified | Secret link is DPAPI-protected; Google feed lag is a known limit |
| FX rates | Implemented | Yahoo with keyless fallbacks; indicative prices only |
| AI usage | Implemented | Parsers unit-tested; sign-in states need a human check |
| Pomodoro, Clipboard, Script, Downloads | Implemented | Downloads need yt-dlp and ffmpeg |
| Update check and one-click update (GitHub Releases) | Not runtime-verified | Feed, asset checks, download and SHA-256 verification unit-tested with fakes; the silent install and restart need a real newer release to verify; the repo must be public for the live check |
| Phone drop (File Shelf) | Implemented | Loopback tests only; real phone and firewall checks are manual |
| Mochi expressions, card faces and the face-change spin | Implemented | Every face, the spin and the whirlwind bounds checked by `verify-mascot.ps1`; the card faces and the spin seen in the running app; rare triggers (match results, overdue tasks, laptop battery) not runtime-verified |
| Persistence | Partial | Atomic queued saves; a daily copy of the last three days of settings; tests write to a scratch folder only; asynchronous loading and full validation pending |
| Service lifecycle and performance | Partial | No performance baseline measured; make no performance claims |

## Continuous integration

`.github/workflows/ci.yml` builds the app and runs the unit tests on Windows for every push to `main` and every pull request (documentation-only changes are skipped). It never launches the app, and the render harnesses are not run there.

## Releasing a version

1. Change `<Version>` in `MochiDock/MochiDock.csproj` and `MyAppVersion` in `installer/MochiDock.iss` to the same value (a test fails if they differ). Add the notes to `CHANGELOG.md`.
2. Build, run the tests and harnesses (the harnesses only run locally).
3. Commit, tag `v<version>` (for example `v0.2.0-beta`) and push `main` and the tag.
4. The **Release** workflow (`.github/workflows/release.yml`) then checks that the tag matches `<Version>`, runs the tests, builds the installer with `scripts\build_installer.bat` and creates a **draft** release (pre-release for a version with a `-` suffix) with `MochiDock-Setup-<version>.exe` attached and notes from the version's `CHANGELOG.md` section (`scripts/release-notes.ps1`). Review and publish the draft by hand. To release without GitHub Actions, run `scripts\build_installer.bat` and attach the setup under exactly that name yourself. GitHub records the asset's SHA-256 digest itself; installed copies use it to verify the download before **Update now** runs the setup silently (`/VERYSILENT ... /RELAUNCH`). Without the asset, or under another name, running copies only link to the release page.

## Human runtime checklist

Record date, build/commit, Windows version, monitor layout/DPI, hardware, prerequisites, expected and actual result for each check. Mark unavailable checks NOT RUN.

| Scenario | Expected result |
| --- | --- |
| Idle and hover | Capsule stays positioned; hover opens, leaving collapses unless pinned |
| Playback and rapid track changes | Controls stay responsive; old lyric requests never replace the new track's lyrics |
| Notifications | Correct source, message, selection and dismissal; no false successful sends |
| Quiet hours and snooze | No presentation while suppressed; overnight intervals handled |
| Gaming transitions | Dock moves to the bottom, click-through, end-of-session summary |
| Lock and display wake | Summary correct; no replayed stale actions or sounds |
| Monitors | Mixed DPI, negative coordinates, removal and reconnection keep the dock reachable |
| Themes and cards | All 18 cards readable in Default, Hojicha and Matcha at 100/125/150% |
| Startup | Enable and disable the logon task; only one launch at logon |
| Phone drop | Old or invalid links rejected, approval required, limits enforced, Stop/lock removes staging files |
| FX rates | Quotes, 24-hour badges, trajectory, fallback labels, refresh cooldown, lock/resume |
| Wireless batteries | Taskbar readout, tooltip, one silent low notice, survives an Explorer restart |
| Shutdown | No owned listeners, processes or ETW sessions remain; persistence flushed |
| Face changes | Opening Stats Battery, FX or Weather spins Mochi three times into the card's face; idle thoughts do not spin him; with Windows animations off faces switch without a spin |
| One-click update | Installed copy on an older version: Update now downloads, verifies, installs silently and restarts on the new version with settings and the logon task kept; a portable copy only links to the release; quick settings Updates box and the one-time "update available" notice are render- and unit-tested only |

## Storage and ports

- `%AppData%\MochiDock`: `settings.json`, `tasks.json` (atomic saves with a `.bak` backup; invalid files are kept as `.corrupt-<id>`).
- `%LocalAppData%\MochiDock`: GOWA session (`GOWA\storages`), `PairedDrop`, `FxRates\rates.json`, `Lyrics`, `Calendar\events.json`, `prayer-cache.json`, `logs\`.
- Ports: GOWA `3099`, webhook `47823`, phone drop `47824`.

## Phone drop limits

Start from File Shelf, pick a LAN IPv4 address and copy the fresh link to your phone (same Wi-Fi). The PC shows sanitized file names and exact sizes; nothing is accepted until you approve.

- Up to 10 files, 512 MiB per file, 1 GiB per request, two simultaneous uploads.
- A request expires after 60 seconds awaiting approval; uploads with no progress for 60 seconds abort; sessions expire after 15 minutes of inactivity.
- Stop, workstation lock and exit invalidate the link and delete partial transfers.
- Existing files are never overwritten (a numbered suffix is added).
- The secret travels in the URL fragment and an Authorization header. This is plain LAN HTTP, not encrypted; no firewall rules are changed.
