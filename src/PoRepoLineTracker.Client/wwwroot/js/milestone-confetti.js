// Milestone confetti — a one-shot particle burst drawn on a full-viewport canvas that removes
// itself when the animation ends. No dependency, no persistent GfxService: the canvas element and
// its rAF loop live only as long as the burst does.
//
// Deliberately plain canvas 2D, not WebGL — this is a few dozen circles for ~1.1s, nowhere near
// where a GPU context would pay for itself, and it is the one thing this file needs to do.

export function burst() {
    if (window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
        return;
    }

    const canvas = document.createElement('canvas');
    canvas.style.position = 'fixed';
    canvas.style.inset = '0';
    canvas.style.width = '100vw';
    canvas.style.height = '100vh';
    canvas.style.pointerEvents = 'none';
    canvas.style.zIndex = '2000';
    document.body.appendChild(canvas);

    const dpr = window.devicePixelRatio || 1;
    const width = window.innerWidth;
    const height = window.innerHeight;
    canvas.width = width * dpr;
    canvas.height = height * dpr;

    const ctx = canvas.getContext('2d');
    if (!ctx) {
        canvas.remove();
        return;
    }
    ctx.scale(dpr, dpr);

    const colors = ['#6750A4', '#7C6CAF', '#0891b2', '#059669', '#d97706', '#f95d9b'];
    const count = 70;
    const particles = Array.from({ length: count }, () => ({
        x: width / 2,
        y: height * 0.28,
        // Wide spread with an upward-biased vertical component, then gravity takes over.
        vx: (Math.random() - 0.5) * 9,
        vy: -Math.random() * 7 - 2,
        size: Math.random() * 6 + 3,
        color: colors[Math.floor(Math.random() * colors.length)],
        rotation: Math.random() * Math.PI,
        spin: (Math.random() - 0.5) * 0.3,
    }));

    const gravity = 0.22;
    const durationMs = 1400;
    let start = null;

    function frame(timestamp) {
        if (start === null) start = timestamp;
        const elapsed = timestamp - start;
        const t = Math.min(elapsed / durationMs, 1);

        ctx.clearRect(0, 0, width, height);

        for (const p of particles) {
            p.vy += gravity;
            p.x += p.vx;
            p.y += p.vy;
            p.rotation += p.spin;

            ctx.save();
            ctx.globalAlpha = 1 - t;
            ctx.translate(p.x, p.y);
            ctx.rotate(p.rotation);
            ctx.fillStyle = p.color;
            ctx.fillRect(-p.size / 2, -p.size / 2, p.size, p.size * 0.6);
            ctx.restore();
        }

        if (t < 1) {
            requestAnimationFrame(frame);
        } else {
            canvas.remove();
        }
    }

    requestAnimationFrame(frame);
}
