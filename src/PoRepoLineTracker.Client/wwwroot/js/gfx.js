/* ═══════════════════════════════════════════════════════════════════════════
   gfx.js — WebGL mesh backdrop, canvas activity ribbon, view transitions.

   Loaded as an ES module through IJSObjectReference (see GfxService.cs), so it
   is fetched only on the pages that actually use it rather than on boot.

   Everything here degrades in a chain, never a cliff:
       WebGL2 → WebGL1 → 2D canvas → the CSS gradient already in app.css
   and every path is switched off entirely under prefers-reduced-motion.
   ═══════════════════════════════════════════════════════════════════════════ */

const reduceMotion = () =>
    window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false;

/* Live handles, keyed by the element being driven, so a Blazor component that
   re-renders (or navigates away and back) replaces its loop instead of stacking
   a second one on top. Leaked rAF loops are the classic failure here: each one
   keeps running, keeps its GL context alive, and the browser hard-caps contexts
   at ~16 before it starts dropping the oldest. */
const handles = new WeakMap();

/* ─────────────────────────────────────────────────────────────────────────────
   Shaders — animated mesh gradient.

   Four moving colour wells summed in linear space. The cost is a handful of
   distance calculations per fragment, which is trivial for the GPU and is the
   whole reason this replaces `filter: blur(60px)` on three DOM elements: the
   CSS version forces a full-page repaint through a separable blur on every
   frame, on the main thread.
   ───────────────────────────────────────────────────────────────────────────── */

const VERT = `
attribute vec2 aPos;
void main() { gl_Position = vec4(aPos, 0.0, 1.0); }
`;

const FRAG = `
precision mediump float;
uniform vec2  uRes;
uniform float uTime;
uniform vec3  uC0, uC1, uC2, uC3;
uniform vec3  uBg;

// Cheap 2D value noise. Used only to break up banding, so a hash-based
// approximation is more than enough and costs no texture fetch.
float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
}

// Each well contributes an inverse-square falloff. The 0.0001 floor stops a
// division by zero when a fragment lands exactly on a well's centre.
vec3 well(vec2 uv, vec2 c, vec3 col, float power) {
    float d = distance(uv, c);
    return col * (power / (d * d * 24.0 + 0.0001));
}

void main() {
    // Normalise to the shorter axis so the wells stay circular on any aspect.
    vec2 uv = gl_FragCoord.xy / uRes;
    uv.x *= uRes.x / uRes.y;

    float t = uTime * 0.12;
    float ar = uRes.x / uRes.y;

    vec2 p0 = vec2(0.30 + 0.22 * sin(t * 0.90), 0.35 + 0.18 * cos(t * 0.70)) * vec2(ar, 1.0);
    vec2 p1 = vec2(0.75 + 0.18 * cos(t * 0.60), 0.28 + 0.20 * sin(t * 1.10)) * vec2(ar, 1.0);
    vec2 p2 = vec2(0.55 + 0.20 * sin(t * 1.30), 0.80 + 0.15 * cos(t * 0.50)) * vec2(ar, 1.0);
    vec2 p3 = vec2(0.15 + 0.16 * cos(t * 1.05), 0.75 + 0.19 * sin(t * 0.85)) * vec2(ar, 1.0);

    vec3 col = uBg;
    col += well(uv, p0, uC0, 0.055);
    col += well(uv, p1, uC1, 0.045);
    col += well(uv, p2, uC2, 0.040);
    col += well(uv, p3, uC3, 0.035);

    // Dither. Without it the smooth falloff bands visibly on 8-bit displays —
    // the exact artefact the CSS blur version was hiding behind its blur radius.
    col += (hash(gl_FragCoord.xy) - 0.5) * 0.012;

    gl_FragColor = vec4(clamp(col, 0.0, 1.0), 1.0);
}
`;

function compile(gl, type, src) {
    const sh = gl.createShader(type);
    gl.shaderSource(sh, src);
    gl.compileShader(sh);
    if (!gl.getShaderParameter(sh, gl.COMPILE_STATUS)) {
        // Not thrown: a shader that fails to compile must fall back to the 2D
        // path, not break the page it decorates.
        console.warn('[gfx] shader compile failed:', gl.getShaderInfoLog(sh));
        gl.deleteShader(sh);
        return null;
    }
    return sh;
}

const PALETTES = {
    light: {
        bg: [0.961, 0.953, 1.0],
        c0: [0.486, 0.227, 0.929],
        c1: [0.545, 0.361, 0.965],
        c2: [0.769, 0.710, 0.992],
        c3: [0.400, 0.310, 0.850]
    },
    dark: {
        bg: [0.043, 0.055, 0.129],
        c0: [0.365, 0.184, 0.788],
        c1: [0.259, 0.180, 0.600],
        c2: [0.180, 0.420, 0.520],
        c3: [0.420, 0.220, 0.560]
    }
};

/* ─────────────────────────────────────────────────────────────────────────────
   Public: startBackdrop
   ───────────────────────────────────────────────────────────────────────────── */
export function startBackdrop(canvas, theme) {
    if (!canvas) return false;
    stop(canvas);
    if (reduceMotion()) return false;

    const palette = PALETTES[theme === 'dark' ? 'dark' : 'light'];

    const gl = canvas.getContext('webgl2', { antialias: false, alpha: false, depth: false })
            || canvas.getContext('webgl',  { antialias: false, alpha: false, depth: false });

    if (!gl) return startBackdrop2d(canvas, palette);

    const vs = compile(gl, gl.VERTEX_SHADER, VERT);
    const fs = compile(gl, gl.FRAGMENT_SHADER, FRAG);
    if (!vs || !fs) return startBackdrop2d(canvas, palette);

    const prog = gl.createProgram();
    gl.attachShader(prog, vs);
    gl.attachShader(prog, fs);
    gl.linkProgram(prog);
    if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) {
        console.warn('[gfx] link failed:', gl.getProgramInfoLog(prog));
        return startBackdrop2d(canvas, palette);
    }
    gl.useProgram(prog);

    // One full-screen triangle rather than a quad: two triangles share an edge,
    // and fragments along that seam get shaded twice by the rasteriser.
    const buf = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, buf);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 3, -1, -1, 3]), gl.STATIC_DRAW);
    const aPos = gl.getAttribLocation(prog, 'aPos');
    gl.enableVertexAttribArray(aPos);
    gl.vertexAttribPointer(aPos, 2, gl.FLOAT, false, 0, 0);

    const u = n => gl.getUniformLocation(prog, n);
    const uRes = u('uRes'), uTime = u('uTime');
    gl.uniform3fv(u('uBg'), palette.bg);
    gl.uniform3fv(u('uC0'), palette.c0);
    gl.uniform3fv(u('uC1'), palette.c1);
    gl.uniform3fv(u('uC2'), palette.c2);
    gl.uniform3fv(u('uC3'), palette.c3);

    // DPR is capped at 1.5. This shader is fill-rate bound, so a 3x buffer on a
    // phone costs 4x the fragments for a gradient nobody can see the pixels of.
    const resize = () => {
        const dpr = Math.min(window.devicePixelRatio || 1, 1.5);
        const w = Math.max(1, Math.floor(canvas.clientWidth  * dpr));
        const h = Math.max(1, Math.floor(canvas.clientHeight * dpr));
        if (canvas.width !== w || canvas.height !== h) {
            // Attributes, not style — ScopedCssUiTests forbids inline styles here.
            canvas.width = w;
            canvas.height = h;
            gl.viewport(0, 0, w, h);
            gl.uniform2f(uRes, w, h);
        }
    };
    resize();

    const ro = new ResizeObserver(resize);
    ro.observe(canvas);

    const state = { raf: 0, ro, gl, running: true, paused: false };
    const start = performance.now();

    const frame = now => {
        if (!state.running) return;
        if (!state.paused) {
            gl.uniform1f(uTime, (now - start) / 1000);
            gl.drawArrays(gl.TRIANGLES, 0, 3);
            if (canvas.dataset.ready !== 'true') canvas.dataset.ready = 'true';
        }
        state.raf = requestAnimationFrame(frame);
    };
    state.raf = requestAnimationFrame(frame);

    // A backgrounded tab already throttles rAF to ~1Hz, but it does not stop it;
    // this drops the draw call entirely so a hidden login tab costs nothing.
    state.onVis = () => { state.paused = document.hidden; };
    document.addEventListener('visibilitychange', state.onVis);

    handles.set(canvas, state);
    return true;
}

/* 2D fallback: the same four wells as radial gradients, redrawn at 30fps. Not a
   still image — a static backdrop where the WebGL one moves would read as a
   broken page rather than a simpler one. 30fps because this path is
   CPU-rasterised and only exists on hardware that already failed at WebGL. */
function startBackdrop2d(canvas, palette) {
    const ctx = canvas.getContext('2d');
    if (!ctx) return false;

    const rgb = c => `rgb(${(c[0] * 255) | 0}, ${(c[1] * 255) | 0}, ${(c[2] * 255) | 0})`;
    const wells = [palette.c0, palette.c1, palette.c2, palette.c3].map(rgb);
    const bg = rgb(palette.bg);

    const state = { raf: 0, running: true, paused: false, last: 0 };
    const start = performance.now();

    const draw = now => {
        if (!state.running) return;
        state.raf = requestAnimationFrame(draw);
        if (state.paused || now - state.last < 33) return;
        state.last = now;

        const dpr = Math.min(window.devicePixelRatio || 1, 1.25);
        const w = Math.max(1, Math.floor(canvas.clientWidth * dpr));
        const h = Math.max(1, Math.floor(canvas.clientHeight * dpr));
        if (canvas.width !== w || canvas.height !== h) { canvas.width = w; canvas.height = h; }

        const t = (now - start) / 1000 * 0.12;
        ctx.fillStyle = bg;
        ctx.fillRect(0, 0, w, h);
        ctx.globalCompositeOperation = 'lighter';

        const pts = [
            [0.30 + 0.22 * Math.sin(t * 0.90), 0.35 + 0.18 * Math.cos(t * 0.70)],
            [0.75 + 0.18 * Math.cos(t * 0.60), 0.28 + 0.20 * Math.sin(t * 1.10)],
            [0.55 + 0.20 * Math.sin(t * 1.30), 0.80 + 0.15 * Math.cos(t * 0.50)],
            [0.15 + 0.16 * Math.cos(t * 1.05), 0.75 + 0.19 * Math.sin(t * 0.85)]
        ];

        pts.forEach(([px, py], i) => {
            const r = Math.max(w, h) * 0.45;
            const g = ctx.createRadialGradient(px * w, py * h, 0, px * w, py * h, r);
            g.addColorStop(0, wells[i]);
            g.addColorStop(1, 'rgba(0,0,0,0)');
            ctx.globalAlpha = 0.55;
            ctx.fillStyle = g;
            ctx.fillRect(0, 0, w, h);
        });

        ctx.globalAlpha = 1;
        ctx.globalCompositeOperation = 'source-over';
        if (canvas.dataset.ready !== 'true') canvas.dataset.ready = 'true';
    };

    state.raf = requestAnimationFrame(draw);
    state.onVis = () => { state.paused = document.hidden; };
    document.addEventListener('visibilitychange', state.onVis);
    handles.set(canvas, state);
    return true;
}

/* ─────────────────────────────────────────────────────────────────────────────
   Public: activity ribbon — a rolling throughput strip for the live feed.

   `push` feeds it one sample per SignalR progress frame; the loop eases the
   drawn value toward the latest sample so a burst of frames reads as a smooth
   rise rather than a step. Ring buffer, so it allocates nothing per frame.
   ───────────────────────────────────────────────────────────────────────────── */
export function startRibbon(canvas, accent) {
    if (!canvas) return false;
    stop(canvas);
    if (reduceMotion()) return false;

    const ctx = canvas.getContext('2d');
    if (!ctx) return false;

    const N = 96;
    const samples = new Float32Array(N);
    const state = { raf: 0, running: true, paused: false, head: 0, target: 0, current: 0, samples };

    const draw = () => {
        if (!state.running) return;
        state.raf = requestAnimationFrame(draw);
        if (state.paused) return;

        // Exponential approach — frame-rate independent enough at 60/120Hz for
        // a decorative strip, and it costs one multiply.
        state.current += (state.target - state.current) * 0.08;

        state.head = (state.head + 1) % N;
        samples[state.head] = state.current;

        const dpr = Math.min(window.devicePixelRatio || 1, 2);
        const w = Math.max(1, Math.floor(canvas.clientWidth * dpr));
        const h = Math.max(1, Math.floor(canvas.clientHeight * dpr));
        if (canvas.width !== w || canvas.height !== h) { canvas.width = w; canvas.height = h; }

        ctx.clearRect(0, 0, w, h);

        const step = w / (N - 1);
        const yOf = v => h - (v * (h - 4)) - 2;

        ctx.beginPath();
        ctx.moveTo(0, h);
        for (let i = 0; i < N; i++) {
            const v = samples[(state.head + 1 + i) % N];
            ctx.lineTo(i * step, yOf(v));
        }
        ctx.lineTo(w, h);
        ctx.closePath();

        const fill = ctx.createLinearGradient(0, 0, 0, h);
        fill.addColorStop(0, accent + '66');
        fill.addColorStop(1, accent + '00');
        ctx.fillStyle = fill;
        ctx.fill();

        ctx.beginPath();
        for (let i = 0; i < N; i++) {
            const v = samples[(state.head + 1 + i) % N];
            i === 0 ? ctx.moveTo(0, yOf(v)) : ctx.lineTo(i * step, yOf(v));
        }
        ctx.strokeStyle = accent;
        ctx.lineWidth = 1.5 * dpr;
        ctx.lineJoin = 'round';
        ctx.stroke();
    };

    state.raf = requestAnimationFrame(draw);
    state.onVis = () => { state.paused = document.hidden; };
    document.addEventListener('visibilitychange', state.onVis);
    handles.set(canvas, state);
    return true;
}

/** Feeds the ribbon a 0..1 sample. No-op if the canvas is not running. */
export function pushRibbon(canvas, value) {
    const state = handles.get(canvas);
    if (state) state.target = Math.max(0, Math.min(1, value));
}

/* ─────────────────────────────────────────────────────────────────────────────
   Public: stop — the counterpart every start above depends on.
   ───────────────────────────────────────────────────────────────────────────── */
export function stop(canvas) {
    const state = handles.get(canvas);
    if (!state) return;

    state.running = false;
    cancelAnimationFrame(state.raf);
    state.ro?.disconnect();
    if (state.onVis) document.removeEventListener('visibilitychange', state.onVis);

    // Explicitly drop the GL context. Browsers cap live contexts (~16 in Chrome)
    // and evict the oldest when the cap is hit — relying on GC to get there is
    // what makes "the backdrop stopped working after navigating around a while".
    if (state.gl) state.gl.getExtension('WEBGL_lose_context')?.loseContext();

    handles.delete(canvas);
}

/* ─────────────────────────────────────────────────────────────────────────────
   Public: view transitions.

   Blazor swaps @Body in place with no document navigation, so the browser's
   automatic cross-document transition never fires. This wraps a caller-supplied
   swap in startViewTransition instead.

   `updateCallback` here is Blazor's re-render, which we cannot await from JS —
   so the callback resolves on the next animation frame, which is after Blazor
   has flushed its render queue for the synchronous part of the navigation.
   ───────────────────────────────────────────────────────────────────────────── */
export function routeTransition() {
    if (!document.startViewTransition || reduceMotion()) return false;

    try {
        document.startViewTransition(() => new Promise(requestAnimationFrame));
        return true;
    } catch {
        // Overlapping transitions throw rather than queue. A skipped animation
        // is the correct outcome; the navigation itself must not be affected.
        return false;
    }
}
