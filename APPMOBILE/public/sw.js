const CACHE_NAME = "despacho-cache-v2";
const ARCHIVOS_CACHE = ["/", "/index.html"];

self.addEventListener("install", (event) => {
  event.waitUntil(
    caches.open(CACHE_NAME).then((cache) => cache.addAll(ARCHIVOS_CACHE))
  );
  self.skipWaiting();
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches.keys().then((names) => Promise.all(names.filter((name) => name !== CACHE_NAME).map((name) => caches.delete(name))))
  );
  self.clients.claim();
});

self.addEventListener("fetch", (event) => {
  const request = event.request;
  const requestUrl = new URL(request.url);

  // API data, credentials, and any non-GET request must always go to the network.
  // The PWA is served from the same origin as /api through Vite or the production proxy.
  if (request.method !== "GET" || requestUrl.origin !== self.location.origin || requestUrl.pathname === "/api" || requestUrl.pathname.startsWith("/api/")) {
    event.respondWith(fetch(request));
    return;
  }

  event.respondWith(
    caches.match(request).then((respuestaCacheada) => {
      return respuestaCacheada || fetch(request);
    })
  );
});
