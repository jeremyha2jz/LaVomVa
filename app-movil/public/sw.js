const CACHE_NAME = "despacho-cache-v1";
const ARCHIVOS_CACHE = ["/", "/index.html"];

self.addEventListener("install", (event) => {
  event.waitUntil(
    caches.open(CACHE_NAME).then((cache) => cache.addAll(ARCHIVOS_CACHE))
  );
});

self.addEventListener("fetch", (event) => {
  event.respondWith(
    caches.match(event.request).then((respuestaCacheada) => {
      return respuestaCacheada || fetch(event.request);
    })
  );
});