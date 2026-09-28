# PWA de despacho LaVomVa

Aplicación del despachador para iniciar sesión, leer QR con la cámara, validar tickets y registrar el despacho con confirmación de identidad. La app está conectada a la API (`USE_MOCK = false`); `mockData.js` queda como dato de desarrollo y no se activa en el flujo habitual.

## Desarrollo

Desde la raíz del repositorio, instala el workspace y arranca la API en `http://localhost:5007`. Inicia la PWA con:

```bash
npm --prefix APPMOBILE ci
npm --prefix APPMOBILE run dev -- --host 127.0.0.1 --port 5174
```

Vite reenvía `/api` al backend. Para cambiar el destino local usa `VITE_API_PROXY_TARGET`; esa variable configura el proxy del servidor y no debe contener secretos. Para acceder a la cámara desde un dispositivo físico, sirve la app por HTTPS; los navegadores permiten cámara sobre `localhost` durante desarrollo.

## Pruebas

```bash
npm --prefix APPMOBILE test
npm --prefix APPMOBILE run build
```

Desde la raíz, `pnpm test:e2e` ejecuta Playwright con Chromium, API/PWA locales y PostgreSQL temporal. Crea tickets en la base de prueba, obtiene PNG QR de la API y los reproduce mediante cámara sintética para cubrir login, despacho e inventario, permisos, QR inválido/vencido/consumido, conflicto de stock y caída de red. El clúster temporal y los procesos se eliminan al terminar. Instala Chromium una vez con `pnpm exec playwright install chromium`.

## Sesión y caché

La PWA conserva access y refresh tokens en `localStorage`; cualquier JavaScript ejecutado en el mismo origen puede leerlos, por lo que se mantiene el riesgo de XSS documentado. El service worker cachea los documentos estáticos y siempre envía requests `/api`, requests con método distinto de GET y requests a otro origen directamente a la red. No almacena respuestas privadas de la API en Cache API.

La configuración de secretos, TLS, reverse proxy y cámara HTTPS está documentada en el [README principal](../README.md#configuración-segura-fuera-de-development). Nunca guardes JWT/HMAC, contraseñas de proveedores, tokens ni certificados privados en variables `VITE_*` o archivos versionados.
