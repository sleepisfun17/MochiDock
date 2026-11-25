import { copyFile, mkdir, readFile } from 'node:fs/promises';

const project = new URL('./', import.meta.url);
const sharedAssets = new URL('../assets/', project);
const localAssets = new URL('assets/', project);
const html = await readFile(new URL('index.html', project), 'utf8');
const media = [...html.matchAll(/src="assets\/([^"/]+)"/g)].map(([, name]) => name);
const assetNames = new Set([...media, 'mochi-icon.png', 'Quicksand-Bold.ttf', 'Quicksand-SemiBold.ttf']);

await mkdir(localAssets, { recursive: true });
await Promise.all([...assetNames].map((name) => copyFile(new URL(name, sharedAssets), new URL(name, localAssets))));
