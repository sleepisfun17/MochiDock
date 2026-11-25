# MochiDock v1 refined

This 49-second silent portrait cut uses the original HyperFrames composition's stylesheet, scene order, caption transitions and end card. Each of its ten feature scenes lasts 1.7 seconds longer. The opening uses v1's wide framing from its first frame, and all footage plays at its recorded speed.

From `promo-hyperframes`, use the existing shared recordings and installed HyperFrames CLI:

```powershell
node mochidock-v1-refined/stage.mjs
npx hyperframes lint mochidock-v1-refined
npx hyperframes render mochidock-v1-refined -o out/mochidock-v1-refined.mp4 --fps 30 --quality delivery --video-frame-format png
```
