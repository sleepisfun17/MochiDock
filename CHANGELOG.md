# Changelog

All notable changes to MochiDock are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.3.0-beta]

### Added

- **Mascot picker and Shiro**: Settings > Appearance & Dock > Mascot chooses who is drawn: Auto (the theme's own: Mochi on Default, Hojicha Mochi on Hojicha, Matcha Mochi on Matcha, Kuro on Kuro Night), Mochi (grey), Matcha Mochi, Hojicha Mochi, Kuro or **Shiro**, a new white cat with dark eyes, pink cheeks and inner ears and an ice-blue spark on his tail. Any mascot works with any theme; switching spins the mascot into the new one inside the whirlwind, and the choice is also in the dock's quick settings, under Theme. On the light Matcha theme the light mascots get a darker, thicker outline so they stay visible. The fourth theme is called **Kuro Night**; a setting saved by an earlier build still loads. The tail now sways now and then and flicks when a cat is petted. No cat ever turns yellow: on Electric, Victory and the other yellow faces Shiro stays white with the colour in his outline.
- **Matching headsets**: the music, gaming and meeting headset now matches the mascot (graphite and silver for grey Mochi, roast brown and caramel for Hojicha Mochi, deep matcha and foam for Matcha Mochi; charcoal and yellow for Kuro; white and ice blue for Shiro) instead of bright blue or red. The cats wear the band behind their ears; gaming faces show small red lights on the cups and a meeting a microphone.
- **Kuro Night theme**: a fourth theme, a storm-night dark (deep indigo, not pure black) with an electric-yellow accent, next to Default, Hojicha and Matcha in Settings > Appearance & Dock. In it Mochi becomes **Kuro**, a black electric cat: the same round body with cat ears, a tapered tail behind him with a small spark at its tip that sways now and then and flicks when he is petted, yellow eyes and a soft glow instead of the shadow. Every face keeps the charcoal body; its own colour (red when angry, orange when hot, blue when freezing) shows in the rim and the glow, and the neutral faces glow electric yellow. The electric face glows brighter, petting him crackles, and his spin whirlwind is electric yellow.

### Changed

- **Mascot faces read at capsule size**: one status badge on the corner instead of small props above the head, and faces that looked alike have their own eyes. A new message and a notice show a red "!" badge (it pops in once) and wide eyes; sleeping, up late, coffee, the prayer bell, the break pause, victory, an overdue task, a low battery, money up and down, the weather faces, studying and all tasks done each have their own badge; thinking glances aside, waking, bored, up late and studying have half-lidded eyes, and surprised has round ones. Kuro and Shiro wear no headset any more: music, gaming and meetings are a note, a controller and a mic badge between their ears, Shiro on Matcha has one even outline on body, ears and tail, and Hot no longer has side puffs.

### Fixed

- **Volume after changing the audio output**: scrolling on the capsule or the volume bar sometimes stopped working right after switching the output (speakers, a Bluetooth headset). The audio endpoint is now rebuilt off Windows' notification thread without holding a lock during the audio calls, a dead endpoint is rebuilt and the call retried once, and a failed read no longer shows an invented 50.

## [0.2.2-beta]

### Fixed

- **Outlook**: new mail from classic Outlook was not picked up in 0.2.0-beta and 0.2.1-beta. In the installed app the Outlook helper started but never finished connecting (it was built for a different version of a library it shares with MochiDock), so it was restarted every minute. The installer build now checks the helper's connection before it packages it.
- **Outlook helper on PCs without Outlook**: when classic Outlook is not installed, MochiDock no longer starts the helper at all; it looks again every 10 minutes.
- **Welcome banner**: **Got it** always hides the banner, also after settings were imported while it was showing.

## [0.2.1-beta]

### Fixed

- **Welcome banner**: the first-run welcome banner no longer cuts off the bottom of the open card. After importing or reloading settings the banner could stay on screen while the dock sized itself as if it were gone; the dock now leaves room for the banner that is actually shown and re-sizes when it appears, disappears or rewraps.

## [0.2.0-beta]

### Added

- **Power chart**: the Battery page's top row is now one full-width **This PC** panel. A chart starts at 0 W under a round scale (for example 200 W), fills in from the right and, on a desktop, stacks the power into CPU, GPU and the rest of the PC, so a game spike shows as GPU; a laptop on battery shows its measured draw as one area. A dashed line marks the average and a dot the latest peak, with the latest watts, average, peak and this session's energy beside it.
- **WhatsApp engine in Settings**: Settings > WhatsApp has a **Local engine** row that shows whether the engine is bundled, downloaded (with its version) or missing, with **Download** (with progress) and **Remove** for the downloaded copy while it is not running. It shows in Remote server mode too, where the engine is not used. Your WhatsApp login is kept.
- **First-run welcome**: a short onboarding cue on first start, and a **Change city** link on the Weather card.
- **Keyboard and screen readers**: **Escape** closes the dock and menus, every control shows a focus outline, and controls across the dock and Settings have accessible names.

### Changed

- **Smaller download and install**: the WhatsApp (GOWA) engine is no longer bundled. It downloads (about 13 MB, checked against a pinned SHA-256) when you turn on WhatsApp in Local mode, and also at startup when Local WhatsApp is on but the engine is missing (a clean reinstall or settings moved to a new PC). Remote mode never downloads it. Together with dropping Windows Forms, a duplicate .NET runtime under OutlookHelper and unused language files, the installer is much smaller. Upgrades keep the engine the old version installed.
- **Stats charts**: the live charts fill in from the right while their history builds, instead of starting as a flat line across the chart, and they keep recording while the Stats card is closed (whenever hardware alerts are on, the readout is pinned or a game runs), so they are already filled when you open it.
- **Games page**: Playtime moved to the left column as separate **Today** and **This week** rows, and the right column fits without a scrollbar.
- **Tray menu**: grouped by purpose: the update (only while one is available), then Show Dock, Music, Stats and WhatsApp Dashboard; Pin Dock Open, Pin Stats to Pill and Snooze; Monitor, Display Resolution and Reset Dock Position; Settings…, Preferences and Settings & Data; Exit. Pins show a checkmark, Snooze shows when an active snooze ends, Start with Windows is in Preferences, and device-battery refresh lives in the battery widget's menu and on the Battery page.
- **Faster and lighter**: dock cards load the first time you open them, hardware stats are read off the UI thread, and idle animations (equalizer, marquee, Mochi's eyes, clock) no longer redraw when nothing changes. The system volume is followed through Core Audio callbacks instead of polling.
- **Matcha theme**: the Stats and Prayer cards follow the theme colours instead of fixed dark-mode colours.
- **Clipboard and Media**: clipboard items show when they are pinned or copied; the Media card shows the active audio output, keeps seek errors from overlapping and offers **Retry lyrics** only when lyrics failed.

### Fixed

- **Import settings**: a successful import is now a normal note instead of red error text. As before, passwords and the WhatsApp server login are not part of an export and have to be entered again on the new PC.
- **Crash when switching tabs**: the dock could close a few seconds after start when you switched tabs, because chart data read in the background was handed to the Stats card.
- **Settings recovery**: a corrupt `settings.json` is restored from its backup again instead of being reset to defaults at startup.
- **Saved mail passwords**: a failed read could wipe every saved IMAP app password; credential files are now written atomically.
- **Email**: IMAP no longer re-announces the whole inbox after a reconnect or stalls 30 s when disconnecting, and Outlook no longer drops new mail past 30 unread.
- **Fullscreen and dragging**: the dock no longer hides on an empty desktop or steals focus when it comes back, and dragging the compact pill no longer jitters.
- **Prayer**: the old "Adzan" option, which could silently mute prayer alerts, is gone.
- **Remote Desktop and monitors**: disconnecting Remote Desktop or turning every display off no longer crashes the dock, and positioning is correct on monitors with different scaling.
- **Upgrades**: the installer stops OutlookHelper and the WhatsApp engine before replacing files.
- **Stability**: fixes across the audio device reader, media sessions, the update download, lyrics fetching, FPS capture, the hardware sensors, WhatsApp message handling, Phone Drop, global hotkeys, wireless battery reads and weather parsing.

## [0.1.5-beta]

### Added

- **FX rates**: pick the three currency pairs the card shows (25 currencies) in Settings, Tabs, FX rates, with a swap button per pair. The default stays USD/JPY, USD/IDR and JPY/IDR. Pairs Yahoo does not list (VND/IDR, AED/IDR, MXN/IDR...) are built from the two USD rates, chart included, and the daily fallback computes any pair as a cross rate.

### Fixed

- **Wireless batteries**: Arbiter Studio Akitsu mice (Akitsu, Akitsu 3950, Akitsu Medium) are now named as such instead of "LAMZU wireless mouse", and the cable and receiver show as one device.
- **Email**: **Open Mail** on the game-session and welcome-back summary opens the newest mail where it came from (Mailspring, the account's web inbox or Outlook), like the Email card's Open. It used to start a `mailto:` link, which made Mailspring and other mail apps open an empty new message.

## [0.1.4-beta]

### Added

- **LAMZU small dongle**: wireless batteries now read LAMZU mice on their small/default 2.4 GHz dongle (a generic "2.4G Wireless Receiver", 25A7:FA7C), with the charging state. The dongle does not say which model is paired, so the mouse shows as "Wireless mouse". Read on a LAMZU Maya; charging not yet seen on hardware.

### Fixed

- **Settings window**: each section keeps its own scroll position when you switch between sections.

## [0.1.3-beta]

### Added

- **Updates in quick settings**: a version button in the top-right of the dock's settings card, next to Reconnect. Click it to check for updates; when a new version is waiting it turns accent and reads "Update to x", and it shows the download progress (click to cancel). When the automatic check finds a new version, a capsule notice says so once; tap it to open the quick settings.
- **Mail passwords**: removing a mail account now reminds you that its app password still works until you revoke it, with a button to the provider's app password page. Uninstalling always deletes saved passwords and secrets (mail app passwords, the WhatsApp server login, the calendar link), even when you keep your settings.

### Fixed

- **Gmail link**: opening a Gmail notice in the browser no longer ends on Gmail's "Temporary Error (404)" page; the account is now passed in a form Gmail reads.
- **Email settings**: a connected mail account now lines up with the rows below it (the address no longer floats in the middle of the row).

## [0.1.2-beta]

### Added

- GitHub Actions: every push and pull request is built and tested, and a version tag builds the installer and drafts the release on GitHub.
- **Apps card**: Windows notifications from the apps you choose (LINE, Telegram, Slack, Teams...) in Settings > Notifications > Other apps, with the app's own icon, a recent list and **Open**. Mutes, quiet hours, snooze, the gaming queue and the missed-while-away summary apply as for Discord. When Windows denies notification access, the settings page now says so.
- **Rain soon**: a short capsule notice when rain, snow or a thunderstorm is expected within the hour (Open-Meteo 15-minute forecast); tap it to open Weather. Settings > Location & Prayer > Rain soon.
- **Stats 24-hour history**: the CPU, GPU and Memory charts switch between the live 2 minutes and the last 24 hours (load, measured temperature and the day's peak; the line breaks where the PC slept). A light reading every 15 seconds, one point a minute, kept on this PC in `StatsHistory.json`; paused while locked or the screen is off; off or cleared in Settings > Hardware & power > History.
- **Sustained-heat alert**: a capsule notice once a measured CPU or GPU temperature has stayed at 80 °C or more for 10 minutes (both adjustable); Mochi looks hot while it lasts, and tapping the notice opens that chart's 24-hour view.
- **Hojicha theme**: a third theme, a warm roasted-tea dark with a caramel accent, next to Default and Matcha in Settings > Appearance & Dock. Mochi wears a kinako-tan body and a caramel whirlwind on it.
- **Mochi's day**: new faces for being up late (a crescent moon), your birthday (a party hat; set it in Settings > Appearance & Dock), a calendar meeting (a headset with a microphone) and all of today's tasks done (a green tick, with a cheer when you tick off the last one). Hovering over Mochi gets a quick happy glance.

### Changed

- **Settings export** writes your settings and tasks to one file for another PC and leaves out the shared WhatsApp server's webhook token and this PC's own values (monitor, positions, ports, startup, power and device choices). Import keeps this PC's values, adds or updates tasks and keeps the others, and still accepts an older settings.json.
- The Stats per-core load is a grid like Task Manager's: many threads wrap into rows (32 threads as 2 x 16) instead of hair-thin bars. On a hybrid CPU the P-core threads and the E-cores always get rows of their own, the header says how many of each ("PER CORE · 8P + 16E"; the tooltip has the thread counts) and the grid takes the page's spare height.
- The Stats pages (CPU, GPU, Memory, Drives, Network) use their full height: each column ends at the bottom of the card instead of leaving an empty band. The process lists show the top 10 instead of 8, and Memory's details add Total, Committed and Cached.
- The email card shows **Mark Read** only when it really marks the email read: in Outlook, or on the server for a mail account with "Mark as read on the server" turned on. For Mailspring and read-only accounts it used to only remove the email from MochiDock, which **Dismiss** already does.

### Fixed

- The Weather card's hourly forecast was labelled in GMT (Open-Meteo's default) instead of your local time; the three-day forecast now follows the city's own days.
- Locking the PC did not reset the missed Discord count, so an old count could reappear in the next summary.

## [0.1.1-beta]

### Added

- **Coffee face**: Mochi sips a coffee as one of his resting faces.

### Fixed

- Mochi no longer falls asleep while you type: only the pointer on the dock used to count as activity. He now rests (idle thoughts, a coffee, a bored sigh, a short doze) while you work elsewhere, and sleeps after 10 minutes without keyboard or mouse input.
- The Stats per-core bars are all the same width at every display scale and interface size.
- The FX one-month chart uses hourly prices like the seven-day chart (it showed about 22 daily points) and refreshes every 30 minutes.
- Running the setup again no longer tries to install PawnIO when it is already installed (the option is hidden then).
- Over Remote Desktop, MochiDock thought the screen was off (Windows reports the PC's physical monitors as off) and paused FX updates, hardware readings and Mochi's faces for the whole session. A remote session now counts as an awake display.

### Documentation

- The mood sheets in the README are labelled grids of every face, the same on both themes, and the user guide explains what makes each one appear.

## [0.1.0-beta]

First public release.

### Added

- **Dock and capsule**: a dynamic-island overlay that stays compact and expands on hover, with PerMonitorV2 multi-monitor and high-DPI support, Default and Matcha themes, tray controls, an Advanced Settings window, an interface size setting and Mochi, an expressive mascot.
- **Media and lyrics**: Windows media session (GSMTC) controls, a seekable timeline, audio-device switch, synced lyrics from LRCLIB with an adjustable offset, and romanized readings for Japanese, Korean and Chinese. Original-script lyrics are preferred over romaji copies, and a line too long for the capsule scrolls once, timed to finish before the next line.
- **Notifications**: WhatsApp through a local or shared GOWA engine (several accounts, replies, chat muting), Email from Outlook Classic and Mailspring (spam filtering, verification-code copy that ignores links and IDs), and Discord toast notifications.
- **Stats and gaming**: CPU, GPU, memory, drives, network and top processes; a gaming mode with a bottom-edge click-through readout (FPS, 1% and 0.1% lows, frame-time chart, ping statistics), per-game profiles and session summaries.
- **Window-size shortcuts**: resize the window you are using to a set size and centre it (Ctrl+Alt+1-4 for 1920x1080, 2560x1440, 1080x1920 and 720x1280, Ctrl+Alt+F to swap width and height), editable in Settings > Gaming & Display. Off until turned on.
- **Mochi's card faces**: Mochi reacts to what you are looking at, and spins round in 3D (three fast turns in about 0.6 s, in an anime-style whirlwind of glowing brush strokes, in the theme's colours) whenever his face changes: Electric on the Stats Battery page (circling lightning), Money on FX Rates (coin eyes, sad when the rate fell), the current sky on Weather (sunny, rainy, snowy or cloudy) and Studying while a Pomodoro runs. Warnings, notifications, games and sleep still come first; the user guide lists every face and its trigger.
- **Date format**: Settings > Appearance & Dock chooses how dates are written in the header, the Today calendar, tasks, prayer times and power sessions: Automatic (the Windows region, the default), year first (`2026/11/19 木`), day first, month first or ISO.
- **Capsule clock and weather**: the resting capsule measures itself so the clock is never clipped; Settings > Appearance & Dock chooses "At the edges" (clock centred) or "Even spacing".
- **Prayer**: location-aware prayer times with a timeline, qibla bearing, a ten-minute pre-notification and a Tone or Silent chime. Off until turned on in Settings > Location & Prayer.
- **Weather**: current conditions, hourly and three-day forecast for a configured city.
- **Today and calendar**: month calendar, events from a Google Calendar iCal link, tasks with quick add, event and task reminders, and optional Japan and Indonesia holidays.
- **FX**: USD/JPY, USD/IDR and JPY/IDR rates with a trajectory chart, using keyless providers.
- **AI usage**: remaining usage for Claude, Codex, Antigravity, Cursor, Gemini and GitHub Copilot, with a low-usage alert. Plans and windows that a provider does not report are left blank instead of guessed.
- **Wireless device batteries**: mouse, keyboard and headset battery levels in the Stats Battery page and an optional taskbar readout.
- **Power chip**: on a desktop the Stats header's Power chip shows the PC's total power with a sparkline. It is sampled continuously with the other readings, so the chip and the Battery page's power history keep running while other tabs are open.
- **Also**: Pomodoro, File Shelf with approved phone drop, Clipboard history, Script teleprompter, Downloads (yt-dlp and ffmpeg) and a missed-while-away Summary.
- **Installer**: a self-contained Inno Setup installer (`scripts\build_installer.bat`) with optional Desktop shortcut and logon task.

### Changed

- **Email for everyone**: add Gmail, Yahoo, iCloud, Fastmail or any IMAP account in Settings > Email with an app password (checked once, then stored encrypted; read-only unless "Mark as read on the server" is turned on). Outlook Classic and Mailspring keep working.
- **Opt-in WhatsApp**: the GOWA engine no longer starts by default; turn on "Enable WhatsApp" in Settings > WhatsApp. Existing installs that already linked a device or use a remote server keep it on.
- **Compact defaults**: new installs show Media, Stats and Weather; prayer times, holidays, window-size shortcuts and the other tabs are switched on in Settings.
- The installer's "Launch MochiDock" option now elevates correctly.
- **Optional PawnIO driver**: Settings > Hardware & power > CPU sensors shows whether it is installed and links to it; the setup has an unchecked task that installs it through winget (never bundled, never required).
- **Power total without a driver**: the Battery page's PC power estimate now reads CPU power from Windows' Energy Meter counters when the PawnIO driver is not installed, instead of showing "-".
- **Updates**: MochiDock looks for a newer GitHub release at startup and every 12 hours (can be turned off). **Update now** (Settings > Data & diagnostics, or the tray) downloads the release's setup, checks it against GitHub's SHA-256 checksum, installs it silently and restarts MochiDock; nothing happens without your click, and a copy not installed with the setup links to the release page instead.

### Fixed

- Network and disk rates of 1 MB/s and more were shown wrong (5 MB/s read "51 MB/s").
- Keyboard focus rings were cut off in Settings and did not fit checkboxes, toggles and radio buttons.
- After a theme switch, the AI usage, Prayer, FX, Today and Battery views kept the previous theme's colours until their data refreshed; they now redraw right away.
- The AI usage card is only as tall as its list of tools.
- Settings safety: running the developer tests could overwrite the settings file with defaults. The tests now use a scratch folder, WhatsApp setup never saves settings on its own, and MochiDock keeps a daily copy of the last three days' settings (`settings.backup-yyyyMMdd.json`).

### Known limitations

- This is a beta. Many features are implemented and unit-tested but have not been verified in a running app on real hardware or services.
- Administrator rights are required (the app manifest asks for them) because FPS capture uses ETW.
- The installer and binaries are unsigned, so Windows SmartScreen warns on first run. The installer has not been tested on a clean machine.
- The Discord toast listener is unverified for unpackaged installs; Windows may deny notification access.
- Mailspring "mark as read" is a no-op, because its database is opened read only.
- Phone drop and the shared GOWA server use plain HTTP on the local network, which is not encrypted.
- The CPU temperature is an estimate unless the optional PawnIO driver is installed.
- The one-click update has not yet been run against a real newer release: the download and checksum checks are tested, the silent install and restart are not.
