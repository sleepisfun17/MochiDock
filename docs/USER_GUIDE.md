# MochiDock user guide

A plain-language tour of the tabs and settings. MochiDock is a beta: see the status column in [docs/README.md](README.md) for what has and has not been verified on real machines.

## First run

A new install shows three tabs: **Media**, **Stats** and **Weather**. Everything else, including prayer times and the holiday calendars, is off until you switch it on:

- **Settings > Tabs** in the Advanced Settings window, or the switches on the dock's own Settings tab, turn tabs on and off.
- **WhatsApp** is off by default and starts nothing until you enable it (see below).
- Open Advanced Settings from the tray icon ("Settings...") or the dock Settings tab ("Open all settings"). Changes are saved automatically.

## Tabs

| Tab | What it does | Needs |
| --- | --- | --- |
| Media | Controls for whatever is playing, a seekable timeline and synced lyrics. | Any Windows media player. Lyrics come from LRCLIB. |
| Stats | CPU, GPU, memory, drives, network and the top 10 processes. The per-thread load is a grid: on a hybrid Intel CPU the P-core threads (purple) and the E-cores (light blue) have rows of their own, and the header shows the counts (hover for threads). Memory also lists committed memory and the file cache. The **2 MIN / 24 H** switch on the CPU, GPU and Memory charts shows the last 24 hours instead: load, the measured temperature (orange) and the day's peak, with gaps where the PC slept (Settings > Hardware & power > History turns it off or clears it). A Battery page and the games readout. On a desktop the Power chip shows the PC's total power and its recent history. | Works with any GPU. NVIDIA adds temperature, memory, clock and power; on AMD and Intel those show `-` unless a sensor provides them. CPU temperature is an estimate (marked `~`) unless the optional PawnIO driver is installed. |
| Weather | Current conditions, hourly and three-day forecast; a "Rain in about 20 min" capsule notice when rain, snow or a thunderstorm is due within the hour (Settings > Location & Prayer > Rain soon). | A city (Settings > Location & Prayer). |
| Prayer | Prayer times, countdown, current moon phase, qibla bearing and reminders. | A city and a calculation method. |
| WhatsApp | Recent messages, quick replies and reactions. Appears when WhatsApp is enabled. | A linked phone. |
| Email | New-mail notifications with sender, subject and a short preview; verification codes can be copied. **Mark Read** appears for Outlook mail and for accounts with "Mark as read on the server"; Mailspring mail and read-only accounts are only dismissed. | A Gmail, Yahoo, iCloud or other IMAP account (app password), or Outlook Classic or Mailspring installed. |
| Discord | Discord toast notifications. | Experimental for unpackaged installs. |
| Apps | Notifications from other apps you choose (LINE, Telegram, Slack, Teams...), with the app's icon and an **Open** button. Appears when one arrives. | Settings > Notifications > Other apps: pick an app that recently notified you, or type its name. Windows must allow notification access. |
| Today | Month calendar, the day's agenda and your tasks with reminders. | One or more Google Calendar iCal links for events ([how to get them](#connecting-google-calendar)). |
| Pomodoro | Focus timer. | Nothing. |
| File shelf | A temporary shelf for files, and phone drop over your Wi-Fi. | Phone and PC on the same network. |
| Clipboard | Clipboard history. | Nothing. |
| AI usage | Remaining usage for Claude, Codex, Antigravity, Cursor, Gemini and GitHub Copilot. | You signed in to those tools. |
| Script | A teleprompter-style script reader (Alt+Shift+S). | Nothing. |
| Downloads | A media downloader. | `yt-dlp` and `ffmpeg` (the installer can include them; ffmpeg is found in `Tools\`, `%LocalAppData%\MochiDock\Tools` or PATH). |
| FX rates | Three currency pairs of your choice (default USD/JPY, USD/IDR and JPY/IDR; change them in Settings, Tabs, FX rates; ⇄ swaps a pair) with a trajectory chart (7 days and 1 month in hourly steps, 1 year in daily steps). Indicative prices only. | Internet. |

## Advanced Settings sections

| Section | What you set there |
| --- | --- |
| General | Start with Windows, hide in fullscreen, hide from screen capture. |
| Appearance & Dock | Theme (Default, Hojicha, Kuro Night or Matcha), dock style, date format (Automatic follows the Windows region; or year first `2026/11/19 木`, day first `Thu 19 Nov 2026`, month first `Thu, Nov 19, 2026`, ISO `2026-11-19 Thu`), monitor, capsule content, interface size, Mascot (Auto is the theme's own: Mochi on Default, Hojicha Mochi on Hojicha, Matcha Mochi on Matcha and Kuro, the black electric cat, on Kuro Night; or choose grey Mochi, Matcha Mochi, Hojicha Mochi, Kuro or Shiro, the white cat, with any theme; the same choice is in the dock's quick settings under Theme). |
| Tabs | Which tabs the dock shows. |
| WhatsApp | Enable WhatsApp, pairing, short names for linked accounts (for example JP and ID), engine (local or remote), ports, read sync, muted and listed chats. |
| Email | Mail accounts (Gmail, Yahoo, iCloud, Fastmail or any IMAP server), which Outlook folders to watch (empty by default) and ad filtering. |
| Calendar | Calendar links (Work, Personal & more), colours, week start and holidays. |
| Location & Prayer | City for weather and prayer, calculation method, prayer sound. |
| Notifications | Mutes, quiet hours, snooze, reminders and sounds. |
| Gaming & Display | Resolution presets and hotkey (default Ctrl+Alt+R); window-size shortcuts, off until you turn them on, that resize the window you are using and centre it (defaults Ctrl+Alt+1 to 4 for 1920x1080, 2560x1440, 1080x1920 and 720x1280, Ctrl+Alt+F to flip width and height; editable); click-through, gaming offset, session summary. |
| Taskbar | Readouts on the Windows taskbar with a live Dark/Light preview: pick which display(s) to show on (Primary, same as MochiDock, a specific display, or All displays), toggle individual items (CPU load/temp, GPU load/temp, memory, disk, upload, download, next prayer with time/remaining countdown, weather temp in `°C`/wind, wireless device batteries), place each block on the Left edge or Right side (left of the tray or after the task buttons) and reorder them, adjust Left/Right/Vertical offsets, choose font (defaults to Windows taskbar font), size (Auto scales to fit Windhawk-tall taskbars), bold, block spacing, stable non-jittering width, device battery layout (`Auto`, `Side by side`, `Always stacked`), next prayer display (`Time & remaining`, `Remaining only`, `Time only`), countdown window before prayer, current-prayer elapsed window (`+Xm` after start), weather icon (`Colour emoji` or `Monochrome`), compact labels (`C/G/M/D`), network arrow style (`↑ ↓`, `▲ ▼`, colour emoji `🔼 🔽`), speed units (Bytes/Bits), swapped up/down lines, refresh rate (1–3 s), and adaptive label/value colours. Kept per PC. Click a block to open its card; right-click for a menu; hover for details. |
| Hardware & power | Alerts & temperatures (separate CPU and GPU Orange/Red colour limits unified with alerts and taskbar warnings), the optional PawnIO driver (real CPU temperature; shows whether it is installed and how to get it), wireless device batteries, power estimate. |
| Music & lyrics | Lyrics, pronunciation and timing offset. |
| AI usage | Which providers are tracked, and low-usage alerts. |
| Data & diagnostics | Export, import, reload, open files and logs, integration status. Export saves your settings and tasks for another PC; passwords, keys and this PC's own values (monitor, positions, ports, startup) stay behind, and import keeps the other PC's own values. Enter mail passwords and the WhatsApp server login again after an import. |

## Setting up WhatsApp

1. Settings > WhatsApp > turn on **Enable WhatsApp**. This starts the local engine (GOWA) on this PC.
2. Under **Link your phone**, click **Open QR dashboard**.
3. On your phone: WhatsApp > Linked devices > Link a device, and scan the QR code.
4. The status line changes to "Linked". Use **Choose chats** to silence groups or limit notifications to a list.

Sharing one engine between PCs: pick **Remote server** and enter the address (for example over Tailscale). The server needs its user name and password (GOWA basic auth), and its messages back to this PC carry a secret token. Unless the address starts with `https://` or the link runs over an encrypted network such as Tailscale, the traffic itself is not encrypted, so use it only on a network you trust.

## Connecting Google Calendar

The Today tab shows your events from one or more calendar *links* (for example **Work** and **Personal**, up to 10 feeds). MochiDock only reads them; it never changes your calendars and does not ask for your Google password.

**Get each link (Google Calendar in a browser, on a computer)**
1. Open [calendar.google.com](https://calendar.google.com) and click the gear icon > **Settings**.
2. In the left list under **Settings for my calendars**, click the calendar you want (for example your personal or work calendar).
3. Open **Integrate calendar** and copy **Secret address in iCal format**. It starts with `https://calendar.google.com/calendar/ical/` and ends with `.ics`.

**Set it in MochiDock**
1. Open Settings > **Calendar** (or click "Connect Google Calendar in Settings" on the Today tab).
2. Under **Add a calendar**, enter a name (e.g. `Work`), pick a colour, paste the secret link, and click **Add**. Repeat for additional calendars (e.g. `Personal`).
3. Events from 35 days back to 70 days ahead appear merged in the Today tab with colour dots on the calendar grid and agenda rows. Use **Connected calendars** to change colours, click **Refresh all** to fetch right away, or click `×` to remove an individual calendar.

**Good to know**
- The links work like passwords: anyone who has one can read that calendar. MochiDock stores all links encrypted together in `gcal.dat` for your Windows user, never in `settings.json`, exports or logs, never shows them again, and only sends each link to its own host. If one leaks, open the same Google page and use **Reset secret address** (the old link stops working, so remove the old calendar and add the new link).
- When 2 or more calendars are connected, the Today tab shows colour dots and calendar names next to each event, and capsule event reminders include the calendar name (e.g. `Work · Standup in 10 min`).
- Mascot status badges react to active timed events: meetings (conferencing link or meeting title), eating/lunch, gym/workout, and work schedules. Right-click any timed schedule in the Today tab to set its badge by title or choose a default badge for that entire calendar. During an active Working schedule, Mochi skips bored sighs and short dozes and shows a 5-minute Coffee break every 45 minutes of continuous work (resetting if you step away for 3+ minutes; real 10-minute away sleep still works normally).
- Google updates this feed with a delay, so a new or changed event can take a while (sometimes hours) to appear. MochiDock rechecks every 5 minutes while the Today tab is open and every 15 minutes in the background.
- The last downloaded events are kept on your PC per calendar, so they still show when you are offline.
- Not working? "Calendar link not accepted" or a failed calendar notice means a link was reset or copied wrong. "That link isn't a calendar feed" means it is not an `.ics` link. Some company or school Google accounts do not allow the secret address; ask the admin.
- Other calendars work too if they give an `https://` or `webcal://` `.ics` link (for example a published Outlook.com or iCloud calendar).
- Reminders for events (and tasks with a time) are set in Settings > Notifications. Japan and Indonesia holidays are built in and can be switched on in Settings > Calendar.

## Setting up email (Gmail and other IMAP accounts)

1. Create an **app password** at your provider. For Gmail turn on 2-step verification first, then open the app passwords page (Settings > Email > "How to create one" opens it). Outlook.com personal accounts do not offer app passwords for IMAP, so they are not supported yet.
2. Settings > Email > pick the provider, type your address and the app password, then **Test and add account**. MochiDock signs in once to check it, then keeps the password encrypted for your Windows user.
3. New mail that arrives while MochiDock runs appears in the dock. Mail that was already in the inbox is not announced.

How safe is it? MochiDock only reads sender, subject and a short preview, and stores no mail on disk. An app password still gives access to the whole mailbox (IMAP cannot be limited to read-only), so create one just for MochiDock and revoke it in your account when you stop using it. "Mark as read on the server" is off by default and is the only thing MochiDock can change in your mailbox. Removing the account in settings deletes the saved password from this PC and then reminds you that the app password itself still works: **Revoke it now** opens your provider's app password page (for Gmail, myaccount.google.com/apppasswords), because no provider lets an app revoke its own app password. Uninstalling MochiDock always deletes the saved mail passwords, the WhatsApp server login and the calendar link, even when you keep your settings, and reminds you to revoke the app password too.

## Real CPU temperature (optional PawnIO driver)

Without extra software MochiDock shows an estimated CPU temperature (marked `~`). For a real reading install **PawnIO**, a small third-party kernel driver (freeware, its own licence, https://pawnio.eu). Two ways:

- Tick **Install the PawnIO driver** on the options page of the MochiDock setup. It runs `winget install --id namazso.PawnIO --exact --source winget` for you; winget verifies the download. If winget is missing the setup tells you where to download PawnIO.
- Or later: Settings > Hardware & power > **CPU sensors** shows whether it is installed, and has **Get PawnIO** and **Copy winget command**.

Restart MochiDock afterwards. PawnIO is never required: the power total on the Battery page works without it (it uses Windows' own Energy Meter counters), and MochiDock never installs it by itself.

## Gaming mode

While a game runs, the dock moves to the bottom of the screen, becomes click-through and shows FPS, GPU and CPU. A summary appears afterwards. FPS capture needs the app to run as Administrator, which it always does.

## Mochi's faces

Mochi changes face with what is going on. At the small size in the capsule a face is told by its eyes and by one small badge on the corner (a red "!" for a message, a moon for up late, a cloud, sun or umbrella for the weather, and so on), not by little props above his head. With **Expressive Mochi** off (Settings > Appearance & Dock) only the basic faces are used.

| Face | When |
| --- | --- |
| Idle, thinking | Nothing special; alternates with the idle thoughts. |
| Music (headset) | Something is playing. The headset matches the mascot (graphite with a silver rim for Mochi on Default, and so on). |
| Alert, Excited | A notification or a new message: wide eyes and a red "!" badge on the corner. |
| Sleeping | The PC is locked, the screen is off, or nobody has used the keyboard or mouse for 10 minutes; also a short doze while resting (below). |
| Waking | Back after four hours away, or MochiDock started in the morning. |
| Coffee, Bored | Resting while you work elsewhere (below). |
| Happy, Cheering | A task was ticked off; a Pomodoro finished. |
| Surprised | A download finished or a file arrived from the phone. |
| Praying | Prayer time (prayer times on). |
| Focused, Worried, Break time | Playing a game; the frame rate or ping is bad; the break reminder. |
| Victory, Defeat | A League of Legends or PUBG match result. |
| Angry | A task is 10 to 90 minutes overdue and nobody acknowledged it (overdue reminders on). |
| Hot, Freezing | The GPU reached its alert temperature, or it is 33 °C or more / 3 °C or less outside. |
| Dizzy | CPU or RAM maxed out for a while, or Mochi was clicked too many times. |
| Drained | A laptop battery at 15% or less, unplugged. |
| Electric | The Stats Battery page is open: star eyes and lightning circling him. |
| Money | The FX Rates card is open: coin eyes, sadder when the rate fell. |
| Sunny, Rainy, Snowy, Cloudy | The Weather card is open: the current sky (a clear night is cloudy). |
| Studying | A Pomodoro focus session is running (coffee during its break). |
| In a meeting | A timed event from your calendar is happening now: a headset with a microphone. (While gaming the headset has small red lights on the cups.) |
| Birthday | Your birthday (Settings > Appearance & Dock > Your birthday): a party hat and confetti all day. |
| All tasks done | Every task due today is ticked off (a cheer for the last one, then a green tick). |
| Up late | Between 23:00 and 05:00: heavy eyes and a moon badge on the corner. |

Warnings (hot GPU, overdue task, low battery, overload), notifications, games and sleep always win over the card faces. The day faces (meeting, birthday, eating, gym/workout, working, all tasks done, up late) show while Mochi is only resting, so music, the card faces and the weather come first. Hover over Mochi for a quick happy glance; click him to pet him.

**Resting while you work.** Typing counts as being there. When you are busy at the PC but have not touched the dock for a minute and a half, Mochi rests instead of falling asleep: every 2 minutes he moves on through his idle thoughts, a coffee, a bored sigh and a short doze. During an active `Working` calendar schedule, he skips the bored sighs and short dozes and takes a 5-minute Coffee break every 45 minutes of continuous work instead. He only really sleeps once nobody has used the keyboard or mouse for 10 minutes, and he looks up as soon as you come to the dock.

When his face changes, Mochi spins round three times in about 0.6 seconds inside a little anime whirlwind (glowing brush strokes, gold for grey Mochi, caramel for Hojicha Mochi, electric yellow for Kuro and green for Matcha Mochi) and comes round wearing the new face; he turns as a rounded body, not a flat card. He does not turn for the small blink between thoughts while idle, or for swaps between the gaming faces during a game, and not at all when Windows animations are off.

## Updating and uninstalling

- **Update:** MochiDock checks GitHub for a newer release at startup and every 12 hours (the version button in the top-right of the dock's quick settings, next to Reconnect: click it to check now; or Settings > Data & diagnostics > Updates, where the automatic check can be turned off). When the automatic check finds a new version, a capsule notice says so once (it waits while you game, during quiet hours or snooze, and while the PC is locked); tap it to open the quick settings. Until you dismiss that version, a small accent ↑ dot (tooltip "Update available") stays on the compact capsule, hidden while you game or a notification or notice holds the capsule, and the expanded dock shows a banner with **Update** (installs after the confirmation, or opens the release page on a portable copy), **Release notes** and **Dismiss**; while the download runs the banner shows its progress and only **Cancel**. Dismiss hides the dot, banner and notice for that version only; a newer release shows them again. The quick settings button then turns accent and reads "Update to x", the tray menu shows "Update to x…", and Settings shows **Update now**. While the update downloads, the button shows the progress and is disabled; use **Cancel** in the update banner or Settings > Data & diagnostics > Updates to stop it. One click (and a confirmation) downloads the release's setup from GitHub, checks it against the SHA-256 checksum GitHub publishes for it, installs it silently and starts MochiDock again on the new version, keeping your settings; MochiDock is gone for about 20 seconds. If the download does not match, nothing is installed. Nothing is ever downloaded or installed without your click. A copy that was not installed with the setup (a portable or self-built copy) cannot update itself: use **View release** and run the new setup by hand.
- **Uninstall:** it asks whether to delete your settings and data (`%AppData%\MochiDock`, `%LocalAppData%\MochiDock`, including the linked WhatsApp session). Saved passwords (mail app passwords, the WhatsApp server login and the calendar link) are always deleted, whatever you answer; if a mail app password was saved, it reminds you to revoke it at your provider.

## Troubleshooting

- **Windows SmartScreen warns on first run:** the beta is not code-signed yet.
- **Settings were lost or overwritten:** MochiDock keeps a daily copy of the last three days in `%AppData%\MochiDock` (`settings.backup-yyyyMMdd.json`). Quit MochiDock from the tray, copy the backup over `settings.json`, then start it again.
- **Downloads says "ffmpeg required":** install ffmpeg into one of the folders above, or put it on PATH.
- **WhatsApp stays "Not linked yet":** open the QR dashboard again and rescan; use **Reconnect**.
- **Discord shows nothing:** the toast listener is unproven for unpackaged installs.
- **Phone drop and a shared GOWA server use plain HTTP on the local network:** both need a secret (phone drop also asks you to approve every transfer), but the traffic is not encrypted, so someone on the same network could read it. Use them only on networks you trust; a shared GOWA server can be reached over Tailscale or an `https://` address instead.
