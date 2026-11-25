# MochiDock v2

A 44-second silent portrait composition. The seven feature scenes run from 5 to 6.5 seconds, followed by a 3.5-second end card. The overview and detail views share the same clip timing when they show the same recording. The opening and the two paired features use separate recordings within one scene.

Run these commands from `promo-hyperframes` using its installed HyperFrames CLI:

```powershell
node mochidock-v2/stage.mjs --preview
npx hyperframes lint mochidock-v2
npx hyperframes render .work/mochidock-v2-preview -o out/mochidock-v2-preview.mp4 --fps 30 --quality looks --video-frame-format png
npx hyperframes render mochidock-v2 -o out/mochidock-v2.mp4 --fps 30 --quality delivery --video-frame-format png
```

The staging script copies from the parent project's `assets` folder into this composition and its 540 × 960 preview. The 1080 × 1920 source uses the same footage and caption timing. The original project and recordings are read-only inputs to this workflow.
