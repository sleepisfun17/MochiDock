<p align="center">
  <img src="docs/images/mochi-icon.png" alt="Mochi, the MochiDock mascot" width="160">
</p>

<h1 align="center">MochiDock</h1>

<p align="center"><b>A dynamic-island companion for Windows.</b><br>
A native .NET 8 WPF overlay that sits at the top of your screen (or at the bottom while you game) and shows media, messages, hardware stats and more.</p>

<p align="center"><a href="https://github.com/sleepisfun17/MochiDock/actions/workflows/ci.yml"><img src="https://github.com/sleepisfun17/MochiDock/actions/workflows/ci.yml/badge.svg" alt="Build and tests"></a> <a href="https://github.com/sleepisfun17/MochiDock/releases/latest"><img src="https://img.shields.io/github/v/release/sleepisfun17/MochiDock?include_prereleases&amp;label=release" alt="Latest release"></a></p>

> **Beta (0.1.5-beta).** Many features have been built and unit-tested but not yet verified on a wide range of machines. See the [known limitations](CHANGELOG.md).

<p align="center">
  <img src="docs/media/hover-expand.gif" alt="The capsule expanding on hover and collapsing again" width="660">
</p>

## What it is

MochiDock is a small capsule that lives at the top of your screen. Notifications appear as a compact pill and expand when you hover. Open it to see what is playing, new messages, your PC's stats, the weather, your day and more. Mochi, a small mascot, reacts to music, alerts, games, battery and the time of day.

## Features

A new install starts small: the **Media**, **Stats** and **Weather** tabs, plus the notifications and gaming mode. Everything else is one switch away. Tabs are switched in **Settings > Tabs** (or the switches on the dock's own Settings tab); the other features in the Settings section named below. Open Settings from the tray icon or the gear on the dock.

| Feature | What it does | New install | Where to switch it |
| --- | --- | --- | --- |
| **Media and lyrics** | Controls for Spotify, YouTube Music and browser playback, a seekable timeline and synced lyrics (LRCLIB) with readings for Japanese, Korean and Chinese. | On | Always shown; lyrics in Music & lyrics |
| **Stats** | CPU, GPU, memory, drives, network, top processes and a Battery page. | On | Tabs |
| **Weather** | Current weather, hourly and three-day forecast, and the temperature in the capsule. | On | Tabs; city in Location & Prayer |
| **Gaming mode** | In games the dock moves to the bottom, becomes click-through and shows FPS, GPU and CPU, with a session summary afterwards. Ctrl+Alt+R switches between screen resolution presets. | On | Gaming & Display |
| **Notifications** | WhatsApp, Email and Discord messages appear as a small capsule first and expand on hover. Notifications from other apps you choose (LINE, Telegram, Slack...) get their own Apps card. | Email and Discord on, WhatsApp off, other apps none | WhatsApp, Email, Notifications |
| **WhatsApp** | Recent messages, quick replies and reactions through a local or shared GOWA engine. Starts nothing until you enable it. | Off | WhatsApp |
| **Email** | New-mail notices from Gmail or other IMAP accounts (app password), Outlook Classic and Mailspring; verification codes can be copied. | Ready, add an account | Email |
| **Prayer times** | Location-aware schedule, qibla bearing, reminders and a Tone or Silent chime. | Off | Tabs, or Location & Prayer |
| **Today and calendar** | Month calendar, events from a Google Calendar link ([how to connect it](docs/USER_GUIDE.md#connecting-google-calendar)), tasks with reminders, optional Japan and Indonesia holidays. | Off | Tabs; link and holidays in Calendar |
| **FX rates** | Three currency pairs of your choice from 25 currencies (default USD/JPY, USD/IDR and JPY/IDR) with a trajectory chart, no API key needed. | Off | Tabs |
| **AI usage** | Remaining usage for Claude, Codex, Antigravity, Cursor, Gemini and GitHub Copilot, with an optional low-usage alert. | Off | Tabs; providers in AI usage |
| **Taskbar readouts** | Two-line blocks beside the system tray: CPU and GPU, memory and disk, network speed, the next prayer, the weather and device batteries; click one to open its card. | Off | Taskbar |
| **Wireless device batteries** | Mouse, keyboard and headset levels on the Stats Battery page and an optional taskbar readout. | Off | Hardware & power, Taskbar |
| **Window-size shortcuts** | Resize the window you are using to a set size and centre it (Ctrl+Alt+1 to 4, Ctrl+Alt+F to swap width and height; editable). | Off | Gaming & Display |
| **Pomodoro** | Focus timer. | Off | Tabs |
| **File Shelf** | A temporary shelf for files, and phone drop over your Wi-Fi (you approve each file). | Off | Tabs |
| **Clipboard** | Clipboard history. | Off | Tabs |
| **Script** | A teleprompter-style script reader (Alt+Shift+S). | Off | Tabs |
| **Downloads** | A media downloader (yt-dlp and ffmpeg). | Off | Tabs |
| **Sounds** | Short notification and Mochi sounds. | On | Notifications |
| **Mochi** | The mascot's faces follow what is going on (music, messages, games, the weather, a hot GPU, overdue tasks…) and the card you open (Electric on the Battery page, Money on FX, the sky on Weather, Studying during a Pomodoro). He spins round in a little whirlwind when his face changes. [Every face and its trigger](docs/USER_GUIDE.md#mochis-faces). | On | Appearance & Dock (Expressive Mochi) |
| **Updates** | Checks GitHub for a new version and shows a one-time notice when one is out. The version button at the top of the dock's quick settings checks on click and turns into **Update to x** when an update is waiting; it downloads the update, verifies its checksum, installs it silently and restarts MochiDock. Nothing installs without your click. | Check on | Quick settings button; automatic checks in Data & diagnostics |

Four themes, `Default` (dark), `Hojicha` (warm roasted-tea brown with a caramel accent), `Kuro Night` (storm-night indigo with an electric-yellow accent, where Mochi becomes Kuro, a black electric cat) and `Matcha` (warm oat and green), are in Appearance & Dock, together with the Mascot (Auto, grey Mochi, Matcha Mochi, Hojicha Mochi, Kuro the black cat or Shiro the white cat, with any theme; also in the dock's quick settings), along with the dock style, date format, monitor and interface size. MochiDock is multi-monitor and high-DPI aware. The [user guide](docs/USER_GUIDE.md) explains every tab and setting.

## Screenshots

![Switching from Default to Matcha, then Kuro Night, and back to Default](docs/media/theme-switch.gif)

| Default | Matcha | Kuro Night |
| --- | --- | --- |
| ![Stats card, Default theme](docs/images/live/stats-default.png) | ![Stats card, Matcha theme](docs/images/live/stats-matcha.png) | ![Stats card, Kuro Night theme with Kuro](docs/images/live/stats-kuro-night-kuro.png) |
| ![Media card with lyrics, Default theme](docs/images/live/media-default.png) | ![Media card with lyrics, Matcha theme](docs/images/live/media-matcha.png) | ![Media card with lyrics, Kuro Night theme with Kuro](docs/images/live/media-kuro-night-kuro.png) |
| ![FX Rates card, Default theme](docs/images/live/fx-default.png) | ![FX Rates card, Matcha theme](docs/images/live/fx-matcha.png) | ![FX Rates card, Kuro Night theme with Kuro](docs/images/live/fx-kuro-night-kuro.png) |
| ![Weather card, Default theme](docs/images/live/weather-default.png) | ![Weather card, Matcha theme](docs/images/live/weather-matcha.png) | ![Weather card, Kuro Night theme with Kuro](docs/images/live/weather-kuro-night-kuro.png) |
| ![Prayer times card, Default theme](docs/images/live/prayer-default.png) | ![Prayer times card, Matcha theme](docs/images/live/prayer-matcha.png) | ![Prayer times card, Kuro Night theme with Kuro](docs/images/live/prayer-kuro-night-kuro.png) |

<table>
  <tr>
    <td><img src="docs/images/live/today-default.png" alt="Today card, Default theme"></td>
    <td><img src="docs/images/live/today-matcha.png" alt="Today card, Matcha theme"></td>
  </tr>
  <tr>
    <td colspan="2"><img src="docs/images/live/pomodoro-matcha.png" alt="Pomodoro card, Matcha theme"></td>
  </tr>
  <tr>
    <td colspan="2"><img src="docs/images/live/ai-usage-default.png" alt="AI usage card, Default theme"></td>
  </tr>
</table>

The compact capsule in the Default, Matcha and Kuro Night themes, here showing the current song:

![Compact capsule, Default theme](docs/images/live/capsule-media-default.png)
![Compact capsule, Matcha theme](docs/images/live/capsule-media-matcha.png)
![Compact capsule, Kuro Night theme](docs/images/live/capsule-media-kuro-night-kuro.png)

Mochi's faces, in both themes ([what makes each one appear](docs/USER_GUIDE.md#mochis-faces)):

![Mochi's moods on the Default dark theme](docs/images/mochi-moods-default.png)

![Mochi's moods on the Matcha theme](docs/images/mochi-moods-matcha.png)

Pick another mascot in Settings > Mascot: Kuro, a black electric cat (at home on the Kuro Night theme), or Shiro, a white cat:

![Kuro's moods on the Kuro Night theme](docs/images/mochi-moods-kuro.png)

![Shiro's moods on the Default dark theme](docs/images/mochi-moods-shiro.png)

Changing the mascot in quick settings spins Mochi into Kuro, Shiro and back:

![Mochi turning into Kuro and Shiro on the Kuro Night theme, then back to Mochi](docs/media/mascot-switch.gif)

Every mascot works on every theme:

![Mochi, Matcha Mochi, Hojicha Mochi, Kuro and Shiro on the Default, Hojicha, Kuro Night and Matcha themes](docs/images/mascots-on-themes.png)

## Requirements

- Windows 10 version 2004 (build 19041) or later, or Windows 11, 64-bit.
- Nothing else is required for the basics. Optional, each for its own feature:
  - **GPU stats:** GPU name, driver and usage come from Windows itself (WMI and the GPU Engine counters), so they work with NVIDIA, AMD and Intel graphics. An NVIDIA driver adds more detail (temperature, memory, clock and power through NVML); on AMD and Intel those extra values are read from hardware sensors when available and show as `-` otherwise. Only NVIDIA has been checked on real hardware so far.
  - **PawnIO:** a real CPU temperature (a separate third-party driver; the setup can install it for you with winget if you tick the option, or see Settings > Hardware & power > CPU sensors). Without it the CPU temperature is an estimate, marked with `~`. It is not needed for the power total on the Battery page: CPU power is read from Windows' own Energy Meter counters when PawnIO is missing (on Intel CPUs with integrated graphics that figure already includes the graphics). GPU power needs an NVIDIA card or a sensor that provides it; the total is always marked as an estimate.
  - **Email:** a Gmail, Yahoo, iCloud or other IMAP account with an app password (Gmail only offers app passwords once 2-step verification is on; see [setting up email](docs/USER_GUIDE.md#setting-up-email-gmail-and-other-imap-accounts)), or classic Outlook or Mailspring.
  - **Calendar:** a Google Calendar (or other `.ics`) link, see the [user guide](docs/USER_GUIDE.md#connecting-google-calendar).
  - **`ffmpeg.exe`** (MP3 in Downloads).

## Install

1. Download `MochiDock-Setup-<version>.exe` from the [GitHub Releases](https://github.com/sleepisfun17/MochiDock/releases) page and run it. The installer is self-contained and needs no .NET runtime.
2. The installer is an **unsigned beta**, so Windows SmartScreen may warn. Choose "More info", then "Run anyway".
3. MochiDock needs **Administrator rights** (FPS capture uses ETW), so Windows asks for approval each time it starts. The optional "Start with Windows" task launches it elevated without a prompt.

**Updating:** you only download the setup once. Later versions install from inside MochiDock (the version button in the dock's quick settings, the tray menu, or Settings > Data & diagnostics), keeping your settings.

**Uninstalling:** use Windows Settings > Apps. The uninstaller asks whether to keep your settings and data, always deletes saved passwords, and reminds you to revoke a mail app password at your provider. See [updating and uninstalling](docs/USER_GUIDE.md#updating-and-uninstalling).

## Build from source

Requires the .NET 8 SDK. From the repository root:

```powershell
dotnet build MochiDock/MochiDock.csproj -c Release
dotnet test MochiDock.Tests/MochiDock.Tests.csproj -c Release
```

To build the installer (Inno Setup 6 required, output `installer\Output\MochiDock-Setup-<version>.exe`):

```powershell
.\scripts\build_installer.bat
```

`scripts\build_and_publish.bat` publishes a portable build to `Publish\` (requires the .NET 8 Windows Desktop Runtime). More commands and the render harnesses are in the [developer documentation](docs/README.md).

## Data and privacy

Settings and tasks live in `%AppData%\MochiDock`. Caches and the WhatsApp (GOWA) session live in `%LocalAppData%\MochiDock`. Phone drop and a shared GOWA server use plain HTTP on your local network, which is not encrypted. Update checks and **Update now** only contact this project's GitHub releases, and a downloaded update is checked against GitHub's SHA-256 checksum before it runs.

**Saved passwords.** Mail app passwords, the shared WhatsApp server login and the calendar link are stored encrypted for your Windows user (Windows DPAPI) in `%LocalAppData%\MochiDock`, and never in settings, exports or logs. A program running as your Windows user could still read them, as with browser cookies. A mail app password can open the whole mailbox, because IMAP cannot be limited to read-only: MochiDock only reads the sender, subject and a short preview, stores no mail, and changes nothing unless you turn on "Mark as read on the server". Create an app password just for MochiDock. Removing an account deletes its password from this PC and reminds you to revoke it at your provider, since no provider lets an app revoke its own app password; uninstalling always deletes every saved password, even when you keep your settings.

## Issues and ideas

Bug reports, plans and feature ideas go to [GitHub Issues](https://github.com/sleepisfun17/MochiDock/issues).

## Credits

The idea for MochiDock came from [coucou](https://github.com/Louis-CFM/coucou), a notch companion for macOS: MochiDock is a light take on it for Windows. Some card layouts were also influenced by [Oyenza Studio](https://github.com/lucacadalora/oyenza-studio). All of MochiDock's code, including the lyric lookup, the Japanese, Korean and Chinese readings and the AI usage readers, is its own. Card icons are [Microsoft Fluent Emoji](https://github.com/microsoft/fluentui-emoji) (MIT). Other bundled code, fonts, icons and data, and the online services MochiDock reads, are listed in the [third-party notices](THIRD-PARTY-NOTICES.txt).

MochiDock is an independent project. WhatsApp, Discord, Gmail, Outlook, Spotify, YouTube Music and the other product names are trademarks of their owners; MochiDock is not affiliated with or endorsed by them.

## Documentation and licence

- [User guide](docs/USER_GUIDE.md): what each tab and setting does, and how to set up WhatsApp.
- [Developer documentation](docs/README.md): layout, commands, feature status and runtime checks.
- [Changelog](CHANGELOG.md) and known limitations.
- [MIT licence](LICENSE).
- [Third-party notices](THIRD-PARTY-NOTICES.txt).
