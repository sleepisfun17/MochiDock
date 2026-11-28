import { spawnSync } from 'node:child_process';
import { mkdir } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

const out = new URL('../out/', import.meta.url);
const source = fileURLToPath(new URL('mochidock-v1-mixed-lyrics-both.mp4', out));
const media = new URL('docs-media/', out);

// Times in the 59.9-second promo. The last value selects the still and website poster.
const scenes = {
  hover: [0, 4.8, 3.4],
  'live-stats': [4.8, 10.7, 6.4],
  drives: [10.7, 12.6, 11.5],
  network: [12.6, 15.1, 13.5],
  power: [15.1, 17.9, 16.4],
  taskbar: [17.9, 22.4, 20],
  gaming: [22.4, 26.5, 24],
  'fx-rates': [26.5, 30.7, 28.5],
  weather: [30.7, 34.8, 32.5],
  prayer: [34.8, 38.9, 37],
  'korean-lyrics': [38.9, 41.9, 40],
  'japanese-lyrics': [41.9, 44.9, 43.6],
  media: [44.9, 47, 45.7],
  themes: [47, 51.8, 50.4],
  mascots: [51.8, 56.6, 55.5],
};

function ffmpeg(args) {
  const result = spawnSync('ffmpeg', ['-v', 'error', '-xerror', '-y', ...args], { stdio: 'inherit' });
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error('FFmpeg could not export the selected scene.');
}

const [scene, ...options] = process.argv.slice(2);
if (!scene || scene === '--list') {
  console.table(Object.entries(scenes).map(([name, [start, end, still]]) => ({ scene: name, start, end, still })));
} else {
  if (!Object.hasOwn(scenes, scene)) throw new Error(`Unknown scene ${scene}. Run with --list for the available scenes.`);
  if (options.some((option) => option !== '--portrait')) throw new Error('The available option is --portrait.');

  const portrait = options.includes('--portrait');
  const crop = portrait ? 'null' : 'crop=960:668:60:386';
  const [start, end, still] = scenes[scene];
  const duration = (end - start).toFixed(3);
  const base = `mochidock-${scene}${portrait ? '-portrait' : ''}`;
  const output = (extension) => fileURLToPath(new URL(`${base}.${extension}`, media));

  await mkdir(media, { recursive: true });

  ffmpeg(['-ss', String(start), '-i', source, '-t', duration, '-vf', crop,
    '-an', '-c:v', 'libx264', '-crf', '19', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', output('mp4')]);
  ffmpeg(['-ss', String(still), '-i', source, '-vf', crop, '-frames:v', '1', output('png')]);
  ffmpeg(['-i', output('mp4'), '-an', '-c:v', 'libwebp_anim',
    '-quality', '88', '-compression_level', '5', '-loop', '0', output('webp')]);
  ffmpeg(['-i', output('mp4'), '-vf',
    `fps=12,scale=${portrait ? 486 : 660}:-2:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff:max_colors=128[p];[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle`,
    '-loop', '0', output('gif')]);

  console.log(`Exported ${base}.mp4, .png, .webp, and .gif to ${fileURLToPath(media)}`);
}
