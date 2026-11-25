import { copyFile, cp, mkdir, readFile, writeFile } from 'node:fs/promises';

const project = new URL('./', import.meta.url);
const original = new URL('../promo/', project);
const assets = new URL('assets/', project);
const html = await readFile(new URL('index.html', project), 'utf8');

await mkdir(assets, { recursive: true });
await mkdir(new URL('out/', project), { recursive: true });

const clips = [...new Set([...html.matchAll(/src="assets\/(promo-[\w-]+\.mp4)"/g)].map((match) => match[1]))];
const inputs = [
  ...clips.map((clip) => [new URL(`clips/${clip}`, original), clip]),
  [new URL('public/mochi-icon.png', original), 'mochi-icon.png'],
  [new URL('public/fonts/Quicksand-Bold.ttf', original), 'Quicksand-Bold.ttf'],
  [new URL('public/fonts/Quicksand-SemiBold.ttf', original), 'Quicksand-SemiBold.ttf'],
  [new URL('node_modules/gsap/dist/gsap.min.js', project), 'gsap.min.js'],
];

await Promise.all(inputs.map(([source, name]) => copyFile(source, new URL(name, assets))));

if (process.argv.includes('--draft')) {
  const draftProject = new URL('.work/draft/', project);
  await mkdir(draftProject, { recursive: true });
  const draft = html.replace(
    'data-width="1080" data-height="1920"',
    'data-width="540" data-height="960" style="width: 540px; height: 960px; --canvas-scale: .5"',
  );
  await Promise.all([
    writeFile(new URL('index.html', draftProject), draft),
    copyFile(new URL('style.css', project), new URL('style.css', draftProject)),
    copyFile(new URL('timeline.js', project), new URL('timeline.js', draftProject)),
    cp(assets, new URL('assets/', draftProject), { recursive: true }),
  ]);
}
