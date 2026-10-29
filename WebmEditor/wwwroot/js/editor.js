// Timeline editor: video preview on top, draggable/trimmable audio clip below.
// All state lives here; C# only calls loadVideo / loadAudio / getState.
window.editor = (() => {
  const ORIGIN = 200;      // px where timeline time 0 sits (room on the left for negative audio start)
  const RES = 200;         // waveform peaks per second
  const MIN = 0.05;        // minimum clip length (s)
  const S = { pps: 100, t: 0, playing: false, hasV: false, hasA: false,
              vdur: 0, adur: 0, peaks: null, start: 0, trimS: 0, trimE: 0 };
  const el = {};
  const aud = new Audio();
  let raf = 0, clockT0 = 0, clockP0 = 0, rulerKey = '';

  const $ = id => document.getElementById(id);
  const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
  const clipLen = () => Math.max(0, S.adur - S.trimS - S.trimE);
  const total = () => Math.max(S.vdur, S.start + clipLen(), 0);
  const X = t => ORIGIN + t * S.pps;
  const T = x => (x - ORIGIN) / S.pps;
  const fmt = t => { const m = Math.floor(t / 60); return m + ':' + (t - m * 60).toFixed(3).padStart(6, '0'); };

  function init(rootId) {
    $(rootId).innerHTML = `
      <div class="stage"><video id="vid" muted playsinline></video></div>
      <div class="transport">
        <button id="bStart" title="Go to start">⏮</button>
        <button id="bPlay" title="Play / Pause (Space)">▶</button>
        <button id="bStop" title="Stop">⏹</button>
        <span id="time" class="time"></span>
        <span id="info" class="small"></span>
        <span class="grow"></span>
        <span>Audio start (s)</span>
        <button data-n="-0.1">-0.1</button><button data-n="-0.01">-0.01</button>
        <input id="startVal" class="num" type="number" step="0.01" value="0.000">
        <button data-n="0.01">+0.01</button><button data-n="0.1">+0.1</button>
        <span>Zoom</span><input id="zoom" type="range" min="20" max="400" value="100">
      </div>
      <div class="tlwrap" id="tlwrap"><div class="tl" id="tl">
        <canvas id="ruler"></canvas>
        <div class="track" id="vtrack"><div class="vbar" id="vbar"></div></div>
        <div class="track" id="atrack">
          <div class="clip" id="clip"><div class="h hl"></div><canvas id="wave"></canvas><div class="lbl" id="clbl"></div><div class="h hr"></div></div>
        </div>
        <div class="playhead" id="ph"></div>
      </div></div>`;
    ['vid','bStart','bPlay','bStop','time','info','startVal','zoom','tl','ruler','vtrack','atrack','vbar','clip','wave','clbl','ph']
      .forEach(id => el[id] = $(id));

    el.bPlay.onclick = () => S.playing ? pause() : play();
    el.bStart.onclick = () => seek(0);
    el.bStop.onclick = () => { pause(); seek(0); };
    el.zoom.oninput = () => { S.pps = +el.zoom.value; layout(true); };
    el.startVal.onchange = () => { S.start = parseFloat(el.startVal.value) || 0; layout(false); };
    document.querySelectorAll('[data-n]').forEach(b => b.onclick = () => { S.start += parseFloat(b.dataset.n); layout(false); });
    window.addEventListener('keydown', e => {
      if (e.code === 'Space' && !/INPUT|TEXTAREA/.test(document.activeElement.tagName)) { e.preventDefault(); S.playing ? pause() : play(); }
    });

    // click / drag on ruler or empty track area moves the playhead
    [el.ruler, el.vtrack, el.atrack].forEach(n => {
      const go = e => seek(T(e.clientX - el.tl.getBoundingClientRect().left));
      n.addEventListener('pointerdown', e => { if (e.target.closest('.clip')) return; n.setPointerCapture(e.pointerId); n._s = true; go(e); });
      n.addEventListener('pointermove', e => { if (n._s) go(e); });
      n.addEventListener('pointerup', () => n._s = false);
    });

    // drag the audio clip (body = move, edges = trim)
    let drag = null;
    el.clip.addEventListener('pointerdown', e => {
      el.clip.setPointerCapture(e.pointerId);
      const c = e.target.classList;
      drag = { mode: c.contains('hl') ? 'l' : c.contains('hr') ? 'r' : 'm', x: e.clientX, start: S.start, ts: S.trimS, te: S.trimE };
    });
    el.clip.addEventListener('pointermove', e => {
      if (!drag) return;
      const dt = (e.clientX - drag.x) / S.pps;
      if (drag.mode === 'm') {
        let s = drag.start + dt;
        const len = clipLen();
        for (const c of [0, S.t]) if (Math.abs(X(s) - X(c)) < 8) s = c;                 // snap start to 0 / playhead
        if (S.vdur && Math.abs(X(s + len) - X(S.vdur)) < 8) s = S.vdur - len;           // snap end to video end
        S.start = s;
        layout(false);
      } else if (drag.mode === 'l') {
        const ts = clamp(drag.ts + dt, 0, S.adur - drag.te - MIN);
        S.trimS = ts; S.start = drag.start + (ts - drag.ts);
        layout(true);
      } else {
        S.trimE = clamp(drag.te - dt, 0, S.adur - drag.ts - MIN);
        layout(true);
      }
      if (S.playing) syncMedia(false);
    });
    const endDrag = () => drag = null;
    el.clip.addEventListener('pointerup', endDrag);
    el.clip.addEventListener('pointercancel', endDrag);

    layout(true);
  }

  // ---------- loading (urls = candidate URLs tried in order; both return a status string for the log) ----------
  function tryVideo(url) {
    const v = el.vid;
    return new Promise(res => {
      const to = setTimeout(() => res('timed out waiting for video metadata'), 8000);
      v.onloadedmetadata = () => { clearTimeout(to); res(''); };
      v.onerror = () => { clearTimeout(to); res('media error code ' + (v.error ? v.error.code + ' ' + v.error.message : '?')); };
      v.src = url;
    });
  }

  async function loadVideo(urls, name) {
    pause();
    const v = el.vid, fails = [];
    let ok = false;
    for (const url of urls) {
      const err = await tryVideo(url);
      if (!err) { ok = true; break; }
      fails.push(err + ' [' + url + ']');
    }
    if (!ok) { S.hasV = false; S.vdur = 0; layout(false); return 'Video failed: ' + fails.join(' | '); }
    if (v.duration === Infinity) {   // webm without a duration in its header
      await new Promise(r => { v.ontimeupdate = () => { v.ontimeupdate = null; v.currentTime = 0; r(); }; v.currentTime = 1e101; });
    }
    S.hasV = isFinite(v.duration) && v.duration > 0;
    S.vdur = S.hasV ? v.duration : 0;
    el.vbar.textContent = name;
    seek(0); layout(false);
    return S.hasV ? 'Video loaded: ' + S.vdur.toFixed(3) + 's (' + v.videoWidth + 'x' + v.videoHeight + ')' : 'Video loaded but its duration is unknown';
  }

  async function loadAudio(urls, name) {
    pause();
    const fails = [];
    for (const url of urls) {
      try {
        const resp = await fetch(url);
        if (!resp.ok) throw new Error('HTTP ' + resp.status);
        const buf = await resp.arrayBuffer();
        const ctx = new (window.AudioContext || window.webkitAudioContext)();
        const ab = await ctx.decodeAudioData(buf);
        ctx.close();
        const n = Math.ceil(ab.duration * RES), per = ab.sampleRate / RES, peaks = new Float32Array(n);
        for (let ch = 0; ch < ab.numberOfChannels; ch++) {
          const d = ab.getChannelData(ch);
          for (let i = 0; i < n; i++) {
            let m = 0;
            const a = Math.floor(i * per), b = Math.min(d.length, Math.floor((i + 1) * per));
            for (let j = a; j < b; j++) { const v = Math.abs(d[j]); if (v > m) m = v; }
            if (m > peaks[i]) peaks[i] = m;
          }
        }
        let mx = 0; for (let i = 0; i < n; i++) if (peaks[i] > mx) mx = peaks[i];
        if (mx > 0) for (let i = 0; i < n; i++) peaks[i] /= mx;
        S.peaks = peaks; S.adur = ab.duration; S.hasA = true;
        S.start = 0; S.trimS = 0; S.trimE = 0;
        aud.src = url;
        el.clbl.textContent = name;
        layout(true);
        return 'Audio loaded: ' + S.adur.toFixed(3) + 's';
      } catch (err) {
        fails.push((err && err.message ? err.message : err) + ' [' + url + ']');
      }
    }
    S.hasA = false; layout(true);
    return 'Audio failed: ' + fails.join(' | ');
  }

  // ---------- drawing ----------
  function drawRuler(w) {
    const key = w + '|' + S.pps;
    if (key === rulerKey) return;
    rulerKey = key;
    const c = el.ruler, g = c.getContext('2d');
    c.width = w; c.height = 28; c.style.width = w + 'px';
    g.fillStyle = '#2a2a2a'; g.fillRect(0, 0, w, 28);
    g.fillStyle = '#1c1c1c'; g.fillRect(0, 0, ORIGIN, 28);
    g.fillStyle = '#aaa'; g.strokeStyle = '#666'; g.font = '10px sans-serif';
    const lab = S.pps >= 80 ? 1 : S.pps >= 40 ? 2 : 5;
    for (let s = Math.ceil(-ORIGIN / S.pps); X(s) < w; s++) {
      const x = Math.round(X(s)) + .5, big = s % lab === 0;
      g.beginPath(); g.moveTo(x, big ? 14 : 20); g.lineTo(x, 28); g.stroke();
      if (big) g.fillText(s + 's', x + 3, 11);
    }
  }

  function drawWave() {
    const c = el.wave, w = Math.max(1, Math.round(clipLen() * S.pps)), H = 64;
    c.width = w; c.height = H;
    const g = c.getContext('2d');
    g.fillStyle = '#7fd1ff';
    if (!S.peaks) return;
    for (let px = 0; px < w; px++) {
      const i0 = Math.floor((S.trimS + px / S.pps) * RES);
      const i1 = Math.max(i0 + 1, Math.floor((S.trimS + (px + 1) / S.pps) * RES));
      let m = 0;
      for (let i = i0; i < i1 && i < S.peaks.length; i++) if (S.peaks[i] > m) m = S.peaks[i];
      const h = Math.max(1, m * H * 0.48);
      g.fillRect(px, H / 2 - h, 1, h * 2);
    }
  }

  function layout(wave) {
    S.start = Math.max(S.start, -(ORIGIN - 4) / S.pps);
    const end = Math.max(total(), 10) + 3;
    const maxPps = (30000 - ORIGIN) / end;                       // keep canvases under browser limits
    if (S.pps > maxPps) { S.pps = maxPps; el.zoom.value = S.pps; }
    const w = Math.round(ORIGIN + end * S.pps);
    el.tl.style.width = w + 'px';
    drawRuler(w);
    Object.assign(el.vbar.style, { left: ORIGIN + 'px', width: S.vdur * S.pps + 'px', display: S.hasV ? '' : 'none' });
    Object.assign(el.clip.style, { left: X(S.start) + 'px', width: clipLen() * S.pps + 'px', display: S.hasA ? '' : 'none' });
    if (wave) drawWave();
    el.ph.style.left = (X(S.t) - 1) + 'px';
    if (document.activeElement !== el.startVal) el.startVal.value = S.start.toFixed(3);
    el.info.textContent = S.hasA ? `trim in ${S.trimS.toFixed(2)}s · trim out ${S.trimE.toFixed(2)}s` : '';
    el.time.textContent = fmt(S.t) + ' / ' + fmt(S.vdur);
  }

  // ---------- playback (video + audio together) ----------
  function syncMedia(hard) {
    const v = el.vid;
    if (S.hasV && S.t < S.vdur) {
      if (hard || Math.abs(v.currentTime - S.t) > 0.15) v.currentTime = S.t;
      if (S.playing && v.paused) v.play().catch(() => {});
    } else if (!v.paused) v.pause();

    const ce = S.start + clipLen();
    if (S.hasA && S.t >= S.start && S.t < ce) {
      const at = S.trimS + (S.t - S.start);
      if (hard || aud.paused || Math.abs(aud.currentTime - at) > 0.15) aud.currentTime = at;
      if (S.playing && aud.paused) aud.play().catch(() => {});
    } else if (!aud.paused) aud.pause();
  }

  function play() {
    if (S.playing || (!S.hasV && !S.hasA)) return;
    if (S.t >= (S.vdur || total()) - 0.01) S.t = 0;
    S.playing = true; clockT0 = performance.now(); clockP0 = S.t;
    el.bPlay.textContent = '⏸';
    syncMedia(true);
    raf = requestAnimationFrame(tick);
  }

  function pause() {
    S.playing = false;
    el.bPlay.textContent = '▶';
    cancelAnimationFrame(raf);
    el.vid.pause(); aud.pause();
  }

  function tick() {
    if (!S.playing) return;
    S.t = clockP0 + (performance.now() - clockT0) / 1000;
    const end = S.vdur || total();
    if (S.t >= end) { S.t = end; pause(); layout(false); return; }
    syncMedia(false);
    el.ph.style.left = (X(S.t) - 1) + 'px';
    el.time.textContent = fmt(S.t) + ' / ' + fmt(S.vdur);
    const wrap = el.tl.parentElement, px = X(S.t);                // keep playhead in view
    if (px > wrap.scrollLeft + wrap.clientWidth - 20 || px < wrap.scrollLeft) wrap.scrollLeft = px - 60;
    raf = requestAnimationFrame(tick);
  }

  function seek(t) {
    S.t = clamp(t, 0, Math.max(total(), 0));
    if (S.playing) { clockT0 = performance.now(); clockP0 = S.t; syncMedia(true); }
    else if (S.hasV) el.vid.currentTime = Math.min(S.t, S.vdur);
    el.ph.style.left = (X(S.t) - 1) + 'px';
    el.time.textContent = fmt(S.t) + ' / ' + fmt(S.vdur);
  }

  // [delay seconds before audio starts, seconds to cut from audio start, audio length to keep, video duration]
  function getState() {
    const neg = Math.max(0, -S.start);          // audio placed before video start: cut that part off
    return [Math.max(0, S.start), S.trimS + neg, clipLen() - neg, S.vdur];
  }

  function scrollLog() { const l = document.querySelector('.log'); if (l) l.scrollTop = l.scrollHeight; }

  return { init, loadVideo, loadAudio, getState, scrollLog };
})();
