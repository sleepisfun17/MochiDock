import React from 'react';
import { Composition, staticFile } from 'remotion';
import { MochiDockShort, PROMO_FPS, PROMO_HEIGHT, PROMO_WIDTH, getTimeline } from './MochiDockShort';
import { MochiDockV1Refined, getV1RefinedDuration } from './MochiDockV1Refined';
import { MochiDockV2, getV2Timeline } from './MochiDockV2';

export const Root: React.FC = () => (
  <>
    <Composition
      id="MochiDockShort"
      component={MochiDockShort}
      durationInFrames={getTimeline(null).duration}
      fps={PROMO_FPS}
      width={PROMO_WIDTH}
      height={PROMO_HEIGHT}
      defaultProps={{ available: null as string[] | null }}
      calculateMetadata={async () => {
        const available: string[] | null = await fetch(staticFile('clips/manifest.json')).then((r) => r.json()).catch(() => null);
        return { durationInFrames: getTimeline(available).duration, props: { available } };
      }}
    />
    <Composition
      id="MochiDockV1Refined"
      component={MochiDockV1Refined}
      durationInFrames={getV1RefinedDuration(null)}
      fps={PROMO_FPS}
      width={PROMO_WIDTH}
      height={PROMO_HEIGHT}
      defaultProps={{ available: null as string[] | null }}
      calculateMetadata={async () => {
        const available: string[] | null = await fetch(staticFile('clips/manifest.json')).then((r) => r.json()).catch(() => null);
        return { durationInFrames: getV1RefinedDuration(available), props: { available } };
      }}
    />
    <Composition
      id="MochiDockV2"
      component={MochiDockV2}
      durationInFrames={getV2Timeline(null).duration}
      fps={PROMO_FPS}
      width={PROMO_WIDTH}
      height={PROMO_HEIGHT}
      defaultProps={{ available: null as string[] | null }}
      calculateMetadata={async () => {
        const available: string[] | null = await fetch(staticFile('clips/manifest.json')).then((r) => r.json()).catch(() => null);
        return { durationInFrames: getV2Timeline(available).duration, props: { available } };
      }}
    />
  </>
);
