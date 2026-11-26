import { spawnSync } from 'node:child_process';
import { copyFile, mkdir, readFile, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

const project = new URL('./', import.meta.url);
const mixed = process.argv.includes('--mixed');
const includeNetwork = process.argv.includes('--network');
const includeCompactLyrics = process.argv.includes('--compact-lyrics');
const bothLyrics = process.argv.includes('--both-lyrics');
if (includeNetwork && !mixed) throw new Error('Use --network with --mixed.');
if (includeCompactLyrics && !includeNetwork) throw new Error('Use --compact-lyrics with --mixed --network.');
if (bothLyrics && !includeCompactLyrics) throw new Error('Use --both-lyrics with --compact-lyrics.');
const mixId = bothLyrics ? 'mochidock-v1-mixed-lyrics-both'
  : includeCompactLyrics ? 'mochidock-v1-mixed-lyrics'
  : includeNetwork ? 'mochidock-v1-mixed-network' : 'mochidock-v1-mixed';
const composition = mixed ? new URL(`../.work/${mixId}/`, project) : project;
const takeOne = new URL('../out/desktop-retake/take-01/', project);
const takeTwo = new URL('../out/desktop-retake/take-02/', project);
const gaming = new URL('../out/gaming-retake/take-01/', project);
const shared = new URL('../assets/', project);
const assets = new URL('assets/', composition);
const html = await readFile(new URL('index.html', project), 'utf8');
const recordings = new Set([...html.matchAll(/src="assets\/(promo-[\w-]+\.mp4)"/g)].map(([, name]) => name));
recordings.delete('promo-taskbar.mp4');
recordings.delete('promo-weather.mp4');

const openingAndStats = new Set(['promo-idle-media.mp4', 'promo-cpu-ready.mp4', 'promo-stats-tour.mp4']);
function recordingFolder(name) {
  if (name === 'promo-gaming-selected.mp4') return gaming;
  return mixed && !openingAndStats.has(name) ? takeTwo : takeOne;
}

await mkdir(assets, { recursive: true });
await Promise.all([
  ...[...recordings].map((name) => copyFile(new URL(name, recordingFolder(name)), new URL(name, assets))),
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

function shiftTimeline(html, cut, duration) {
  return html.replace(/data-start="([\d.]+)"/g, (attribute, time) => {
    const start = Number(time);
    return start < cut ? attribute : `data-start="${Number((start + duration).toFixed(6))}"`;
  });
}

if (mixed) {
  await copyFile(new URL('promo-weather-masked.mp4', takeTwo), new URL('promo-weather.mp4', assets));
} else {
  // Cover the location pin and name while leaving the Change city button and readings visible.
  prepareVideo('promo-weather.mp4', [
    '-i', fileURLToPath(new URL('promo-weather.mp4', takeOne)),
    '-vf', 'drawbox=x=136:y=61:w=152:h=24:color=black:t=fill',
  ]);
}

// Stack the two close-ups to keep the original taskbar labels readable in a portrait frame.
const taskbarTake = mixed ? takeTwo : takeOne;
prepareVideo('promo-taskbar.mp4', [
  '-i', fileURLToPath(new URL('promo-taskbar-01.mp4', taskbarTake)),
  '-i', fileURLToPath(new URL('promo-taskbar-02.mp4', taskbarTake)),
  '-filter_complex', '[0:v]pad=256:62:(ow-iw)/2:4:color=0x171327[a];[1:v]pad=256:62:(ow-iw)/2:4:color=0x171327[b];[a][b]vstack=shortest=1',
]);

if (mixed) {
  let mixedHtml = html
    .replace('data-composition-id="mochidock-v1-retake"', `data-composition-id="${mixId}"`)
    .replace(/<video id="theme"[^>]*>/, (tag) => tag.replace(/data-media-start="[^"]+"/, 'data-media-start="2.4"'))
    .replace(/<video id="mascot"[^>]*>/, (tag) => tag.replace(/data-media-start="[^"]+"/, 'data-media-start="3.5"'));

  if (includeNetwork) {
    await copyFile(
      new URL('../out/network-retake/take-01/promo-network-masked.mp4', project),
      new URL('promo-network-masked.mp4', assets),
    );
    const networkStart = 12.6;
    const networkDuration = 2.5;
    const networkWindow = `          <div class="window" data-crop="26 0 778 436" data-source="880 580">
            <video id="network" class="clip" src="assets/promo-network-masked.mp4" data-start="${networkStart}" data-duration="${networkDuration}" data-track-index="1" data-media-start="1" muted playsinline></video>
          </div>\n`;

    mixedHtml = shiftTimeline(mixedHtml, networkStart, networkDuration)
      .replace('data-duration="53.5"', 'data-duration="56"')
      .replace(/^ {10}<div class="window"[^>]*>\r?\n {12}<video id="power"/m, (window) => networkWindow + window)
      .replace(/(<section id="caption-power"[^>]*data-duration=")4\.7"/, (_match, prefix) => `${prefix}7.2"`)
      .replace('<span class="label">Disk &amp; power</span><span class="heading">Track your<br><em>power use.</em></span>',
        '<span class="label">Disk, network &amp; power</span><span class="heading">Track your<br><em>PC activity.</em></span>')
      .replace('Estimated watts over time.', 'Disk activity, network speeds, and estimated watts.');
  }

  if (includeCompactLyrics) {
    await copyFile(
      new URL('../out/compact-lyrics-retake/take-01/promo-compact-lyrics.mp4', project),
      new URL('promo-compact-lyrics.mp4', assets),
    );
    const compactStart = 38.9;
    const compactDuration = 3;
    const japaneseStart = 41.9;
    const expandedStart = bothLyrics ? 44.9 : japaneseStart;
    const expandedDuration = bothLyrics ? 2.1 : 4.2;
    const captionDuration = bothLyrics ? 8.1 : 7.2;
    const compactWindow = (id, name, start, offset) => `          <div class="window" data-crop="154 0 524 84" data-source="880 216">
            <video id="${id}" class="clip" src="assets/${name}" data-start="${start}" data-duration="${compactDuration}" data-track-index="1" data-media-start="${offset}" muted playsinline></video>
          </div>\n`;
    let compactShots = compactWindow('compact-lyrics', 'promo-compact-lyrics.mp4', compactStart, 5);

    if (bothLyrics) {
      const japaneseAsset = 'promo-compact-lyrics-ja.mp4';
      await copyFile(
        new URL('../out/compact-lyrics-retake/take-02/promo-compact-lyrics.mp4', project),
        new URL(japaneseAsset, assets),
      );
      compactShots += compactWindow('compact-lyrics-ja', japaneseAsset, japaneseStart, 2.2);
      const labelCues = JSON.stringify([[japaneseStart, 'Japanese lyrics'], [expandedStart, 'Expanded Media']]);
      mixedHtml = mixedHtml.replace('<span class="label">Media</span>',
        `<span class="label" data-label-cues='${labelCues}'>Korean lyrics</span>`);
    }

    mixedHtml = shiftTimeline(mixedHtml, 43.1, bothLyrics ? 3.9 : compactDuration)
      .replace('data-duration="56"', `data-duration="${bothLyrics ? 59.9 : 59}"`)
      .replace(/<video id="lyrics"[^>]*>/, (tag) => tag
        .replace(/data-start="[^"]*"/, `data-start="${expandedStart}"`)
        .replace(/data-duration="[^"]*"/, `data-duration="${expandedDuration}"`))
      .replace(/^ {10}<div class="window"[^>]*>\r?\n {12}<video id="lyrics"/m, (window) => compactShots + window)
      .replace(/(<section id="caption-lyrics" class="clip caption" data-start="38\.9") data-duration="4\.2"/,
        (_match, section) => `${section} data-duration="${captionDuration}"`);
  }

  await Promise.all([
    writeFile(new URL('index.html', composition), mixedHtml),
    copyFile(new URL('style.css', project), new URL('style.css', composition)),
    copyFile(new URL('timeline.js', project), new URL('timeline.js', composition)),
  ]);
}
