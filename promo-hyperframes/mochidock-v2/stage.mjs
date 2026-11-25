import { copyFile, cp, mkdir, readFile, writeFile } from 'node:fs/promises';

const project = new URL('./', import.meta.url);
const sharedAssets = new URL('../assets/', project);
const localAssets = new URL('assets/', project);
const html = await readFile(new URL('index.html', project), 'utf8');
const assetNames = [...new Set([...html.matchAll(/src="assets\/([^"/]+)"/g)].map(([, name]) => name))];

await mkdir(localAssets, { recursive: true });
await Promise.all(assetNames.map((name) => copyFile(new URL(name, sharedAssets), new URL(name, localAssets))));

if (process.argv.includes('--preview')) {
  const preview = new URL('../.work/mochidock-v2-preview/', project);
  const draft = html.replace(
    'data-width="1080" data-height="1920"',
    'data-width="540" data-height="960" style="width:540px;height:960px;--preview-scale:.5"',
  );
  await mkdir(preview, { recursive: true });
  await Promise.all([
    writeFile(new URL('index.html', preview), draft),
    copyFile(new URL('style.css', project), new URL('style.css', preview)),
    copyFile(new URL('timeline.js', project), new URL('timeline.js', preview)),
    cp(localAssets, new URL('assets/', preview), { recursive: true }),
  ]);
}
