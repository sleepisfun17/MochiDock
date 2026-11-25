const film = document.getElementById('film');
const timeline = gsap.timeline({ paused: true });
const scenes = [...film.querySelectorAll('.scene:not(.outro)')];
const outro = film.querySelector('.outro');
const outroStart = 40.5;
const dissolve = 0.42;

function numbers(value) {
  return value.split(' ').map(Number);
}

function setupViewport(viewport) {
  const [x, y, width, height] = numbers(viewport.dataset.crop);
  const panelWidth = Number(viewport.dataset.width || 800);
  const sourceWidth = Number(viewport.dataset.sourceWidth || 880);
  const sourceHeight = Number(viewport.dataset.sourceHeight || 580);
  const scale = panelWidth / width;
  viewport.style.setProperty('--pane-width', `${panelWidth}px`);
  viewport.style.setProperty('--pane-height', `${scale * height}px`);

  const lens = viewport.querySelector('.lens');
  Object.assign(lens.querySelector('video').style, {
    left: `${-x * scale}px`,
    top: `${-y * scale}px`,
    width: `${sourceWidth * scale}px`,
    height: `${sourceHeight * scale}px`,
  });

  const redaction = lens.querySelector('.redaction');
  if (redaction) {
    const [coverX, coverY, coverWidth, coverHeight] = numbers(viewport.dataset.cover);
    Object.assign(redaction.style, {
      left: `${(coverX - x) * scale}px`,
      top: `${(coverY - y) * scale}px`,
      width: `${coverWidth * scale}px`,
      height: `${coverHeight * scale}px`,
    });
  }

  const [focusX, focusY] = numbers(viewport.dataset.focus);
  lens.style.transformOrigin = `${focusX}% ${focusY}%`;
  return lens;
}

function sceneStart(scene) {
  return Number(scene.querySelector('video').dataset.start);
}

scenes.forEach((scene, index) => {
  const start = sceneStart(scene);
  const end = index + 1 < scenes.length ? sceneStart(scenes[index + 1]) : outroStart;
  const caption = scene.querySelector('.caption');
  timeline.set(scene, { visibility: 'visible' }, start);
  timeline.set(scene, { visibility: 'hidden' }, end + dissolve);
  if (index > 0) {
    timeline.fromTo(scene, { opacity: 0 }, { opacity: 1, duration: dissolve, ease: 'sine.inOut', immediateRender: false }, start);
    timeline.fromTo(caption, { opacity: 0, y: -9 }, {
      opacity: 1, y: 0, duration: 0.32, ease: 'power2.out', immediateRender: false,
    }, start + 0.1);
  } else {
    timeline.fromTo(caption, { opacity: 0.7, y: 8 }, { opacity: 1, y: 0, duration: 0.34, ease: 'power2.out' }, 0);
  }
  timeline.to(caption, { opacity: 0, y: -7, duration: 0.18, ease: 'sine.in' }, end - 0.11);

  for (const viewport of scene.querySelectorAll('.viewport')) {
    const lens = setupViewport(viewport);
    if (viewport.classList.contains('overview')) {
      timeline.fromTo(lens, { scale: 1 }, { scale: Number(viewport.dataset.zoom), duration: 0.8, ease: 'sine.inOut' }, 0);
      timeline.to(lens, { scale: 1, duration: 0.68, ease: 'sine.inOut' }, 0.88);
      timeline.fromTo(viewport, { y: 246 }, { y: 0, duration: 0.76, ease: 'sine.inOut' }, 2.56);
      continue;
    }

    const mediaStart = Number(viewport.querySelector('video').dataset.start);
    const cameraStart = mediaStart + 0.45;
    timeline.fromTo(lens, { scale: 1 }, {
      scale: Number(viewport.dataset.zoom), duration: end - cameraStart - 0.18,
      ease: 'sine.inOut', immediateRender: false,
    }, cameraStart);
    if (viewport.classList.contains('opening-detail')) {
      timeline.set(viewport, { visibility: 'visible' }, mediaStart);
      timeline.fromTo(viewport, { opacity: 0 }, { opacity: 1, duration: 0.47, ease: 'sine.inOut', immediateRender: false }, mediaStart);
    }
  }
});

timeline.set(outro, { visibility: 'visible' }, outroStart);
timeline.fromTo(outro, { opacity: 0 }, { opacity: 1, duration: dissolve, ease: 'sine.inOut', immediateRender: false }, outroStart);
timeline.fromTo('.outro-content', { opacity: 0, y: 14 }, {
  opacity: 1, y: 0, duration: 0.45, ease: 'power2.out', immediateRender: false,
}, outroStart + 0.06);
timeline.set({}, {}, Number(film.dataset.duration));

window.__timelines = window.__timelines || {};
window.__timelines[film.dataset.compositionId] = timeline;
