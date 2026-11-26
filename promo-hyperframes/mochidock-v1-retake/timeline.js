const film = document.getElementById('mochidock');
const timeline = gsap.timeline({ paused: true });
const frameWidth = 940;

function readRect(value) {
  return value.split(' ').map(Number);
}

function setCrop(windowElement, video, crop, source) {
  const [x, y, width, height] = crop;
  const [sourceWidth, sourceHeight] = source;
  const scale = frameWidth / width;
  windowElement.style.setProperty('--clip-height', `${height * scale}px`);
  Object.assign(video.style, {
    left: `${-x * scale}px`,
    top: `${-y * scale}px`,
    width: `${sourceWidth * scale}px`,
    height: `${sourceHeight * scale}px`,
  });
}

for (const windowElement of film.querySelectorAll('.window')) {
  const video = windowElement.querySelector('video');
  const start = Number(video.dataset.start);
  const duration = Number(video.dataset.duration);
  const crop = readRect(windowElement.dataset.crop);
  const source = readRect(windowElement.dataset.source);
  setCrop(windowElement, video, crop, source);
  timeline.set(windowElement, { visibility: 'visible' }, start);
  timeline.set(windowElement, { visibility: 'hidden' }, start + duration);
}

// The footage cuts in place. The headline follows with a short masked rise.
for (const caption of film.querySelectorAll('.caption')) {
  const start = Number(caption.dataset.start);
  if (start > 0) {
    timeline.from(caption, {
      y: 24, clipPath: 'inset(12% 0 0 0)', opacity: 0,
      duration: 0.22, ease: 'power2.out', immediateRender: false,
    }, start);
  }
}

for (const label of film.querySelectorAll('[data-label-cues]')) {
  for (const [start, textContent] of JSON.parse(label.dataset.labelCues)) {
    timeline.set(label, { textContent }, start);
  }
}

const outroStart = Number(film.querySelector('.outro').dataset.start);
timeline.from('.outro', { opacity: 0, duration: 0.16, ease: 'power1.out', immediateRender: false }, outroStart);
timeline.from('.outro-logo', { scale: 0.86, duration: 0.5, ease: 'power2.out', immediateRender: false }, outroStart);
timeline.fromTo('.progress-fill', { scaleX: 0 }, { scaleX: 1, duration: Number(film.dataset.duration), ease: 'none' }, 0);

window.__timelines = window.__timelines || {};
window.__timelines[film.dataset.compositionId] = timeline;
