# README images

- `mochi-icon.png`: 512 px render of the current Mochi, also the source of `MochiDock/icon.ico`. Regenerate both with `pwsh -NoProfile -STA -File tools/make-icon.ps1` after a Release build.
- `mochi-moods-*.png`: from `tools/verify/verify-mascot.ps1` (`MochiDock/obj/mascot`). Drawn by the real mascot control, not captured.
- `live/<card>-<theme>.png`: captures of the running dock, Default and Matcha. See below.
- `../media/*.gif`: recordings of the running dock (hover expand, card tour, theme switch).

## How the live captures are made

From an **elevated** PowerShell on the local console (not RDP, which gives wrong CPU/GPU readings), with MochiDock already running and "Exclude from screen capture" off in Settings:

1. `pwsh -NoProfile -STA -File tools/capture/backdrop.ps1` in a separate window: a borderless gradient window behind the dock so no other program shows. It closes itself after 30 minutes or on Esc.
2. `tools/capture/capture-stills.ps1 -Theme default` (then `-Theme matcha` after switching the theme): clicks each tab and saves a cropped PNG per card. It uses UI Automation to find the tabs, and it blanks text matching the patterns in the script (account line, calendar title). It skips Settings and Clipboard because they show personal data.
3. `tools/capture/capture-clips.ps1 -Clip hover|tour|theme`: records the dock with ffmpeg (`gdigrab`) and converts the clip to a GIF in `docs/media`.

Before capturing: turn on the tabs the scripts visit (Media, Stats, Weather, Pomodoro, Prayer, Today, AI usage, FX Rates; they are found by tooltip, so other tabs may stay on; prayer times are off by default on new installs), and play a track whose cover art and lyrics you may publish (your own or royalty-free), so no commercial album art or lyrics end up in the README.

`tools/capture/capture-lib.ps1` holds the shared helpers (dock lookup, auto-crop, mouse movement, redaction). Review every image for private data before committing, and put your settings back (theme, Pomodoro tab, Stats module) when done.

Not captured: the Gaming readout, notification capsules, and the WhatsApp, Email and Discord cards (they show real conversations).
