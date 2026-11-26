# MochiDock v1 retake

This 53.5-second silent portrait promo uses the refined v1 design and pacing with the new desktop and gaming recordings. A 4.5-second taskbar scene shows both captured strips in one fixed frame. The gaming scene uses the selected gameplay clip.

From `promo-hyperframes`, with the shared assets, HyperFrames and FFmpeg available:

```powershell
node mochidock-v1-retake/stage.mjs
npx hyperframes lint mochidock-v1-retake
npx hyperframes render mochidock-v1-retake -o out/mochidock-v1-retake.mp4 --fps 30 --quality delivery --video-frame-format png
```

The staging script reads the takes under `out/desktop-retake/take-01` and `out/gaming-retake/take-01`. It masks the Weather card’s location in the staged clip and writes the prepared clips to this composition’s `assets` directory.
