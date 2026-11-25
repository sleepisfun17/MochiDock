import React from 'react';
import { AbsoluteFill, Easing, Freeze, Img, OffthreadVideo, Sequence, interpolate, spring, staticFile, useCurrentFrame } from 'remotion';
import { PROMO_FPS } from './MochiDockShort';

const frames = (seconds: number) => Math.round(seconds * PROMO_FPS);
const OUTRO = frames(3.5);
const BACKGROUND = '#0A0A0A';
const INK = '#F5F6F8';
const MUTED = '#BCC0C9';
const FRAME_LEFT = 60;
const FRAME_WIDTH = 800;
const INSET = 10;
const USABLE_WIDTH = FRAME_WIDTH - 2 * INSET;
const BORDER = '1px solid rgba(255,255,255,0.12)';

type Crop = readonly [left: number, top: number, width: number, height: number];
type Point = readonly [x: number, y: number];
type Shot = {
  clip: string;
  crop: Crop;
  focus: Point;
  from: number;
  seconds: number;
  rate?: number;
  holdAfter?: number;
  blur?: Crop;
  restingCrop?: Crop;
};
type Feature = {
  id: string;
  headline: string;
  explanation: string;
  shots: Shot[];
};
export type MochiDockV2Props = { available: string[] | null };

// These are source-pixel rectangles. Desktop and gaming captures have different dimensions.
const MEDIA: Crop = [16, 0, 798, 222];
const HARDWARE: Crop = [16, 0, 798, 438];
const SETTINGS: Crop = [16, 0, 798, 506];
const STATS_TOUR = 'promo-stats-tour';

export const getV2Timeline = (available: string[] | null) => {
  const has = (clip: string) => available?.includes(clip) ?? false;
  const mediaOpening: Shot = has('promo-idle-media')
    ? {
      clip: 'promo-idle-media', crop: MEDIA, focus: [415, 105], from: 1.03, seconds: 3.1,
      restingCrop: [211, 0, 408, 96],
    }
    : { clip: 'promo-hover', crop: MEDIA, focus: [415, 105], from: 1.2, seconds: 3.1 };
  const cpu: Shot = {
    clip: has('promo-cpu-ready') ? 'promo-cpu-ready' : 'promo-stats',
    crop: HARDWARE, focus: [180, 159], from: 0.65, seconds: has(STATS_TOUR) ? 2.1 : 5.7, rate: 0.75,
  };
  const stats = [cpu];
  if (has(STATS_TOUR)) {
    stats.push(
      { clip: STATS_TOUR, crop: HARDWARE, focus: [161, 161], from: 4.17, seconds: 1.8, rate: 0.75 },
      { clip: STATS_TOUR, crop: HARDWARE, focus: [162, 160], from: 6.87, seconds: 1.8, rate: 0.75 },
    );
  }

  const features: Feature[] = [
    {
      id: 'media',
      headline: 'Media Controls on Hover',
      explanation: 'Control playback and read synced lyrics without switching windows.',
      shots: [mediaOpening, { clip: 'promo-media', crop: MEDIA, focus: [630, 119], from: 1.35, seconds: 2.7, rate: 0.85 }],
    },
    {
      id: 'stats',
      headline: 'Live CPU, GPU and RAM',
      explanation: 'See processor, graphics and memory usage update in one place.',
      shots: stats,
    },
  ];
  if (has(STATS_TOUR)) {
    features.push({
      id: 'power',
      headline: 'Power and Activity History',
      explanation: 'Watch disk and network activity and see estimated PC wattage.',
      shots: [
        {
          clip: STATS_TOUR, crop: HARDWARE, focus: [448, 204], from: 9.65, seconds: 1.8, rate: 0.75,
          blur: [54, 155, 109, 183],
        },
        { clip: STATS_TOUR, crop: HARDWARE, focus: [553, 221], from: 12.15, seconds: 3.2, rate: 0.7 },
      ],
    });
  }
  if (has('promo-gaming')) {
    features.push({
      id: 'gaming',
      headline: 'Stats While You Play',
      explanation: 'See CPU and GPU load without leaving a supported game.',
      shots: [{ clip: 'promo-gaming', crop: [252, 0, 596, 299], focus: [550, 28], from: 2.2, seconds: 5, rate: 0.75 }],
    });
  }
  features.push(
    {
      id: 'weather',
      headline: 'Hourly Weather Forecast',
      explanation: 'Check the next few hours before you head out.',
      shots: [{ clip: 'promo-weather', crop: [16, 0, 798, 350], focus: [415, 196], from: 2.7, seconds: 5, rate: 0.7 }],
    },
    {
      id: 'prayer',
      headline: 'Prayer Times and Qibla',
      explanation: 'Find your next prayer time and see the qibla direction.',
      shots: [{ clip: 'promo-prayer', crop: [16, 0, 798, 344], focus: [592, 135], from: 0.6, seconds: 5, rate: 0.8 }],
    },
    {
      id: 'fx',
      headline: 'Exchange Rate History',
      explanation: 'Compare currency rates and track recent changes on a chart.',
      shots: [{ clip: 'promo-fx', crop: [16, 0, 798, 457], focus: [490, 282], from: 2.1, seconds: 5, rate: 0.75, holdAfter: 3 }],
    },
    {
      id: 'appearance',
      headline: 'Themes and Mascots',
      explanation: 'Switch themes and choose a mascot that suits your desktop.',
      shots: [
        { clip: 'promo-theme', crop: SETTINGS, focus: [460, 160], from: 1.75, seconds: 2.5, rate: 0.85 },
        { clip: 'promo-mascot', crop: SETTINGS, focus: [115, 92], from: 4.6, seconds: 2.5, rate: 0.9 },
      ],
    },
  );

  let next = 0;
  const scenes = features.map((feature) => {
    let offset = 0;
    const shots = feature.shots.map((shot) => {
      const duration = frames(shot.seconds);
      const timedShot = { shot, offset, duration };
      offset += duration;
      return timedShot;
    });
    const scene = { feature, shots, duration: offset, start: next };
    next += offset;
    return scene;
  });
  return { scenes, outroStart: next, duration: next + OUTRO };
};

const clampUnit = (value: number) => Math.max(0, Math.min(1, value));

const DesktopWindow: React.FC<{ shot: Shot; offset: number }> = ({ shot, offset }) => {
  const frame = useCurrentFrame();
  const { restingCrop } = shot;
  const [left, top, width, height] = restingCrop
    ? shot.crop.map((value, i) => interpolate(frame, [frames(1.08), frames(1.72)], [restingCrop[i], value], {
      easing: Easing.inOut(Easing.sin), extrapolateLeft: 'clamp', extrapolateRight: 'clamp',
    }))
    : shot.crop;
  const [sourceWidth, sourceHeight] = shot.clip === 'promo-gaming' ? [1100, 420] : [880, 580];
  const viewportHeight = USABLE_WIDTH * shot.crop[3] / shot.crop[2];
  const zoom = 1 + 0.25 * clampUnit(spring({
    frame: frame + offset, fps: PROMO_FPS, durationInFrames: frames(4),
    config: { damping: 22, stiffness: 65, overshootClamping: true },
  }));
  // Reserve room for the full zoom and anchor it at the active point, keeping the entire crop in view.
  const scale = Math.min(USABLE_WIDTH / width, viewportHeight / height) * zoom / 1.25;
  const pictureWidth = width * scale;
  const pictureHeight = height * scale;
  const x = INSET + (USABLE_WIDTH - pictureWidth) * clampUnit((shot.focus[0] - left) / width);
  const y = INSET + (viewportHeight - pictureHeight) * clampUnit((shot.focus[1] - top) / height);
  const hold = shot.holdAfter === undefined ? undefined : frames(shot.holdAfter);

  return (
    <div style={{
      position: 'relative', width: FRAME_WIDTH, height: viewportHeight + 2 * INSET, boxSizing: 'border-box',
      background: 'linear-gradient(145deg, #191B20, #111215 68%)',
      border: BORDER, borderRadius: 22, boxShadow: '0 28px 76px rgba(0,0,0,0.58)',
    }}>
      <div style={{
        position: 'absolute', left: x, top: y, width: pictureWidth, height: pictureHeight,
        borderRadius: '3px 3px 12px 12px', overflow: 'hidden',
      }}>
        <Freeze frame={hold ?? 0} active={hold !== undefined && frame >= hold}>
          <OffthreadVideo
            src={staticFile(`clips/${shot.clip}.mp4`)}
            trimBefore={frames(shot.from)}
            playbackRate={shot.rate ?? 1}
            muted
            style={{ position: 'absolute', left: -left * scale, top: -top * scale, width: sourceWidth * scale, height: sourceHeight * scale }}
          />
        </Freeze>
        {shot.blur && (
          <div style={{
            position: 'absolute', left: (shot.blur[0] - left) * scale, top: (shot.blur[1] - top) * scale,
            width: shot.blur[2] * scale, height: shot.blur[3] * scale, backdropFilter: 'blur(10px)',
          }} />
        )}
      </div>
    </div>
  );
};

const FeatureScene: React.FC<{ scene: ReturnType<typeof getV2Timeline>['scenes'][number] }> = ({ scene }) => {
  const frame = useCurrentFrame();
  const enter = interpolate(frame, [0, 8], [0, 1], { easing: Easing.out(Easing.cubic), extrapolateRight: 'clamp' });
  return (
    <div style={{ position: 'absolute', left: FRAME_LEFT, top: 450, width: FRAME_WIDTH }}>
      <div style={{ opacity: 0.85 + 0.15 * enter, transform: `translateY(${6 * (1 - enter)}px)` }}>
        <div style={{ color: INK, fontSize: 60, lineHeight: 1.08, fontWeight: 700, letterSpacing: -1.9, whiteSpace: 'nowrap' }}>
          {scene.feature.headline}
        </div>
        <div style={{ color: MUTED, marginTop: 16, fontSize: 27, lineHeight: 1.3, whiteSpace: 'nowrap' }}>
          {scene.feature.explanation}
        </div>
      </div>
      <div style={{ position: 'relative', marginTop: 52 }}>
        {scene.shots.map(({ shot, offset, duration }) => (
          <Sequence key={offset} layout="none" from={offset} durationInFrames={duration}>
            <DesktopWindow shot={shot} offset={offset} />
          </Sequence>
        ))}
      </div>
    </div>
  );
};

const Outro: React.FC = () => (
  <div style={{ position: 'absolute', left: FRAME_LEFT, top: 503, width: FRAME_WIDTH, textAlign: 'center' }}>
    <Img src={staticFile('mochi-icon.png')} style={{ width: 162, height: 162 }} />
    <div style={{ marginTop: 16, fontSize: 100, lineHeight: 1.06, fontWeight: 700, letterSpacing: -4, color: INK }}>
      MochiDock
    </div>
    <div style={{ marginTop: 17, fontSize: 36, color: MUTED }}>A desktop dock for Windows.</div>
    <div style={{ margin: '42px auto 0', width: 'max-content', padding: '14px 30px', border: BORDER, borderRadius: 10, background: '#141518', color: INK, fontSize: 34 }}>
      github.com/sleepisfun17/MochiDock
    </div>
  </div>
);

export const MochiDockV2: React.FC<MochiDockV2Props> = ({ available }) => {
  const { scenes, outroStart } = getV2Timeline(available);
  return (
    <AbsoluteFill style={{
      backgroundColor: BACKGROUND,
      backgroundImage: 'radial-gradient(690px 570px at 42% 47%, rgba(145,157,181,0.095), transparent 78%)',
      fontFamily: '"Segoe UI", Arial, sans-serif',
    }}>
      {scenes.map((scene) => (
        <Sequence key={scene.feature.id} name={scene.feature.headline} from={scene.start} durationInFrames={scene.duration}>
          <FeatureScene scene={scene} />
        </Sequence>
      ))}
      <Sequence name="MochiDock" from={outroStart} durationInFrames={OUTRO}>
        <Outro />
      </Sequence>
    </AbsoluteFill>
  );
};
