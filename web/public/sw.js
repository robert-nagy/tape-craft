// Shows an offline page when the server cannot be reached. Nothing the app
// serves is cached apart from that page and its assets.
const PREFIX = 'tape-craft-';
// Raise the version when the precached files change.
const CACHE = `${PREFIX}offline-v1`;
const OFFLINE_PAGE = '/offline.html';
const PRECACHE = [OFFLINE_PAGE, '/styles.css', '/icons/icon-192.png'];

async function install() {
  const cache = await caches.open(CACHE);
  await cache.addAll(PRECACHE.map((path) => new Request(path, { cache: 'reload' })));
  await self.skipWaiting();
}

async function activate() {
  const names = await caches.keys();
  await Promise.all(
    names.filter((name) => name.startsWith(PREFIX) && name !== CACHE).map((name) => caches.delete(name)),
  );
  await self.clients.claim();
}

async function networkFirst(request, fallback) {
  try {
    return await fetch(request);
  } catch {
    const cache = await caches.open(CACHE);
    return (
      (await cache.match(fallback)) ??
      new Response('The tape-craft server cannot be reached.', {
        status: 503,
        headers: { 'content-type': 'text/plain; charset=utf-8' },
      })
    );
  }
}

self.addEventListener('install', (event) => event.waitUntil(install()));
self.addEventListener('activate', (event) => event.waitUntil(activate()));

self.addEventListener('fetch', (event) => {
  const { request } = event;
  const url = new URL(request.url);
  if (request.method !== 'GET' || url.origin !== self.location.origin) return;

  if (request.mode === 'navigate') {
    event.respondWith(networkFirst(request, OFFLINE_PAGE));
  } else if (PRECACHE.includes(url.pathname)) {
    event.respondWith(networkFirst(request, url.pathname));
  }
});
