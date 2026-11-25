import React from 'react';
import {
  AbsoluteFill, Freeze, Img, OffthreadVideo, Sequence, interpolate, spring, staticFile, useCurrentFrame, useVideoConfig,
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

const FPS = 30;
const s = (sec: number) => Math.round(sec * FPS);
const EXTRA_FEATURE_FRAMES = 45;

// Each clip is the dock at the top of a capture backdrop (880 x 580, or the gaming strip). `crop` is the part of the clip that is shown, as
// [x, y, w, h] in clip pixels, fitted to the original layout. from = where in the clip to start.
type Beat = {
  clip: string; src: [number, number]; crop: [number, number, number, number]; from: number; dur: number;
  kicker: string; title: string; optional?: boolean; holdAt?: number;
};
const DOCK: [number, number] = [880, 580];   // the dock itself spans x 28 to 801 of the clip
const ALL_BEATS: Beat[] = [
  { clip: 'promo-hover', src: DOCK, crop: [24, 0, 782, 215], from: 2.3, dur: 4, kicker: 'MochiDock for Windows', title: 'Hover to open your dock', holdAt: 6.6 },
  { clip: 'promo-media', src: DOCK, crop: [24, 0, 782, 215], from: 0.4, dur: 3, kicker: 'Media', title: 'Control music and follow lyrics' },
  { clip: 'promo-stats', src: DOCK, crop: [24, 0, 532, 430], from: 0.4, dur: 3.5, kicker: 'Stats', title: 'Check CPU, GPU & RAM usage' },
  { clip: 'promo-gaming', src: [1100, 420], crop: [270, 0, 560, 200], from: 1, dur: 3.5, kicker: 'Gaming mode', title: 'See CPU & GPU while gaming', optional: true },
  { clip: 'promo-weather', src: DOCK, crop: [24, 0, 782, 344], from: 0.4, dur: 3, kicker: 'Weather', title: 'See the forecast hour by hour' },
  { clip: 'promo-prayer', src: DOCK, crop: [24, 50, 782, 240], from: 0.4, dur: 3, kicker: 'Prayer times', title: 'See your next prayer and qibla' },
  { clip: 'promo-fx', src: DOCK, crop: [24, 0, 782, 450], from: 0.4, dur: 3, kicker: 'FX rates', title: 'Compare rates and see trends' },
  // End on the selected theme and mascot before the recording restores the defaults.
  { clip: 'promo-theme', src: DOCK, crop: [24, 0, 782, 500], from: 1.35, dur: 5.5, kicker: 'Themes', title: 'Switch your dock’s theme', holdAt: 6.4 },
  { clip: 'promo-mascot', src: DOCK, crop: [24, 0, 782, 500], from: 1.15, dur: 5.5, kicker: 'Mascots', title: 'Choose your dock mascot', holdAt: 7.1 },
];
const OUTRO = 3.5;
// public/clips/manifest.json (written by `npm run clips`) lists the clips that exist; optional scenes without a clip are skipped.
const buildBeats = (available: string[] | null): Beat[] =>
  ALL_BEATS.filter((b) => !b.optional || (available?.includes(b.clip) ?? false));
const beatFrames = (beat: Beat) => s(beat.dur) + EXTRA_FEATURE_FRAMES;
const totalFrames = (beats: Beat[]) => beats.reduce((n, b) => n + beatFrames(b), 0) + s(OUTRO);
export const getV1RefinedDuration = (available: string[] | null) => totalFrames(buildBeats(available));

// Frame area below the caption and above the bottom safe area (Shorts / Reels cover roughly y > 1520 and y < 250).
const AREA_TOP = 650;
const AREA_H = 860;
const FRAME_W = 1040;

const Caption: React.FC<{ kicker: string; title: string }> = ({ kicker, title }) => {
  const f = useCurrentFrame();
  const { fps } = useVideoConfig();
  const pop = spring({ frame: f, fps, config: { damping: 14, mass: 0.6 } });
  return (
    <div style={{ position: 'absolute', top: 290, left: 70, right: 70, textAlign: 'center', transform: `translateY(${(1 - pop) * 40}px)`, opacity: pop }}>
      <div style={{ display: 'inline-block', padding: '10px 28px', borderRadius: 999, background: ACCENT, color: CREAM, fontSize: 38, fontWeight: 700, letterSpacing: 1 }}>
        {kicker}
      </div>
      <div style={{ marginTop: 26, color: INK, fontSize: 82, fontWeight: 700, lineHeight: 1.08 }}>{title}</div>
    </div>
  );
};

const DockClip: React.FC<{ beat: Beat }> = ({ beat }) => {
  const f = useCurrentFrame();
  const { fps } = useVideoConfig();
  const [cx, cy, cw, ch] = beat.crop;
  // Fit the crop once so the recorded cursor and UI move together throughout the scene.
  const scale = Math.min(FRAME_W / cw, AREA_H / ch);
  const w = cw * scale;
  const h = ch * scale;
  const enter = spring({ frame: f - 4, fps, config: { damping: 16, mass: 0.7 } });
  const top = AREA_TOP + (AREA_H - h) / 2;
  const startFrame = s(beat.from);
  // holdAt is a source time. Freeze receives a frame relative to the scene.
  const holdFrame = beat.holdAt === undefined ? undefined : s(beat.holdAt) - startFrame;
  return (
    <div style={{
      position: 'absolute', left: (1080 - w) / 2, top, width: w, height: h, borderRadius: 40, overflow: 'hidden',
      boxShadow: '0 40px 90px rgba(40, 51, 34, 0.35)', transform: 'scale(1)', opacity: enter,
    }}>
      <Freeze frame={holdFrame ?? 0} active={holdFrame !== undefined && f >= holdFrame}>
        <OffthreadVideo
          src={staticFile(`clips/${beat.clip}.mp4`)}
          trimBefore={startFrame}
          muted
          style={{ width: beat.src[0] * scale, height: beat.src[1] * scale, position: 'absolute', top: -cy * scale, left: -cx * scale }}
        />
      </Freeze>
    </div>
  );
};

const Outro: React.FC = () => {
  const f = useCurrentFrame();
  const { fps } = useVideoConfig();
  const pop = spring({ frame: f, fps, config: { damping: 12, mass: 0.7 } });
  const t = (d: number) => interpolate(f, [d, d + 10], [0, 1], { extrapolateLeft: 'clamp', extrapolateRight: 'clamp' });
  return (
    <AbsoluteFill style={{ alignItems: 'center', justifyContent: 'center', flexDirection: 'column', paddingBottom: 120 }}>
      <Img src={staticFile('mochi-icon.png')} style={{ width: 300, height: 300, transform: `scale(${pop}) rotate(${(1 - pop) * -20}deg)` }} />
      <div style={{ marginTop: 30, color: INK, fontSize: 96, fontWeight: 700, opacity: t(6) }}>MochiDock</div>
      <div style={{ marginTop: 10, color: MUTED, fontSize: 44, fontWeight: 600, opacity: t(12) }}>Free & open source for Windows</div>
      <div style={{ marginTop: 40, padding: '16px 34px', borderRadius: 999, background: ACCENT, color: CREAM, fontSize: 38, fontWeight: 700, opacity: t(18) }}>
        github.com/sleepisfun17/MochiDock
      </div>
    </AbsoluteFill>
  );
};

export const MochiDockV1Refined: React.FC<{ available: string[] | null }> = ({ available }) => {
  const beats = buildBeats(available);
  const total = totalFrames(beats);
  let at = 0;
  return (
    <AbsoluteFill style={{ background: `linear-gradient(160deg, ${CREAM} 0%, ${SAGE} 100%)`, fontFamily: 'Quicksand, sans-serif' }}>
      <style>{fontFaces}</style>
      {beats.map((b) => {
        const from = at;
        at += beatFrames(b);
        return (
          <Sequence key={b.clip} from={from} durationInFrames={beatFrames(b)}>
            <DockClip beat={b} />
            <Caption kicker={b.kicker} title={b.title} />
          </Sequence>
        );
      })}
      <Sequence from={total - s(OUTRO)} durationInFrames={s(OUTRO)}>
        <Outro />
      </Sequence>
    </AbsoluteFill>
  );
};
