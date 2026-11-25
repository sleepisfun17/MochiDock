import React from 'react';
import {
  AbsoluteFill, Easing, Img, OffthreadVideo, Sequence, interpolate, staticFile, useCurrentFrame,
} from 'remotion';

// Colours from MochiDock's Matcha theme (ThemeService.cs) and the dark Default theme.
const CREAM = '#EFE6CF';
const SAGE = '#DDE0C4';
const INK = '#283322';
const ACCENT = '#335226';
const MUTED = '#53604A';

const fontFaces = `
@font-face { font-family: 'Quicksand'; font-weight: 700; src: url('${staticFile('fonts/Quicksand-Bold.ttf')}') format('truetype'); }
@font-face { font-family: 'Quicksand'; font-weight: 600; src: url('${staticFile('fonts/Quicksand-SemiBold.ttf')}') format('truetype'); }
`;

export const PROMO_FPS = 30;
export const PROMO_WIDTH = 1080;
export const PROMO_HEIGHT = 1920;
const frames = (seconds: number) => Math.round(seconds * PROMO_FPS);
const CUT = 6;
const OUTRO = frames(3.3);

type Rect = readonly [x: number, y: number, width: number, height: number];
type Shot = {
  clip: string;
  crop: Rect;
  focus?: Rect;
  blur?: Rect;
  from: number;
  seconds: number;
  rate?: number;
  reframe?: { crop: Rect; start: number; end: number };
};
type Beat = {
  kicker: string;
  title: string;
  shots: [Shot, ...Shot[]];
  optional?: boolean;
};

// crop and focus are in source pixels. Desktop clips are 880 x 580 and the dock spans x = 28–801.
// Keep both edges in the crop. The push-in scales the whole crop around the point of interest.
const STATS_CROP: Rect = [24, 0, 782, 436];
const WIDE_STATS_CROP: Rect = [16, 0, 798, 436];
const HOVER_CROP: Rect = [24, 0, 782, 216];
const TOUR = 'promo-stats-tour';
const IDLE_HOVER = 'promo-idle-hover';
const IDLE_MEDIA = 'promo-idle-media';
const CPU_READY = 'promo-cpu-ready';
const IDLE_STATS: Shot = {
  clip: IDLE_HOVER, crop: WIDE_STATS_CROP, focus: [43, 56, 309, 156], from: 1.03, seconds: 3.7,
  reframe: { crop: [210, 0, 410, 93], start: 1.1, end: 1.73 },
};
const CPU_STATS: Shot = { clip: 'promo-stats', crop: STATS_CROP, focus: [43, 56, 309, 156], from: 0.4, seconds: 3.7 };
const MEDIA_OPENING: Shot = {
  clip: IDLE_MEDIA, crop: [16, 0, 798, 226], from: 1.03, seconds: 3.3,
  reframe: { crop: [210, 0, 410, 93], start: 1.1, end: 1.73 },
};

const openingBeats = (available: string[] | null): Beat[] => {
  const hasMediaOpening = available?.includes(IDLE_MEDIA);
  const hasTour = available?.includes(TOUR);
  const cpu: Shot = available?.includes(CPU_READY) ? { ...CPU_STATS, clip: CPU_READY, crop: WIDE_STATS_CROP } : CPU_STATS;
  const beats: Beat[] = [];
  if (hasMediaOpening) {
    beats.push({ kicker: 'MochiDock for Windows', title: 'Open your dock\nwith a hover', shots: [MEDIA_OPENING] });
  }
  const stats: Beat = hasMediaOpening
    ? { kicker: 'MochiDock Stats', title: 'See CPU, GPU\n& RAM usage', shots: [{ ...cpu, seconds: hasTour ? 1.6 : 3.2 }] }
    : {
      kicker: 'MochiDock', title: 'A Windows dock\nfor live PC stats',
      shots: [available?.includes(IDLE_HOVER) ? IDLE_STATS : cpu],
    };
  beats.push(stats);
  if (!hasTour) {
    if (!hasMediaOpening) beats.push({
      kicker: 'Live stats', title: 'See CPU, GPU\n& RAM usage',
      shots: [{ ...CPU_STATS, from: 2.6, seconds: 3.2 }],
    });
    return beats;
  }

  stats.shots.push(
    { clip: TOUR, crop: STATS_CROP, focus: [43, 114, 230, 98], from: 4.15, seconds: 1.3 },
    { clip: TOUR, crop: STATS_CROP, focus: [43, 114, 230, 98], from: 6.85, seconds: 1.3 },
  );
  beats.push({
    kicker: 'Disk, network & power', title: 'Track your PC’s\npower use',
    shots: [
      {
        clip: TOUR, crop: STATS_CROP, focus: [351, 56, 207, 58], blur: [54, 155, 109, 183],
        from: 9.6, seconds: 1.2,
      },
      { clip: TOUR, crop: STATS_CROP, focus: [43, 173, 735, 114], from: 12.15, seconds: 2.1 },
    ],
  });
  return beats;
};

const ALL_BEATS: Beat[] = [
  {
    kicker: 'Gaming mode', title: 'Track PC load\nwhile you play', optional: true,
    shots: [{ clip: 'promo-gaming', crop: [244, 0, 612, 265], focus: [320, 2, 460, 39], from: 2, seconds: 2.7 }],
  },
  {
    kicker: 'FX rates', title: 'See how exchange\nrates move',
    shots: [{ clip: 'promo-fx', crop: [24, 0, 782, 454], focus: [44, 176, 739, 226], from: 2, seconds: 2.7 }],
  },
  {
    kicker: 'Weather', title: 'Plan around\nthe next few hours',
    shots: [{ clip: 'promo-weather', crop: [24, 0, 782, 347], focus: [44, 145, 742, 110], from: 2.8, seconds: 2.6 }],
  },
  {
    kicker: 'Prayer times', title: 'Know when\nit’s time to pray',
    shots: [{ clip: 'promo-prayer', crop: [24, 0, 782, 342], focus: [343, 99, 440, 115], from: 0.8, seconds: 2.6 }],
  },
  {
    kicker: 'Media', title: 'Follow the lyrics\nas they play',
    shots: [{ clip: 'promo-media', crop: HOVER_CROP, focus: [483, 49, 298, 150], from: 1.2, seconds: 2.7 }],
  },
  {
    kicker: 'Themes', title: 'Make your dock\nmatch your desktop',
    shots: [{ clip: 'promo-theme', crop: [24, 0, 782, 503], focus: [49, 143, 534, 40], from: 1.45, seconds: 3.3, rate: 1.9 }],
  },
  {
    kicker: 'Mascots', title: 'Pick a mascot\nfor your dock',
    shots: [{ clip: 'promo-mascot', crop: [24, 0, 782, 503], focus: [40, 4, 58, 46], from: 1.5, seconds: 3.3, rate: 1.9 }],
  },
];

// The clip manifest is written by npm run clips. The extra opening footage and Gaming clips are optional.
export const getTimeline = (available: string[] | null) => {
  let next = 0;
  const scenes = [...openingBeats(available), ...ALL_BEATS]
    .filter((beat) => !beat.optional || available?.includes(beat.shots[0].clip))
    .map((beat) => {
      let offset = 0;
      const shots = beat.shots.map((shot) => {
        const duration = frames(shot.seconds);
        const timedShot = { shot, offset, duration };
        offset += duration;
        return timedShot;
      });
      const scene = { beat, shots, start: next, duration: offset };
      next += scene.duration - CUT;
      return scene;
    });
  return { scenes, outroStart: next, duration: next + OUTRO };
};

// Text stays between y = 250 and y = 1520, clear of Shorts and Reels controls.
const GUTTER = 100;
const CLIP_WIDTH = 890;
const ease = (frame: number, start: number, end: number) => interpolate(frame, [start, end], [0, 1], {
  easing: Easing.out(Easing.cubic),
  extrapolateLeft: 'clamp',
  extrapolateRight: 'clamp',
});

const Caption: React.FC<{ beat: Beat; first: boolean; duration: number }> = ({ beat, first, duration }) => {
  const frame = useCurrentFrame();
  const enter = ease(frame, first ? 0 : CUT / 2, 11);
  const leave = ease(frame, duration - CUT, duration - CUT / 2);
  return (
    <div style={{
      position: 'absolute', top: 350, left: GUTTER, right: GUTTER,
      transform: `translateY(${(1 - enter) * 15}px)`,
      opacity: (first ? 0.85 + 0.15 * enter : enter) * (1 - leave),
    }}>
      <div style={{ color: ACCENT, fontSize: 32, fontWeight: 700, letterSpacing: 3, textTransform: 'uppercase' }}>
        {beat.kicker}
      </div>
      <div style={{ marginTop: 15, color: INK, fontSize: first ? 108 : 86, fontWeight: 700, lineHeight: 1.02, whiteSpace: 'pre-line' }}>
        {beat.title}
      </div>
    </div>
  );
};

const DockClip: React.FC<{
  shot: Shot;
  index: number;
  offset: number;
  duration: number;
  sceneDuration: number;
}> = ({ shot, index, offset, duration, sceneDuration }) => {
  const frame = useCurrentFrame();
  const sceneFrame = offset + frame;
  const { focus, blur, reframe } = shot;
  // Start close enough to read the compact capsule, then give the expanding card its full width.
  const [cx, cy, cw, ch] = reframe
    ? shot.crop.map((value, i) => interpolate(
      frame, [frames(reframe.start), frames(reframe.end)], [reframe.crop[i], value],
      { easing: Easing.inOut(Easing.sin), extrapolateLeft: 'clamp', extrapolateRight: 'clamp' },
    ))
    : shot.crop;
  const [sourceWidth, sourceHeight] = shot.clip === 'promo-gaming' ? [1100, 420] : [880, 580];
  const scale = CLIP_WIDTH / cw;
  const height = ch * scale;
  const focusX = focus ? (focus[0] + focus[2] / 2 - cx) * scale : CLIP_WIDTH / 2;
  const focusY = focus ? (focus[1] + focus[3] / 2 - cy) * scale : height / 2;
  const travel = index % 2 === 0 ? -1 : 1;
  const enter = index === 0 ? 1 : ease(sceneFrame, 0, CUT);
  const leave = ease(sceneFrame, sceneDuration - CUT, sceneDuration);
  const slide = travel * (28 * (1 - enter) - 14 * leave);
  const push = reframe ? 1 : interpolate(frame, [0, duration], [1, duration < 40 ? 1.04 : 1.065], { easing: Easing.inOut(Easing.sin) });
  const ringStart = reframe ? frames(reframe.end) + 12 : 2;
  const ringIn = ease(frame, ringStart, ringStart + 10);

  return (
    <div style={{
      position: 'absolute', left: (PROMO_WIDTH - CLIP_WIDTH) / 2, top: reframe ? 700 : Math.max(700, 940 - height / 2),
      width: CLIP_WIDTH, height, borderRadius: reframe || shot.clip === CPU_READY ? '4px 4px 18px 18px' : 18,
      overflow: 'hidden', boxShadow: `0 24px 54px ${INK}40`,
      transformOrigin: `${focusX}px ${focusY}px`,
      transform: `translateX(${slide}px) scale(${push * (0.985 + 0.015 * enter)})`,
      opacity: enter * (1 - leave),
    }}>
      <OffthreadVideo
        src={staticFile(`clips/${shot.clip}.mp4`)}
        trimBefore={frames(shot.from)}
        playbackRate={shot.rate ?? 1}
        muted
        style={{ width: sourceWidth * scale, height: sourceHeight * scale, position: 'absolute', left: -cx * scale, top: -cy * scale }}
      />
      {blur && (
        <div style={{
          position: 'absolute', left: (blur[0] - cx) * scale, top: (blur[1] - cy) * scale,
          width: blur[2] * scale, height: blur[3] * scale, backdropFilter: 'blur(10px)',
        }} />
      )}
      {focus && (
        <div style={{
          position: 'absolute', left: (focus[0] - cx) * scale, top: (focus[1] - cy) * scale,
          width: focus[2] * scale, height: focus[3] * scale, boxSizing: 'border-box',
          border: `3px solid ${CREAM}`, borderRadius: 10, boxShadow: `0 0 0 1px ${ACCENT}`,
          opacity: 0.72 * ringIn, transform: `scale(${1 + 0.025 * (1 - ringIn)})`,
        }} />
      )}
    </div>
  );
};

const Outro: React.FC = () => {
  const frame = useCurrentFrame();
  const enter = ease(frame, CUT / 2, 11);
  return (
    <AbsoluteFill style={{
      top: 250, height: 1180, alignItems: 'center', justifyContent: 'center', flexDirection: 'column',
      opacity: enter, transform: `translateY(${(1 - enter) * 15}px)`,
    }}>
      <Img src={staticFile('mochi-icon.png')} style={{ width: 240, height: 240 }} />
      <div style={{ marginTop: 12, color: INK, fontSize: 106, fontWeight: 700 }}>MochiDock</div>
      <div style={{ marginTop: 4, color: MUTED, fontSize: 40, fontWeight: 600 }}>Get MochiDock for Windows</div>
      <div style={{ marginTop: 36, padding: '18px 34px', borderRadius: 999, background: ACCENT, color: CREAM, fontSize: 39, fontWeight: 700 }}>
        github.com/sleepisfun17/MochiDock
      </div>
    </AbsoluteFill>
  );
};

export const MochiDockShort: React.FC<{ available: string[] | null }> = ({ available }) => {
  const frame = useCurrentFrame();
  const { scenes, outroStart, duration } = getTimeline(available);
  const progress = frame / (duration - 1);
  return (
    <AbsoluteFill style={{
      background: `radial-gradient(ellipse at ${30 + 30 * progress}% 55%, ${CREAM} 0%, transparent 64%), linear-gradient(160deg, ${CREAM}, ${SAGE})`,
      fontFamily: 'Quicksand, sans-serif',
    }}>
      <style>{fontFaces}</style>
      {scenes.map(({ beat, shots, start, duration: sceneDuration }, index) => (
        <Sequence key={beat.kicker} name={beat.kicker} from={start} durationInFrames={sceneDuration}>
          {shots.map(({ shot, offset, duration: shotDuration }) => (
            <Sequence key={offset} from={offset} durationInFrames={shotDuration}>
              <DockClip shot={shot} index={index} offset={offset} duration={shotDuration} sceneDuration={sceneDuration} />
            </Sequence>
          ))}
          <Caption beat={beat} first={index === 0} duration={sceneDuration} />
        </Sequence>
      ))}
      <Sequence name="MochiDock" from={outroStart} durationInFrames={OUTRO}>
        <Outro />
      </Sequence>
      <div style={{ position: 'absolute', top: 1400, left: GUTTER, right: GUTTER, height: 5, borderRadius: 4, background: `${ACCENT}28`, overflow: 'hidden' }}>
        <div style={{ width: `${progress * 100}%`, height: '100%', backgroundColor: ACCENT, borderRadius: 4 }} />
      </div>
    </AbsoluteFill>
  );
};
