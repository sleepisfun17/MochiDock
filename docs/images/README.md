# README images

- `mochi-icon.png`: 512 px render of the current Mochi, also the source of `MochiDock/icon.ico`. Regenerate both with `pwsh -NoProfile -STA -File tools/make-icon.ps1` after a Release build.
- `mochi-moods-*.png`: the labelled face grids (`moods-grid-default.png` / `moods-grid-matcha.png`) from `tools/verify/verify-mascot.ps1` (`MochiDock/obj/mascot`), copied here. Drawn by the real mascot control, not captured; the same faces on both themes plus one frame of the face-change spin.
- `social-preview.png`: the 1280 x 640 card GitHub shows when the repository link is shared. Regenerate with `pwsh -NoProfile -STA -File tools/make-social-preview.ps1` after a Release build, then upload it by hand in the repository's Settings > General > Social preview (GitHub has no API for it).
- `live/<card>-<theme>.png` (and `live/<card>-<theme>-<mascot>.png`): captures of the running dock in any theme (Default, Hojicha, Kuro Night, Matcha) and, optionally, with a chosen mascot. See below.
- `../media/*.gif`: recordings of the running dock (hover expand, card tour, theme switch, mascot switch).

## How the live captures are made

From an **elevated** PowerShell on the local console (not RDP, which gives wrong CPU/GPU readings), with MochiDock already running and "Exclude from screen capture" off in Settings:

1. `pwsh -NoProfile -STA -File tools/capture/backdrop.ps1` in a separate window: a borderless gradient window behind the dock so no other program shows. It closes itself after 30 minutes or on Esc.
2. `tools/capture/capture-stills.ps1 -Theme default|hojicha|kuro-night|matcha [-Mascot auto|mochi|matcha-mochi|hojicha-mochi|kuro|shiro] [-Apply]`: clicks each tab and saves a cropped PNG per card. Without `-Apply` the dock must already wear that theme; with `-Apply` (or a `-Mascot`) the script switches the theme and mascot in quick settings itself and puts the previous ones back at the end. For example `-Theme kuro-night -Mascot kuro -Apply` saves `live/<card>-kuro-night-kuro.png`. It uses UI Automation to find the tabs, and it blanks text matching the patterns in the script (account line, calendar title). It skips Settings and Clipboard because they show personal data.
3. `tools/capture/capture-clips.ps1 -Clip hover|tour|theme|mascot`: records the dock with ffmpeg (`gdigrab`) and converts the clip to a GIF in `docs/media`. `theme` switches Matcha, Kuro Night and Default in quick settings (`theme-switch.gif`); `mascot` spins Mochi into Kuro, Shiro and back (`mascot-switch.gif`; set the theme you want first, Kuro Night shows Kuro best). Both put your theme and mascot back afterwards.

Before capturing: turn on the tabs the scripts visit (Media, Stats, Weather, Pomodoro, Prayer, Today, AI usage, FX Rates; they are found by tooltip, so other tabs may stay on; prayer times are off by default on new installs), and play a track whose cover art and lyrics you may publish (your own or royalty-free), so no commercial album art or lyrics end up in the README.

`tools/capture/capture-lib.ps1` holds the shared helpers (dock lookup, auto-crop, mouse movement, redaction, and `Set-Theme` / `Set-Mascot` / `Get-Look` / `Restore-Look`, which click the quick-settings radios by their AutomationId because the labels "Matcha" and "Hojicha" appear in both the Theme and the Mascot row). Review every image for private data before committing, and put your settings back (theme, Pomodoro tab, Stats module) when done.

Not captured: the Gaming readout, notification capsules, and the WhatsApp, Email and Discord cards (they show real conversations).
