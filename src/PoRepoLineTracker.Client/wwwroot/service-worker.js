// Development service worker — deliberately does nothing.
//
// The published build swaps this file for service-worker.published.js (see the <ServiceWorker>
// item in PoRepoLineTracker.Client.csproj). Caching during development is actively harmful: the
// dev server rebuilds the framework files on every change, and a worker serving yesterday's
// _framework/*.wasm from a cache is indistinguishable from a build that silently did not take.
//
// It still has to EXIST and register, because that is the only way to notice locally that the
// registration itself is broken — a worker that only appears in a published build is a worker
// whose failures only appear in production.
self.addEventListener('fetch', () => { });
