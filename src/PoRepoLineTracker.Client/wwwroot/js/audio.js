/* ═══════════════════════════════════════════════════════════════════════════
   audio.js — Web Audio feedback engine.

   Every sound in this file is SYNTHESISED at runtime and rendered once into an
   AudioBuffer. There are no .mp3/.ogg/.wav assets, which is not a stylistic
   choice:

     • Zero bytes over the wire. The whole cue set below is ~7KB of source that
       gzips into the JS bundle; the equivalent as eight optimised audio files
       is 40-80KB plus eight requests.
     • No CSP surface. The app's Content-Security-Policy has no `media-src`
       directive, so it inherits `default-src 'self'`. Synthesis never fetches a
       media URL, so there is nothing to allow.
     • No decode stall. decodeAudioData on first click is async and audibly late;
       a pre-rendered buffer starts on the same frame as the gesture.

   Autoplay policy: an AudioContext created before a user gesture starts in the
   'suspended' state and every scheduled sound is silently dropped. The context
   here is therefore created lazily on the first real interaction, and `unlock`
   resumes it from a genuine gesture handler.
   ═══════════════════════════════════════════════════════════════════════════ */

const PREF_KEY = 'audio-prefs';

let ctx = null;          // AudioContext, created on first gesture
let master = null;       // master gain → destination
let uiBus = null;        // one-shot cues
let ambientBus = null;   // background pad
let limiter = null;      // catches stacked cues clipping the output
const buffers = new Map();
let ambient = null;      // live ambient voice handle
let lastPlay = new Map();

const prefs = {
    enabled: false,      // OFF until the user opts in — see setEnabled
    volume: 0.5,
    ambient: false,
    ambientVolume: 0.18
};

/* ─── Preferences ────────────────────────────────────────────────────────────
   Persisted client-side rather than on the user preferences API: this is a
   per-device concern (the machine with speakers vs. the one in an office), and
   round-tripping it through the server would make a mute toggle wait on a
   network call. */
function load() {
    try {
        const raw = localStorage.getItem(PREF_KEY);
        if (raw) Object.assign(prefs, JSON.parse(raw));
    } catch { /* private mode / corrupt value — defaults are fine */ }
}

function save() {
    try { localStorage.setItem(PREF_KEY, JSON.stringify(prefs)); } catch { /* ignore */ }
}

load();

/* ─── Graph ──────────────────────────────────────────────────────────────────
                    ┌─ uiBus ──────┐
   sources ─────────┤              ├── limiter ── master ── destination
                    └─ ambientBus ─┘
   Two buses so the ambient pad has its own level independent of the cue level:
   the pad wants to sit near the noise floor while a click needs to be audible.
   The limiter is a DynamicsCompressor with a hard ratio — with a dozen repos
   finishing at once, a dozen chimes sum well past 0dBFS and clip. */
function ensureContext() {
    if (ctx) return ctx;

    const AC = window.AudioContext || window.webkitAudioContext;
    if (!AC) return null;

    ctx = new AC({ latencyHint: 'interactive' });

    limiter = ctx.createDynamicsCompressor();
    limiter.threshold.value = -8;
    limiter.knee.value = 0;
    limiter.ratio.value = 20;
    limiter.attack.value = 0.002;
    limiter.release.value = 0.15;

    master = ctx.createGain();
    master.gain.value = prefs.volume;

    uiBus = ctx.createGain();
    uiBus.gain.value = 1;

    ambientBus = ctx.createGain();
    ambientBus.gain.value = 0;

    uiBus.connect(limiter);
    ambientBus.connect(limiter);
    limiter.connect(master);
    master.connect(ctx.destination);

    return ctx;
}

/* ─── Synthesis helpers ───────────────────────────────────────────────────── */

const TAU = Math.PI * 2;

/** Equal-power-ish envelope. Exponential decay is what makes a tone read as a
 *  physical strike rather than a beep with the volume pulled down linearly. */
function env(t, dur, attack = 0.004, curve = 4) {
    if (t < attack) return t / attack;
    const p = (t - attack) / (dur - attack);
    return Math.exp(-curve * p) * (1 - p);
}

/** Renders one cue offline into an AudioBuffer and caches it.
 *  OfflineAudioContext rather than live nodes: a cue is a fixed waveform, so
 *  building the oscillator graph on every click is work repeated for an
 *  identical result. Rendered once, played thereafter as a buffer source. */
function render(name, seconds, fill) {
    if (buffers.has(name)) return buffers.get(name);

    const rate = ctx.sampleRate;
    const len = Math.max(1, Math.floor(seconds * rate));
    const buf = ctx.createBuffer(2, len, rate);
    const L = buf.getChannelData(0);
    const R = buf.getChannelData(1);

    for (let i = 0; i < len; i++) {
        const t = i / rate;
        const [l, r] = fill(t, i / len);
        L[i] = l;
        R[i] = r;
    }

    buffers.set(name, buf);
    return buf;
}

/** Detuned stereo pair. A few cents of spread between channels is what stops a
 *  pure tone sounding like it is coming from inside the user's head. */
function stereo(v, spread = 0.0) {
    return [v * (1 - spread), v * (1 + spread)];
}

/* ─── The cue set ─────────────────────────────────────────────────────────────
   Frequencies are picked off a pentatonic scale rooted at A4=440 so that cues
   overlapping in time (several repos finishing together) stay consonant rather
   than beating against each other. */

const CUES = {
    /* Short filtered click. Two partials plus a noise transient — the noise is
       what gives it "contact"; a pure sine reads as a notification, not a tap. */
    click: () => render('click', 0.045, t => {
        const e = env(t, 0.045, 0.001, 9);
        const tone = Math.sin(TAU * 1180 * t) * 0.5 + Math.sin(TAU * 2360 * t) * 0.18;
        const noise = (Math.random() * 2 - 1) * Math.exp(-t * 900) * 0.25;
        return stereo((tone + noise) * e * 0.35, 0.05);
    }),

    /* Toggle: a rising two-step for "on". Pitch movement is the cheapest way to
       encode direction without a second sample. */
    toggleOn: () => render('toggleOn', 0.13, t => {
        const f = t < 0.055 ? 660 : 880;
        const e = env(t, 0.13, 0.003, 6);
        return stereo(Math.sin(TAU * f * t) * e * 0.30, 0.08);
    }),

    toggleOff: () => render('toggleOff', 0.13, t => {
        const f = t < 0.055 ? 880 : 587;
        const e = env(t, 0.13, 0.003, 6);
        return stereo(Math.sin(TAU * f * t) * e * 0.28, 0.08);
    }),

    /* Navigation whoosh: filtered noise with a falling centre. Band-limited by
       summing two detuned sines under a noise bed rather than running a real
       filter, which offline rendering would make needlessly expensive. */
    nav: () => render('nav', 0.20, (t, p) => {
        const e = env(t, 0.20, 0.02, 3.5);
        const sweep = 1400 - 900 * p;
        const noise = (Math.random() * 2 - 1) * 0.4;
        const body = Math.sin(TAU * sweep * t) * 0.5;
        return stereo((noise * 0.35 + body) * e * 0.14, 0.35);
    }),

    /* Success: a major triad arpeggio, A-C#-E. Deliberately the longest cue —
       it marks a job that took minutes, and a 40ms blip undersells it. */
    success: () => render('success', 0.55, t => {
        const notes = [440, 554.37, 659.25];
        let v = 0;
        notes.forEach((f, i) => {
            const start = i * 0.075;
            if (t < start) return;
            const lt = t - start;
            v += Math.sin(TAU * f * lt) * env(lt, 0.55 - start, 0.006, 3.2) * 0.30;
            v += Math.sin(TAU * f * 2 * lt) * env(lt, 0.55 - start, 0.006, 5) * 0.08;
        });
        return stereo(v * 0.5, 0.12);
    }),

    /* Error: a minor second (a tritone's cheaper cousin) held briefly. Dissonant
       on purpose — it must not be mistakable for the success cue at low volume. */
    error: () => render('error', 0.38, t => {
        const e = env(t, 0.38, 0.005, 3.4);
        const v = Math.sin(TAU * 233.08 * t) * 0.55 + Math.sin(TAU * 246.94 * t) * 0.45;
        return stereo(v * e * 0.30, 0.06);
    }),

    /* Warning: single mid tone, two pulses. */
    warn: () => render('warn', 0.28, t => {
        const gate = (t < 0.09) || (t > 0.14 && t < 0.23) ? 1 : 0;
        const e = env(t, 0.28, 0.004, 2.2) * gate;
        return stereo(Math.sin(TAU * 493.88 * t) * e * 0.28, 0.05);
    }),

    /* Progress tick. Very quiet and very short — it fires per analysis frame, so
       anything with a tail would smear into a drone. */
    tick: () => render('tick', 0.028, t => {
        const e = env(t, 0.028, 0.001, 12);
        return stereo(Math.sin(TAU * 1760 * t) * e * 0.10, 0.02);
    }),

    /* Job start: low-to-high blip signalling work beginning. */
    start: () => render('start', 0.16, (t, p) => {
        const e = env(t, 0.16, 0.004, 5);
        const f = 330 + 330 * p;
        return stereo(Math.sin(TAU * f * t) * e * 0.24, 0.1);
    })
};

/* ─── Playback ───────────────────────────────────────────────────────────── */

/** Rate-limits a cue name. Without this the analysis feed's per-commit frames
 *  fire `tick` hundreds of times a second and the result is a buzz, not
 *  feedback — and each one is a live source node the GC then has to reap. */
function throttled(name, ms) {
    const now = performance.now();
    const prev = lastPlay.get(name) ?? -Infinity;
    if (now - prev < ms) return false;
    lastPlay.set(name, now);
    return true;
}

const THROTTLE = { tick: 110, click: 40, nav: 160, default: 60 };

export function play(name, gain = 1) {
    if (!prefs.enabled) return;
    if (!CUES[name]) return;
    if (!ensureContext()) return;

    // Still suspended means no gesture has reached us yet; scheduling here would
    // be silently discarded, so skip rather than pretend.
    if (ctx.state === 'suspended') return;

    if (!throttled(name, THROTTLE[name] ?? THROTTLE.default)) return;

    const buf = CUES[name]();
    const src = ctx.createBufferSource();
    src.buffer = buf;

    const g = ctx.createGain();
    g.gain.value = gain;

    src.connect(g);
    g.connect(uiBus);
    src.start();

    // Sources are single-use; disconnecting on end lets the node graph shrink
    // instead of accumulating one dead branch per click for the session.
    src.onended = () => { try { src.disconnect(); g.disconnect(); } catch { /* already gone */ } };
}

/* ─── Ambient bed ────────────────────────────────────────────────────────────
   Two detuned sawtooth-ish voices a fifth apart through a lowpass, plus a slow
   LFO on the filter. Generated live rather than looped from a buffer: a looped
   pad has an audible seam every cycle, and at these frequencies the oscillator
   cost is negligible.

   OFF by default and gated behind an explicit opt-in. A dashboard that starts
   humming on load is a bug report, not a feature — the browser's own autoplay
   policy exists because of exactly that pattern. */
export function setAmbient(on) {
    prefs.ambient = !!on;
    save();

    if (!on) {
        stopAmbient();
        return;
    }

    if (!prefs.enabled || !ensureContext() || ctx.state === 'suspended') return;
    if (ambient) return;

    const now = ctx.currentTime;

    const filter = ctx.createBiquadFilter();
    filter.type = 'lowpass';
    filter.frequency.value = 420;
    filter.Q.value = 0.8;

    const lfo = ctx.createOscillator();
    const lfoGain = ctx.createGain();
    lfo.frequency.value = 0.05;   // ~20s cycle
    lfoGain.gain.value = 160;
    lfo.connect(lfoGain);
    lfoGain.connect(filter.frequency);

    // A2 and E3 — a bare fifth, no third, so the bed stays modal and does not
    // imply a key the success cue's major triad would then contradict.
    const voices = [110, 164.81, 220.5].map((f, i) => {
        const o = ctx.createOscillator();
        o.type = i === 2 ? 'sine' : 'sawtooth';
        o.frequency.value = f;
        o.detune.value = (i - 1) * 6;
        const g = ctx.createGain();
        g.gain.value = i === 2 ? 0.12 : 0.22;
        o.connect(g);
        g.connect(filter);
        return o;
    });

    filter.connect(ambientBus);

    // Ramp in over 2.5s. An ambient bed that starts at full level is a startle,
    // which is the opposite of what it is for.
    ambientBus.gain.cancelScheduledValues(now);
    ambientBus.gain.setValueAtTime(0, now);
    ambientBus.gain.linearRampToValueAtTime(prefs.ambientVolume, now + 2.5);

    voices.forEach(o => o.start());
    lfo.start();

    ambient = { voices, lfo, lfoGain, filter };
}

function stopAmbient() {
    if (!ambient || !ctx) return;

    const now = ctx.currentTime;
    const handle = ambient;
    ambient = null;

    ambientBus.gain.cancelScheduledValues(now);
    ambientBus.gain.setValueAtTime(ambientBus.gain.value, now);
    ambientBus.gain.linearRampToValueAtTime(0, now + 1.2);

    // Stop AFTER the fade completes — stopping immediately produces a click as
    // the waveform is truncated mid-cycle.
    const stopAt = now + 1.3;
    handle.voices.forEach(o => { try { o.stop(stopAt); } catch { /* already stopped */ } });
    try { handle.lfo.stop(stopAt); } catch { /* already stopped */ }

    setTimeout(() => {
        try {
            handle.voices.forEach(o => o.disconnect());
            handle.lfo.disconnect();
            handle.lfoGain.disconnect();
            handle.filter.disconnect();
        } catch { /* already torn down */ }
    }, 1500);
}

/* ─── Global delegated cues ──────────────────────────────────────────────────
   One capture-phase listener on the document rather than a handler wired into
   every component. Three concrete reasons:

     • Coverage. Radzen renders its own buttons, dialogs and grid rows; there is
       no Blazor markup on most of them to hang an @onclick off, so per-component
       wiring would silently miss most of the app's clickable surface.
     • Unlock. The autoplay policy only accepts a resume from inside a real
       gesture handler. A delegated pointerdown IS that gesture, so the first
       click both unlocks the context and plays its own cue.
     • Cost. One listener, versus an interop round trip per component per click.

   Capture phase specifically: Radzen and Blazor both call stopPropagation in
   places, which would eat a bubble-phase listener on exactly the controls that
   most need feedback.
   ───────────────────────────────────────────────────────────────────────────── */

let installed = false;

/* What counts as "interactive". Matching on role as well as tag catches Radzen's
   div-based menu items and grid rows, which are not <button> elements. */
const INTERACTIVE = 'button, a[href], [role="button"], [role="menuitem"], [role="tab"], ' +
                    'input[type="checkbox"], input[type="radio"], summary, .rz-button, ' +
                    '.rz-navigation-item-text, .rz-switch, .rz-listbox-item';

export function installGlobalCues() {
    if (installed) return;
    installed = true;

    document.addEventListener('pointerdown', ev => {
        // Unlock unconditionally on ANY pointer press, even one we will not play
        // a cue for — otherwise a user whose first interaction is scrolling or
        // selecting text has a dead context when they later hit a button.
        if (prefs.enabled) unlock();

        if (!prefs.enabled) return;
        if (ev.button !== 0) return;   // right/middle click opens menus; not a cue

        const el = ev.target?.closest?.(INTERACTIVE);
        if (!el) return;
        if (el.disabled || el.getAttribute('aria-disabled') === 'true') return;

        // Switches and checkboxes get directional feedback. `aria-checked` is read
        // BEFORE the click flips it, so the value here is the pre-toggle state and
        // the cue must describe where it is going, not where it was.
        const checked = el.getAttribute('aria-checked') ?? (el.checked === true ? 'true' : null);
        if (checked !== null) {
            play(checked === 'true' ? 'toggleOff' : 'toggleOn');
            return;
        }

        play('click');
    }, { capture: true, passive: true });

    // Keyboard activation produces no pointerdown. Without this branch the app is
    // audibly dead for anyone navigating by keyboard — which is the group most
    // likely to want non-visual feedback in the first place.
    document.addEventListener('keydown', ev => {
        if (!prefs.enabled) return;
        if (ev.key !== 'Enter' && ev.key !== ' ') return;
        if (ev.repeat) return;

        const el = document.activeElement;
        if (!el?.matches?.(INTERACTIVE)) return;
        if (el.disabled || el.getAttribute('aria-disabled') === 'true') return;

        unlock();
        play('click');
    }, { capture: true, passive: true });
}

/* ─── Control surface ────────────────────────────────────────────────────── */

/** Called from a real user gesture. Creates the context if needed and resumes
 *  it — both are required, and only from inside a gesture handler. */
export function unlock() {
    if (!prefs.enabled) return false;
    if (!ensureContext()) return false;

    if (ctx.state === 'suspended') ctx.resume().catch(() => { /* denied; stays muted */ });

    // Deferred: the ambient bed cannot start before the context is running, so
    // an ambient=true preference restored on load is applied at unlock instead.
    if (prefs.ambient && !ambient) setAmbient(true);

    return ctx.state !== 'suspended';
}

export function setEnabled(on) {
    prefs.enabled = !!on;
    save();

    if (!on) {
        stopAmbient();
        // suspend() rather than close(): the context can be resumed on re-enable,
        // whereas a closed context can never be reused and a new one would have
        // to re-render every buffer.
        if (ctx && ctx.state === 'running') ctx.suspend().catch(() => {});
    } else if (ctx && ctx.state === 'suspended') {
        ctx.resume().catch(() => {});
    }
}

export function setVolume(v) {
    prefs.volume = Math.max(0, Math.min(1, v));
    save();
    if (master && ctx) {
        // A ramp, not an assignment: setting `.value` directly on a live gain
        // steps the signal and produces an audible zip.
        master.gain.setTargetAtTime(prefs.volume, ctx.currentTime, 0.02);
    }
}

export function setAmbientVolume(v) {
    prefs.ambientVolume = Math.max(0, Math.min(1, v));
    save();
    if (ambientBus && ctx && ambient) {
        ambientBus.gain.setTargetAtTime(prefs.ambientVolume, ctx.currentTime, 0.05);
    }
}

/* Individual primitive getters rather than one object snapshot.
   Blazor's JS interop serialises return values with the JSRuntime's own
   JsonSerializerOptions, which is a REFLECTION-based resolver and is not the
   source-generated AppJsonSerializerContext the HTTP layer uses. Returning an
   object here would therefore reintroduce reflection-driven serialisation into
   a project that is published trimmed specifically to have none — the exact
   failure mode (CtorNotLocated at runtime, nothing at build time) documented in
   .Client.csproj. Primitives are handled by the interop layer without any
   resolver at all. */
export function isSupported() { return !!(window.AudioContext || window.webkitAudioContext); }
export function isRunning() { return ctx?.state === 'running'; }
export function getEnabled() { return prefs.enabled; }
export function getVolume() { return prefs.volume; }
export function getAmbient() { return prefs.ambient; }
export function getAmbientVolume() { return prefs.ambientVolume; }

export function dispose() {
    stopAmbient();
    if (ctx) { ctx.close().catch(() => {}); ctx = null; }
    buffers.clear();
    lastPlay = new Map();
}
