import { spawnSync } from 'node:child_process';
import { copyFile, mkdir, readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

const project = new URL('./', import.meta.url);
const desktop = new URL('../out/desktop-retake/take-01/', project);
const gaming = new URL('../out/gaming-retake/take-01/', project);
const shared = new URL('../assets/', project);
const assets = new URL('assets/', project);
const html = await readFile(new URL('index.html', project), 'utf8');
const recordings = new Set([...html.matchAll(/src="assets\/(promo-[\w-]+\.mp4)"/g)].map(([, name]) => name));
recordings.delete('promo-taskbar.mp4');
recordings.delete('promo-weather.mp4');

await mkdir(assets, { recursive: true });
await Promise.all([
  ...[...recordings].map((name) => copyFile(new URL(name, name === 'promo-gaming-selected.mp4' ? gaming : desktop), new URL(name, assets))),
  ...['gsap.min.js', 'mochi-icon.png', 'Quicksand-Bold.ttf', 'Quicksand-SemiBold.ttf']
    .map((name) => copyFile(new URL(name, shared), new URL(name, assets))),
]);

function prepareVideo(name, inputsAndFilters) {
  const result = spawnSync('ffmpeg', [
    '-v', 'error', '-y', ...inputsAndFilters,
    '-an', '-c:v', 'libx264', '-crf', '12', '-pix_fmt', 'yuv420p', '-movflags', '+faststart',
    fileURLToPath(new URL(name, assets)),
  ], { stdio: 'inherit' });
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(`Could not prepare ${name}.`);
}

// Cover the location pin and name while leaving the Change city button and readings visible.
prepareVideo('promo-weather.mp4', [
  '-i', fileURLToPath(new URL('promo-weather.mp4', desktop)),
  '-vf', 'drawbox=x=136:y=61:w=152:h=24:color=black:t=fill',
]);

// Stack the two close-ups to keep the original taskbar labels readable in a portrait frame.
prepareVideo('promo-taskbar.mp4', [
  '-i', fileURLToPath(new URL('promo-taskbar-01.mp4', desktop)),
  '-i', fileURLToPath(new URL('promo-taskbar-02.mp4', desktop)),
  '-filter_complex', '[0:v]pad=256:62:12:4:color=0x171327[a];[1:v]pad=256:62:12:4:color=0x171327[b];[a][b]vstack=shortest=1',
]);
