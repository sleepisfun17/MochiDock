import React from 'react';
import { Composition, staticFile } from 'remotion';
import { MochiDockShort, buildBeats, totalFrames } from './MochiDockShort';

export const Root: React.FC = () => (
  <Composition
    id="MochiDockShort"
    component={MochiDockShort}
    durationInFrames={totalFrames(buildBeats(null))}
    fps={30}
    width={1080}
    height={1920}
    defaultProps={{ available: null as string[] | null }}
    calculateMetadata={async () => {
      const available: string[] | null = await fetch(staticFile('clips/manifest.json')).then((r) => r.json()).catch(() => null);
      return { durationInFrames: totalFrames(buildBeats(available)), props: { available } };
    }}
  />
);
