# MochiDock HyperFrames promo

`index.html`, `style.css` and `timeline.js` define the 32-second silent portrait composition. HyperFrames handles clip timing and media playback. The GSAP timeline animates the opening crop, captions, progress bar and end card. The edit uses read-only inputs from `../promo/clips` and `../promo/public`.

From `promo-hyperframes`, with Node.js 22+, npm and FFmpeg available:

```powershell
npm ci
npm run assets
npm run lint
npm run studio
```

HyperFrames uses its managed Chrome by default. On a machine with an existing compatible Chrome or headless shell, set `HYPERFRAMES_BROWSER_PATH` to its executable. Set `HYPERFRAMES_CACHE_DIR` to a writable directory if the default cache location is unavailable.

```powershell
npm run draft
python review/storyboard.py
```

The draft command renders the same composition at 540 × 960, 30 fps. The storyboard script requires Pillow and makes a matched-time storyboard and a side-by-side player at `out/compare.html`. The player reads the approved `../promo/out/mochidock-short.mp4` in place.

After approval, run `npm run render` to produce `out/mochidock-hyperframes.mp4` at 1080 × 1920, 30 fps. Tool and font credits are in [THIRD-PARTY-NOTICES.txt](../THIRD-PARTY-NOTICES.txt).
