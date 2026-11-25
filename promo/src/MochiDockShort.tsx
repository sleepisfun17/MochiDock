import React from 'react';
import {
  AbsoluteFill, Img, OffthreadVideo, Sequence, interpolate, spring, staticFile, useCurrentFrame, useVideoConfig, Easing,
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

// Every clip is 660 x 420 (the dock at the top of a capture backdrop). cropH keeps only the dock; from = where in the clip to start.
type Beat = { clip: string; cropH: number; from: number; dur: number; rate?: number; kicker: string; title: string };
const BEATS: Beat[] = [
  { clip: 'promo-hover', cropH: 180, from: 2.3, dur: 4, kicker: 'MochiDock for Windows', title: 'Your PC needs a Dynamic Island' },
  { clip: 'promo-media', cropH: 180, from: 0.4, dur: 3, kicker: 'Media', title: 'Now playing, with synced lyrics' },
  { clip: 'promo-stats', cropH: 350, from: 0.4, dur: 3.5, kicker: 'Stats', title: 'Live CPU, GPU & memory' },
  { clip: 'promo-weather', cropH: 282, from: 0.4, dur: 3, kicker: 'Weather', title: 'The forecast at a glance' },
  { clip: 'promo-prayer', cropH: 276, from: 0.4, dur: 3, kicker: 'Prayer times', title: 'Next prayer & qibla' },
  { clip: 'promo-fx', cropH: 366, from: 0.4, dur: 3, kicker: 'FX rates', title: 'Currencies, no API key' },
  { clip: 'promo-theme', cropH: 410, from: 0.6, dur: 5.5, rate: 1.75, kicker: 'Themes', title: 'Four cozy themes' },
  { clip: 'promo-mascot', cropH: 410, from: 0.6, dur: 5.5, rate: 1.8, kicker: 'Mascots', title: 'Meet Mochi & friends' },
];
const OUTRO = 3.5;
const STARTS = BEATS.reduce<number[]>((a, b, i) => [...a, i === 0 ? 0 : a[i - 1] + s(BEATS[i - 1].dur)], []);
export const TOTAL_FRAMES = STARTS[STARTS.length - 1] + s(BEATS[BEATS.length - 1].dur) + s(OUTRO);

const SRC_W = 660;
const FRAME_W = 1000;   // the dock frame on screen; the clip is scaled by FRAME_W / SRC_W

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
  const scale = FRAME_W / SRC_W;
  const h = beat.cropH * scale;
  const enter = spring({ frame: f - 4, fps, config: { damping: 16, mass: 0.7 } });
  const push = interpolate(f, [0, s(beat.dur)], [1, 1.06], { easing: Easing.inOut(Easing.quad) });
  // Centre the dock frame in the space between the caption and the bottom safe area (y 760 to 1520).
  const top = 760 + Math.max(0, (760 - h) / 2);
  return (
    <div style={{
      position: 'absolute', left: (1080 - FRAME_W) / 2, top, width: FRAME_W, height: h, borderRadius: 40, overflow: 'hidden',
      boxShadow: '0 40px 90px rgba(40, 51, 34, 0.35)', transform: `scale(${(0.9 + 0.1 * enter) * push})`, opacity: enter,
    }}>
      <OffthreadVideo
        src={staticFile(`clips/${beat.clip}.mp4`)}
        startFrom={s(beat.from)}
        playbackRate={beat.rate ?? 1}
        muted
        style={{ width: FRAME_W, height: 420 * scale, position: 'absolute', top: 0, left: 0 }}
      />
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

export const MochiDockShort: React.FC = () => (
  <AbsoluteFill style={{ background: `linear-gradient(160deg, ${CREAM} 0%, ${SAGE} 100%)`, fontFamily: 'Quicksand, sans-serif' }}>
    <style>{fontFaces}</style>
    {BEATS.map((b, i) => (
      <Sequence key={b.clip} from={STARTS[i]} durationInFrames={s(b.dur)}>
        <DockClip beat={b} />
        <Caption kicker={b.kicker} title={b.title} />
      </Sequence>
    ))}
    <Sequence from={TOTAL_FRAMES - s(OUTRO)} durationInFrames={s(OUTRO)}>
      <Outro />
    </Sequence>
  </AbsoluteFill>
);
