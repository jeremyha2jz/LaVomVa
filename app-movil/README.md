# despacho-app — App Móvil/PWA del Despachador

Parte de **Persona 4** dentro del proyecto grupal `reto-tendencias` (Tendencias en Software). Esta app es la herramienta que usa el despachador en la estación de combustible, desde un celular, para escanear el QR de un ticket, validarlo y registrar el despacho de galones.

> 📌 **Si eres Persona 2 (Backend) o estás usando una IA para ayudarte con el backend**: pégale este archivo completo a tu asistente y dile que implemente los 4 endpoints descritos en la sección "Contrato con el backend". Esa sección tiene exactamente los JSON de entrada/salida que esta app ya está esperando — no hace falta adivinar nada.

---

## Estado del proyecto

✅ Funcionalidad completa (login, escaneo QR, validación, registro de despacho, consulta de tickets, PWA instalable) — probada en celular real.
🔧 Pendiente: pulido visual (CSS) — no afecta la lógica ni el contrato de datos.
🔌 Backend: la app corre 100% con **datos simulados (mock)** por ahora. En cuanto el backend esté listo, se conecta cambiando **una sola variable** (ver abajo).

---

## Stack técnico

- **Vanilla JavaScript + Vite** (sin framework — cada "página" es una función que pinta HTML en un contenedor)
- **PWA**: `manifest.json` + `sw.js` (service worker), instalable en celular
- **html5-qrcode**: librería para leer códigos QR desde la cámara

## Cómo correr el proyecto

```bash
npm install
npm run dev
```

Abre `http://localhost:5173`. Usuario de prueba (mock): `despachador1` / `1234`.

## Estructura de archivos

```
despacho-app/
├── index.html
├── manifest.json          # config de PWA (nombre, iconos, colores)
├── vite.config.js
├── package.json
├── public/
│   ├── sw.js               # service worker (cache offline)
│   └── icons/               # icon-192.png, icon-512.png
└── src/
    ├── main.js              # punto de entrada, decide qué pantalla mostrar
    ├── pages/
    │   ├── login.js
    │   ├── escaner.js        # lee QR con la cámara
    │   ├── ticket.js          # muestra el ticket validado
    │   ├── despacho.js        # formulario de galones servidos
    │   └── consultaTickets.js
    ├── services/
    │   ├── ticketService.js   # ⭐ ÚNICA capa que habla con el backend
    │   └── mockData.js        # datos falsos usados mientras USE_MOCK = true
    └── styles/
        └── main.css
```

---

## Contrato con el backend

Todo lo que esta app necesita del backend pasa por **4 funciones** en `src/services/ticketService.js`. Ahora mismo esas funciones devuelven datos simulados (`mockData.js`). Cuando el backend esté listo, solo hay que:

1. Cambiar `const USE_MOCK = true;` a `false` en `ticketService.js`.
2. Poner la URL real en `const API_URL = "https://tu-backend-aqui.com/api";`.

El resto de la app (todas las páginas) **no cambia ni una línea** — solo consume estas 4 funciones, nunca llama al backend directamente.

### 1. Login

**`POST {API_URL}/login`**

Request:
```json
{
  "usuario": "string",
  "contrasena": "string"
}
```

Response (200 OK):
```json
{
  "token": "jwt-de-verdad-aqui",
  "nombre": "Juan Pérez",
  "rol": "Despachador"
}
```

Si usuario/contraseña son incorrectos, la app espera un status de error (4xx) para mostrar el mensaje "Usuario o contraseña incorrectos".

El `token` se guarda en `localStorage` y se debería mandar en cada request futura como header `Authorization: Bearer <token>`.

### 2. Validar ticket (al escanear el QR)

**`POST {API_URL}/tickets/validar`**

Request — el string crudo que lee la cámara del QR:
```json
{
  "qrData": "string-o-json-codificado-en-el-QR"
}
```

Response (siempre 200, el resultado se indica con el campo `valido`):
```json
{
  "valido": true,
  "estado": "Creado",
  "ticket": {
    "id": "COM-2026-000001",
    "empleado": { "codigo": "E001", "nombre": "Juan Pérez" },
    "vehiculo": { "placa": "A123456", "ficha": "V01" },
    "departamento": "Logística",
    "cantidadAutorizada": 20,
    "tipoCombustible": "Gasolina Premium",
    "fechaEmision": "2026-09-01",
    "fechaVencimiento": "2026-09-30"
  },
  "mensajeError": null
}
```

Caso inválido (ticket vencido, ya usado, QR corrupto, etc.):
```json
{
  "valido": false,
  "estado": "Vencido",
  "ticket": null,
  "mensajeError": "El ticket ya venció"
}
```

Valores esperados para `estado`: `"Creado"` / `"Pendiente"`, `"Vencido"`, `"Consumido"`, `"Anulado"`. La app rechaza el despacho en cualquier estado que no sea válido, con el `mensajeError` mostrado tal cual al usuario.

### 3. Registrar despacho

**`POST {API_URL}/despachos`**

Request:
```json
{
  "ticketId": "COM-2026-000001",
  "galonesServidos": 18.5,
  "observaciones": "string opcional"
}
```

Response:
```json
{
  "ok": true,
  "mensaje": "Despacho registrado"
}
```

Notas de validación que ya hace el frontend (pero deben repetirse en el backend, porque el frontend nunca es garantía de seguridad):
- `galonesServidos` debe ser mayor a 0.
- `galonesServidos` no puede exceder la `cantidadAutorizada` del ticket.

### 4. Consultar tickets

**`GET {API_URL}/tickets`**

Response — lista (sin body de request):
```json
[
  {
    "id": "COM-2026-000001",
    "estado": "Pendiente",
    "vehiculo": "A123456",
    "cantidadAutorizada": 20,
    "fechaVencimiento": "2026-09-30"
  }
]
```

---

## Notas importantes para el backend

- **Autenticación**: una vez el login devuelva un token real, todos los demás endpoints deberían validar el header `Authorization: Bearer <token>` y rechazar con 401 si falta o es inválido.
- **El QR** solo trae un identificador/string (o JSON firmado) del ticket — es el backend quien decodifica, verifica hash/firma y busca los datos reales. El frontend nunca genera ni valida el QR por su cuenta, solo lo lee y lo reenvía tal cual llega de la cámara.
- **Los campos de fecha** se están manejando como string `"YYYY-MM-DD"` en el mock — si el backend real usa otro formato (ISO con hora, timestamp, etc.), avisar para ajustar el frontend.
- El frontend **no toca base de datos ni lógica de negocio** — solo consume esta API. Cualquier cambio a la forma de estos JSON debe coordinarse antes de implementarlo, porque rompe el contrato de arriba.

## Progressive Web App (PWA)

- `manifest.json` permite "instalar" la app desde el navegador.
- `sw.js` cachea `index.html` y sirve la app aunque no haya internet (aunque las llamadas al backend sí necesitan conexión).
- Recuerda: el service worker cachea agresivamente. Si haces cambios a `sw.js` y no se reflejan, en DevTools → Application → Service Workers, marca "Update on reload" o dale "Unregister" manualmente.

## Pendientes conocidos

- [ ] Estilo visual (CSS) de todas las pantallas — en progreso.
- [ ] Conexión real al backend (cambiar `USE_MOCK` a `false`).
- [ ] HTTPS real para producción (para que la cámara funcione fuera de `localhost`; en desarrollo se usa un túnel de ngrok).
