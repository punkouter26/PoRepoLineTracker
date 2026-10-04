// Animated background: one fixed WebGL2 canvas behind the app shell.
//
// ON by default (the owner's choice; there is no settings switch). localStorage 'backdrop' = 'off'
// — set via window.setBackdrop(false) — is the only way to disable it. It does not start under
// prefers-reduced-motion or without WebGL2 — the app's ordinary CSS background stays exactly as
// it is, because the rules that let the canvas show through (app.css, `data-backdrop`) only apply
// once a first frame has actually been drawn. Nothing here is required by anything else: every
// entry point is wrapped, and a failure leaves the page as if this file had not loaded.
(function () {
    var canvas = null, gl = null, uniforms = null, observer = null, raf = 0, last = 0;

    // One triangle covering the viewport, generated from gl_VertexID — no buffers to manage.
    var VERTEX = '#version 300 es\n' +
        'void main(){gl_Position=vec4(gl_VertexID==1?3.:-1.,gl_VertexID==2?3.:-1.,0.,1.);}';

    // The page's own surface colour with two slow, wide tints of the brand colours drifting over
    // it. Capped at ~8% so the well reads as the same surface as before; cards are opaque and sit
    // above it. The last line is half a step of noise, which is what stops a gradient this
    // shallow from banding.
    var FRAGMENT = '#version 300 es\n' +
        'precision highp float;' +
        'uniform vec2 size;uniform float time;uniform vec3 base,tintA,tintB;out vec4 colour;' +
        'void main(){' +
        'vec2 p=gl_FragCoord.xy/size;' +
        'float a=.5+.5*sin(p.x*2.6+time*.11)*sin(p.y*2.1-time*.07);' +
        'float b=.5+.5*sin((p.x+p.y)*1.9-time*.09+1.7);' +
        'vec3 c=mix(mix(base,tintA,.08*a),tintB,.05*b);' +
        'c+=(fract(sin(dot(gl_FragCoord.xy,vec2(12.9898,78.233)))*43758.5453)-.5)/255.;' +
        'colour=vec4(c,1.);}';

    // Resolved through the canvas's own `color`, so any CSS colour syntax a token uses comes
    // back as rgb(r, g, b).
    function token(name) {
        canvas.style.color = 'var(' + name + ')';
        var parts = getComputedStyle(canvas).color.match(/[\d.]+/g) || [0, 0, 0];
        return [parts[0] / 255, parts[1] / 255, parts[2] / 255];
    }

    function readColours() {
        gl.uniform3fv(uniforms.base, token('--bg-secondary'));
        gl.uniform3fv(uniforms.tintA, token('--primary-color'));
        gl.uniform3fv(uniforms.tintB, token('--accent-secondary'));
    }

    function draw(now) {
        last = now;
        gl.uniform1f(uniforms.time, now / 1000);
        gl.drawArrays(gl.TRIANGLES, 0, 3);
    }

    // A backdrop this soft gains nothing from native resolution: capped at 1.5x.
    function resize() {
        var ratio = Math.min(window.devicePixelRatio || 1, 1.5);
        canvas.width = Math.round(window.innerWidth * ratio);
        canvas.height = Math.round(window.innerHeight * ratio);
        gl.viewport(0, 0, canvas.width, canvas.height);
        gl.uniform2f(uniforms.size, canvas.width, canvas.height);
        draw(last); // sizing clears the canvas; repaint now rather than show a blank frame
    }

    function frame(now) {
        raf = requestAnimationFrame(frame);
        if (now - last >= 33) draw(now); // ~30fps: the motion is too slow to need more
    }

    function onVisibility() {
        cancelAnimationFrame(raf);
        if (!document.hidden) raf = requestAnimationFrame(frame);
    }

    function compile(type, source) {
        var shader = gl.createShader(type);
        gl.shaderSource(shader, source);
        gl.compileShader(shader);
        return shader;
    }

    function stop() {
        cancelAnimationFrame(raf);
        window.removeEventListener('resize', resize);
        document.removeEventListener('visibilitychange', onVisibility);
        if (observer) observer.disconnect();
        document.documentElement.removeAttribute('data-backdrop');
        // Before losing the context on purpose: that raises the event too, later, and by then a
        // quick off-and-on would have it tear down the new canvas.
        if (canvas) canvas.removeEventListener('webglcontextlost', stop);
        if (gl) {
            // Browsers cap live WebGL contexts per page; dropping the canvas alone leaves this
            // one counted until garbage collection gets round to it.
            var lose = gl.getExtension('WEBGL_lose_context');
            if (lose) lose.loseContext();
        }
        if (canvas) canvas.remove();
        canvas = gl = uniforms = observer = null;
    }

    function start() {
        if (canvas) return;
        if (window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;
        try {
            canvas = document.createElement('canvas');
            canvas.id = 'backdrop';
            canvas.setAttribute('aria-hidden', 'true');
            gl = canvas.getContext('webgl2', { alpha: false, antialias: false, powerPreference: 'low-power' });
            if (!gl) { canvas = null; return; }

            var program = gl.createProgram();
            gl.attachShader(program, compile(gl.VERTEX_SHADER, VERTEX));
            gl.attachShader(program, compile(gl.FRAGMENT_SHADER, FRAGMENT));
            gl.linkProgram(program);
            if (!gl.getProgramParameter(program, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(program));
            gl.useProgram(program);

            uniforms = {};
            ['size', 'time', 'base', 'tintA', 'tintB'].forEach(function (name) {
                uniforms[name] = gl.getUniformLocation(program, name);
            });

            // In the document before the colours are read: var() only resolves on an attached element.
            document.body.prepend(canvas);
            readColours();
            resize();

            // A lost context (GPU reset, driver update) is not worth restoring for decoration.
            canvas.addEventListener('webglcontextlost', stop);
            window.addEventListener('resize', resize);
            document.addEventListener('visibilitychange', onVisibility);
            observer = new MutationObserver(function () { readColours(); draw(last); });
            observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });

            // Only now: this is what makes the shell's own backgrounds transparent.
            document.documentElement.setAttribute('data-backdrop', 'on');
            onVisibility();
        } catch (e) {
            stop();
        }
    }

    // Explicit choice (devtools / a future switch): apply it and remember it, like setTheme.
    window.setBackdrop = function (on) {
        try { localStorage.setItem('backdrop', on ? 'on' : 'off'); } catch (e) { }
        if (on) start(); else stop();
    };

    var off = false;
    try { off = localStorage.getItem('backdrop') === 'off'; } catch (e) { }
    if (!off) start();
})();
