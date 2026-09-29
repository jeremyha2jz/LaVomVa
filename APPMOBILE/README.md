# LaVomVa PWA

Aplicación móvil instalable para el despachador de combustible: valida tickets mediante QR, registra despachos y consulta tickets contra el backend LaVomVa.

## Stack

- Vite y JavaScript puro.
- `html5-qrcode` para lectura con cámara.
- PWA con manifest y service worker. Las operaciones con el backend necesitan conexión; no hay registro de despachos offline.

## Funcionalidades actuales

- Login real para DESPACHADOR, JWT, refresh y logout manual.
- Aviso a los 4 minutos de inactividad y logout a los 5 minutos. La actividad reinicia el contador.
- Sesión en `sessionStorage`, sin persistencia en `localStorage`: al terminar la sesión de pestaña/PWA se requiere login. Algunos navegadores pueden restaurar la sesión de pestaña al reabrirla; cerrar una ventana de PWA no siempre termina su proceso.
- Escaneo QR y validación real de tickets.
- Confirmación de identidad, consulta de tanques y registro real de despacho.
- Consulta real de tickets, filtros por estado, búsqueda por correlativo/empleado/vehículo y detalle por UUID.
- El detalle reutiliza los nombres del listado. No muestra observaciones del despacho porque no hay un GET compatible para recuperarlas.

Estados soportados: `CREADO`, `ENVIADO`, `PENDIENTE`, `PROXIMO_A_VENCER`, `VENCIDO`, `CONSUMIDO` y `ANULADO`.

El QR contiene un **token**, no el correlativo visible `COM-2026-...`. La entrada manual también espera el token exacto del QR.

## Desarrollo

Requisitos: Node.js compatible con Vite 8 (20.19+ o 22.12+), npm y backend LaVomVa funcionando. Para cámara/PWA en un celular utiliza HTTPS, por ejemplo mediante un túnel a Vite; `localhost` permite pruebas en el propio equipo.

Desde la raíz del repositorio:

```sh
cd app-movil
npm install
```

Copia `.env.example` a `.env.local` dentro de `app-movil` y conserva:

```dotenv
VITE_API_URL=/api
```

Inicia el backend en `http://localhost:5007` y ejecuta:

```sh
npm run dev
```

Abre la URL que imprime Vite. `vite.config.js` reenvía `/api` a `http://localhost:5007`, conservando la ruta: `/api/tickets` llega a `http://localhost:5007/api/tickets`. Este proxy se configura para desarrollo y no se incluye en el build. Permite usar un único túnel HTTPS hacia Vite desde el celular.

## Probar desde un celular con ngrok

Con ngrok instalado y configurado, el backend corriendo en `http://localhost:5007` y `VITE_API_URL=/api`:

1. Desde `app-movil`, levanta la PWA:

   ```sh
   npm run dev
   ```

2. Vite normalmente inicia en `http://localhost:5173`. Comprueba el puerto que indica la terminal.
3. En otra terminal, ejecuta:

   ```sh
   ngrok http 5173
   ```

4. Abre en el celular la URL **HTTPS** que proporciona ngrok.

Si Vite utiliza otro puerto, usa ese mismo puerto en el comando de ngrok. En desarrollo, Vite redirige `/api` hacia `http://localhost:5007`: no necesitas exponer el backend con otro túnel.

Mantén activas las tres terminales durante la prueba: backend, `npm run dev` y ngrok.

Si el navegador móvil carga una versión vieja, puede ser necesario limpiar los datos/caché del sitio o reinstalar la PWA.

## Entorno y producción

`src/services/apiConfig.js` lee `VITE_API_URL`, elimina barras finales y usa `/api` si no está definida. No necesitas modificar código para cambiar de backend.

Antes de compilar para producción, define la variable en el entorno de build o en un archivo local `.env.production` ignorado por Git:

```dotenv
VITE_API_URL=https://DOMINIO-DEL-BACKEND/api
```

Sustituye el dominio de ejemplo por el real. Las variables del proceso tienen prioridad sobre los archivos de entorno y `.env.production` tiene prioridad sobre `.env.local` para ese modo. Vite incorpora el valor al compilar: cambiarlo después requiere otro build. Reinicia Vite si cambias variables durante desarrollo.

```sh
npm run build
npm run preview
```

`preview` permite revisar localmente el build; no es un servidor de producción. Publica el contenido de `dist/` mediante HTTPS. El backend desplegado debe permitir CORS desde el dominio de la PWA, incluyendo los encabezados Authorization/Content-Type y los métodos utilizados. Si se conserva `/api` en producción, el alojamiento debe reenviar esa ruta al backend; el proxy de desarrollo no se despliega.

En PowerShell, si la política bloquea `npm.ps1`, usa `npm.cmd` en los comandos anteriores.

## Flujo

Login → Escanear QR → Validar ticket → Confirmar identidad → Seleccionar tanque → Registrar despacho → Consultar tickets.

## Notas para desarrollo

- Los flujos principales no utilizan mocks. Las peticiones protegidas reutilizan `fetchConSesion()` para JWT y refresh.
- «Mis tickets» muestra el listado entregado por la API, sin filtrar por despachador o estación. Búsqueda y filtros de estado se aplican en el cliente.
- `.env.local` no debe subirse a Git. `.env.example` es la plantilla compartida sin credenciales.
- Las variables `VITE_*` son públicas en el navegador. Nunca guardes contraseñas, JWT ni secretos QR/JWT/bootstrap en ellas o en el repositorio.
- `dist/` se genera mediante build y está ignorado; no se versiona. Tampoco se versionan `node_modules/` ni logs.
- `design-reference/` contiene referencias visuales; los flujos reales están en `src/`.
- El service worker almacena la página inicial en caché. Si ves una versión antigua al probar, revisa/desregistra el service worker y limpia su caché desde las herramientas del navegador.
