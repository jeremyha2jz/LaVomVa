const CACHE_NAME = "despacho-static-v2";
const ARCHIVOS_CACHE = ["/", "/index.html"];

self.addEventListener("install", (event) => {
  event.waitUntil(
    caches.open(CACHE_NAME)
      .then((cache) => cache.addAll(ARCHIVOS_CACHE))
      .then(() => self.skipWaiting())
  );
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches.keys()
      .then((keys) => Promise.all(keys
        .filter((key) => (key.startsWith("despacho-static-") || key === "despacho-cache-v1") && key !== CACHE_NAME)
        .map((key) => caches.delete(key))))
      .then(() => self.clients.claim())
  );
});

self.addEventListener("fetch", (event) => {
  const request = event.request;
  const url = new URL(request.url);
  const esApi = url.pathname === "/api" || url.pathname.startsWith("/api/");
  const esRecursoEstatico = request.mode === "navigate" || /\.(?:html|css|js|mjs|json|webmanifest|svg|png|jpe?g|webp|ico|woff2?)$/i.test(url.pathname);

  if (request.method !== "GET" || url.origin !== self.location.origin || esApi ||
      request.headers?.has("authorization") || request.cache === "no-store" || !esRecursoEstatico) {
    event.respondWith(fetch(request));
    return;
  }

  event.respondWith(
    fetch(request).then((respuesta) => {
      const cacheControl = respuesta.headers.get("cache-control") || "";
      if (respuesta.ok && respuesta.type === "basic" &&
          !/(?:no-store|private)/i.test(cacheControl) &&
          !request.headers?.has("authorization")) {
        const copia = respuesta.clone();
          event.waitUntil(
            caches.open(CACHE_NAME)
              .then((cache) => cache.put(request, copia))
              .catch(() => {})
          );
      }
      return respuesta;
    }).catch(async () => {
      const respuestaCacheada = await caches.match(request);
      if (respuestaCacheada) return respuestaCacheada;
      throw new Error("Recurso estático no disponible sin conexión");
    })
  );
});