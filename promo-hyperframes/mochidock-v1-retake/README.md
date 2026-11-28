# MochiDock v1 retake

This 53.5-second silent portrait promo uses the refined v1 design and pacing with the new desktop and gaming recordings. A 4.5-second taskbar scene shows both captured strips in one fixed frame. The gaming scene uses the selected gameplay clip.

From `promo-hyperframes`, with the shared assets, HyperFrames and FFmpeg available:

```powershell
node mochidock-v1-retake/stage.mjs
npx hyperframes lint mochidock-v1-retake
npx hyperframes render mochidock-v1-retake -o out/mochidock-v1-retake.mp4 --fps 30 --quality delivery --video-frame-format png
```

The staging script reads the takes under `out/desktop-retake/take-01` and `out/gaming-retake/take-01`. It masks the Weather card’s location in the staged clip and writes the prepared clips to this composition’s `assets` directory.

## Mixed desktop takes

The mixed edit uses Take 01 for the opening and the connected Stats and Power sequence. It switches to Take 02 on the taskbar scene and uses the matching daytime readouts for Weather and Prayer. FX, synced lyrics, themes and mascots also come from Take 02. The selected gaming clip stays in its original slot. The generated mix uses the same v1 layout and 53.5-second timeline.

```powershell
node mochidock-v1-retake/stage.mjs --mixed
npx hyperframes lint .work/mochidock-v1-mixed
npx hyperframes render .work/mochidock-v1-mixed -o out/mochidock-v1-mixed.mp4 --fps 30 --quality delivery --video-frame-format png
```

## Mixed edit with the Network page

This 56-second version adds the selected Network dashboard between Drives and Power. The Network take has its IP and MAC fields covered. The Drives list and the Network chart are readable.

```powershell
node mochidock-v1-retake/stage.mjs --mixed --network
npx hyperframes lint .work/mochidock-v1-mixed-network
npx hyperframes render .work/mochidock-v1-mixed-network -o out/mochidock-v1-mixed-network.mp4 --fps 30 --quality delivery --video-frame-format png
```

## Mixed edit with compact lyrics

The 59-second version shows three seconds of synced lyrics and romanization in the compact dock before the expanded Media view. It uses the same Media caption across the two shots. The masked Network and Weather scenes stay in the timeline.

```powershell
node mochidock-v1-retake/stage.mjs --mixed --network --compact-lyrics
npx hyperframes lint .work/mochidock-v1-mixed-lyrics
npx hyperframes render .work/mochidock-v1-mixed-lyrics -o out/mochidock-v1-mixed-lyrics.mp4 --fps 30 --quality delivery --video-frame-format png
```

## Mixed edit with Korean and Japanese lyrics

The 59.9-second version gives each language three seconds in the same fixed compact framing, with a caption label for Korean and Japanese. The expanded Media view follows for 2.1 seconds.

```powershell
node mochidock-v1-retake/stage.mjs --mixed --network --compact-lyrics --both-lyrics
npx hyperframes lint .work/mochidock-v1-mixed-lyrics-both
npx hyperframes render .work/mochidock-v1-mixed-lyrics-both -o out/mochidock-v1-mixed-lyrics-both.mp4 --fps 30 --quality delivery --video-frame-format png
```

## Extract a scene for documentation

From `promo-hyperframes`, select a scene from the 59.9-second promo:

```powershell
node mochidock-v1-retake/extract-media.mjs --list
node mochidock-v1-retake/extract-media.mjs taskbar
node mochidock-v1-retake/extract-media.mjs japanese-lyrics --portrait
```

Each command writes a silent H.264 MP4, a PNG poster, a 30 fps animated WebP for the README, and a 12 fps GIF to `out/docs-media/`. The default crop isolates the app inside the promo’s frame. `--portrait` keeps the full portrait composition and its captions. Extraction uses the rendered promo so the Weather and Network masks carry over.
