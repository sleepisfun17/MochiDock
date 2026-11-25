import React from 'react';
import { Composition } from 'remotion';
import { MochiDockShort, TOTAL_FRAMES } from './MochiDockShort';

export const Root: React.FC = () => (
  <Composition
    id="MochiDockShort"
    component={MochiDockShort}
    durationInFrames={TOTAL_FRAMES}
    fps={30}
    width={1080}
    height={1920}
  />
);
