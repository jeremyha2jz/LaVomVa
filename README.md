# LaVomVa — Tickets Digitales e Inventario de Combustible

Sistema para controlar el despacho e inventario de combustible mediante tickets digitales con código QR. Se compone de una web administrativa en React, una API REST en ASP.NET Core 8, una aplicación móvil PWA para el despachador y una base de datos PostgreSQL con la numeración de tickets y el inventario resueltos en triggers. Se ejecuta en local, en Windows, sin Docker.

Se basa en el documento *SRS Plataforma Web y Aplicación Móvil para Gestión de Tickets Digitales e Inventario de Combustible* (v1.0, agosto 2026), proyecto académico de INTEC.

> Estado actual: desde la web se crean, aprueban y rechazan solicitudes, y al aprobar se emite el ticket con su QR. Además se consultan tickets, catálogos, inventario y movimientos, se registran recepciones de combustible y se exportan reportes a CSV. La app móvil valida el QR contra la API. **La API todavía no exige inicio de sesión ni aplica roles**, y **el despacho desde la app móvil está roto** tras los últimos cambios de la API (ver "Limitaciones y notas conocidas"). No debe exponerse en una red pública ni usarse con datos reales.

## Tecnologías

- .NET 8 y ASP.NET Core Web API (controladores)
- Entity Framework Core 8 y Npgsql
- PostgreSQL 14 o superior (probado con PostgreSQL 18) y pgAdmin 4
- JWT Bearer para la sesión y PBKDF2-SHA256 (100 000 iteraciones) para las contraseñas
- QRCoder para generar la imagen PNG del QR, y SHA-256 con secreto del servidor para su verificación
- Swagger / Swashbuckle para explorar y probar la API
- Web: React, TypeScript, Vite, lucide-react y pnpm
- App móvil: JavaScript sin framework, Vite, html5-qrcode y PWA (`manifest.json` + service worker)
- ngrok (opcional) para usar la cámara del celular por HTTPS

## Funcionalidades disponibles

**Web administrativa** (`src/`)

- Dos modos: demostración con datos locales en el navegador (predeterminado) y conectado a la API (`VITE_USE_MOCKS=false`), que exige iniciar sesión y usa solo datos de PostgreSQL.
- Resumen: inventario total, despachado hoy, tickets activos, solicitudes pendientes, consumo de los últimos 7 días, nivel por tanque y aviso de tanques por debajo del nivel crítico.
- Solicitudes: listado con búsqueda y filtro por estado, alta con empleado, vehículo, departamento, combustible, galones, vencimiento, tipo (manual, automática o recurrente) y motivo, y aprobación (con galones autorizados) o rechazo. Aprobar emite el ticket automáticamente.
- Tickets digitales: listado con búsqueda y filtro por estado, y detalle con la imagen del QR generada por la API.
- Inventario: tarjetas por tanque (existencia, capacidad, nivel crítico, ocupación), últimos movimientos y registro de recepciones de combustible.
- Recepciones y movimientos: historial filtrable por tipo y exportación a CSV.
- Reportes: filtros por fecha, departamento y combustible; totales por departamento y por combustible; exportación de tickets a CSV.
- Catálogos: consulta de empleados, vehículos y departamentos.
- Campana de notificaciones calculada en el navegador con los tanques en nivel crítico. También cuenta tickets vencidos o por vencer, pero la base nunca les asigna esos estados, así que en modo conectado no aparecen.

**API** (`backend/TicketsCombustible.Api/`)

- Inicio de sesión (`POST /api/login`) que devuelve un JWT de 8 horas, el id, el nombre y el rol del usuario.
- Creación y listado de usuarios, con un rol por usuario; seis roles precargados: Administrador, Supervisor, Despachador, Solicitante, Auditor y Consulta.
- Departamentos, empleados, vehículos, estaciones y tanques: crear, editar y desactivar (baja lógica) en `api/gestion`, y consulta de activos en `api/catalogos`.
- Solicitudes: crear (valida que empleado, vehículo, departamento y combustible existan y estén activos), aprobar y rechazar. Las fechas se aceptan en UTC (`...Z`), con desfase (`-04:00`) o sin zona.
- Tickets: emisión desde una solicitud aprobada con UUID, número `COM-AAAA-NNNNNN` (prefijo configurable, reinicio anual y sin duplicados gracias a un bloqueo de fila en la base), token aleatorio de 256 bits y hash SHA-256 con el secreto del servidor.
- Validación del QR (`POST /api/tickets/validar`): recalcula y compara el hash, e indica si el ticket es válido, vencido, consumido o anulado.
- Despacho: exige identidad confirmada, galones mayores que cero y no más de lo autorizado, tanque compatible e inventario suficiente. La base registra la salida y marca el ticket como consumido; un ticket no se puede despachar dos veces.
- Recepciones por proveedor y factura, con uno o varios tanques; ajustes positivos, negativos y mermas.
- Inventario por tanque y últimos 100 movimientos, con existencia anterior y nueva en cada uno. La base impide existencias negativas o por encima de la capacidad.

**App móvil del despachador** (`app-movil/`)

- Inicio de sesión, escaneo del QR con la cámara trasera, pantalla de ticket válido o inválido, formulario de despacho y lista de tickets.

## Guía paso a paso por requisito funcional

Los pasos de la web usan el modo conectado (`VITE_USE_MOCKS=false`) y la cuenta creada en "Primer usuario". Lo que la web aún no cubre se hace desde Swagger (`http://localhost:5007/swagger`).

**RF-01 — Usuarios.** Solo por Swagger: `POST /api/gestion/usuarios` con usuario, correo, nombre, contraseña y `rolId`; `GET /api/gestion/usuarios` lista las cuentas. La web no administra usuarios.

**RF-02, RF-03 y RF-04 — Empleados, vehículos y departamentos.** En la web, "Empleados y vehículos" los muestra. Para crearlos o editarlos usa Swagger: `POST /api/gestion/{tipo}`, `PUT /api/gestion/{tipo}/{id}` y `DELETE /api/gestion/{tipo}/{id}` (`tipo` = `departamentos`, `empleados`, `vehiculos`, `estaciones` o `tanques`).

**RF-05 — Solicitudes.** Web → "Solicitudes" → "Nueva solicitud": elige empleado, vehículo, departamento y combustible, indica galones, vencimiento, tipo y motivo, y registra. Queda en "PENDIENTE".

**RF-06, RF-07, RF-08 y RF-11 — Aprobación y emisión.** En "Solicitudes", abre una pendiente, ajusta los galones autorizados y pulsa "Aprobar y emitir ticket". El ticket aparece en "Tickets digitales"; al abrirlo se ve su QR.

**RF-10 — Estado del ticket.** "Tickets digitales" en la web o "Ver tickets" en la app móvil.

**RF-12 y RF-13 — Despacho.** El despacho se hace desde la app móvil escaneando el QR; la web no permite despachar por número. **Hoy falla**: la API exige `identidadConfirmada` y la app no la envía (ver "Limitaciones").

**RF-14 y RF-16 — Recepción.** Web → "Inventario" → "Registrar recepción": proveedor, factura, tanque, volumen y fecha. Los proveedores se crean por Swagger (`POST /api/recepciones/proveedores`).

**RF-14 — Ajustes y mermas.** Solo por Swagger: `POST /api/inventario/ajustes` con tanque, tipo (`AJUSTE_POSITIVO`, `AJUSTE_NEGATIVO` o `MERMA`), cantidad, motivo y usuario.

**RF-15 y RF-17 — Inventario y movimientos.** Web → "Inventario" y "Recepciones y movimientos".

**RF-19 y RF-20 — Reportes.** Web → "Reportes": filtra y pulsa "Exportar CSV".

**RF-22 — Tablero.** Web → "Resumen".

## Limitaciones y notas conocidas

El detalle requisito por requisito está en el documento de brechas frente al SRS. En resumen:

- **La API no exige autenticación.** Ningún endpoint tiene `[Authorize]`: sin token se pueden crear usuarios administradores, emitir tickets y registrar despachos. El JWT se emite pero no se exige, y los roles no se aplican. La web sí pide iniciar sesión, pero eso no protege la API.
- **El despacho no exige el QR.** `POST /api/despachos` acepta el número correlativo del ticket; la web lo oculta, pero la API lo sigue permitiendo.
- **La app móvil no puede despachar.** La API ahora rechaza el despacho con 400 si no llega `identidadConfirmada: true`, y la app no lo envía. Como la app espera JSON y el error llega en texto plano, el despachador solo ve "Error al registrar el despacho".
- **La lista de tickets de la app móvil está rota.** `GET /api/tickets` cambió de formato para la web: la app muestra el UUID en lugar del número, el estado en mayúsculas, "undefined gal" y la fecha completa.
- **La web y la app móvil usan el mismo puerto (5173).** Para usarlas a la vez, arranca la app móvil con `npm run dev -- --port 5174`.
- **Zonas horarias mezcladas.** Algunas fechas se guardan en UTC (vencimiento, aprobación, movimientos) y otras con la hora local del servidor de PostgreSQL (creación del ticket, fecha de solicitud). La web interpreta todas como hora local, así que las fechas en UTC se ven desplazadas.
- **Seguridad del QR incompleta.** El hash no incluye empleado, vehículo, cantidad ni fechas, y no hay firma digital (`qr_firma` queda vacía).
- **Estados sin uso.** No hay envío ni anulación de tickets, así que `ENVIADO`, `PROXIMO_A_VENCER` y `ANULADO` nunca se asignan.
- **Tablas sin uso.** Auditoría, notificaciones, cierres diarios y envíos de ticket existen en la base, pero nada escribe en ellas.
- **Sin cierre diario, auditoría, correo, SMS, PDF, exportación a Excel ni reportes del servidor.** Los reportes de la web se calculan en el navegador con los datos ya cargados.
- **Solicitudes automáticas y recurrentes:** se guarda el tipo, pero no hay programación que las genere.
- **Sin transferencias entre tanques:** la base las permite, pero no hay endpoint.
- **Web solo de consulta** en usuarios, catálogos y administración; las altas se hacen por Swagger.
- **Sin pruebas automatizadas.** La web declara Vitest, pero no tiene pruebas.
- **Versiones sin fijar.** `package.json` de la web usa `latest` en todas sus dependencias; `pnpm-lock.yaml` fija las versiones, así que instala con `pnpm install --frozen-lockfile`.
- **Caché de la app móvil.** El service worker sirve `index.html` desde caché; tras un cambio puede hacer falta "Update on reload" o "Unregister" en DevTools → Application → Service Workers.

## Requisitos

1. Windows 10 u 11.
2. .NET SDK 8 o 9 (el proyecto apunta a `net8.0`; hace falta el runtime ASP.NET Core 8).
3. Node.js 20.19 o superior, o 22.12 o superior.
4. pnpm para la web (`npm install -g pnpm`, o `corepack enable`).
5. PostgreSQL 14 o superior en ejecución (probado con 18), con la extensión `pgcrypto`.
6. Opcional: ngrok, para probar la cámara desde un celular real.

## Instalación paso a paso

### 1. Obtener el código

Clona o descarga el repositorio y ubícate en su raíz.

### 2. Crear la base de datos

Desde pgAdmin 4 o `psql`, crea la base y carga el script completo (esquema, triggers, vistas, roles y tipos de combustible):

```powershell
psql -U postgres -h localhost -c "CREATE DATABASE tickets_combustible;"
psql -U postgres -h localhost -d tickets_combustible -v ON_ERROR_STOP=1 -f DATABASE_FINALLL
```

Si `psql` no está en el PATH, usa la ruta completa, por ejemplo `"C:\Program Files\PostgreSQL\18\bin\psql.exe"`. El script deja 21 tablas y 5 vistas. No tiene sentencias `DROP`: ejecútalo una sola vez sobre una base vacía.

### 3. Crear la configuración privada de la API

```powershell
Copy-Item "backend\TicketsCombustible.Api\appsettings.Development.example.json" "backend\TicketsCombustible.Api\appsettings.Development.json"
notepad "backend\TicketsCombustible.Api\appsettings.Development.json"
```

En `appsettings.Development.json` reemplaza:

- `TU_CLAVE` por la contraseña local del usuario `postgres`.
- `Qr:SigningSecret` por un secreto aleatorio de **al menos 32 caracteres** (sin él la API no emite ni valida tickets). Si lo cambias después, los tickets ya emitidos dejan de validar.
- `Jwt:Key` por otro secreto aleatorio de al menos 32 caracteres (sin él la API no arranca).

El archivo está excluido del control de versiones. También se pueden usar variables de entorno: `ConnectionStrings__TicketsCombustible`, `Jwt__Key` y `Qr__SigningSecret`.

### 4. Arrancar la API

```powershell
dotnet run --project backend\TicketsCombustible.Api --launch-profile http
```

### 5. Instalar y arrancar la web

```powershell
Copy-Item ".env.example" ".env"
pnpm install --frozen-lockfile
pnpm dev
```

Con `.env` tal como viene (`VITE_USE_MOCKS=true`) la web usa datos de demostración y no necesita la API. Para conectarla, pon `VITE_USE_MOCKS=false` en `.env` y reinicia `pnpm dev`; el proxy de Vite envía `/api` a `http://localhost:5007`.

### 6. Arrancar la app móvil (opcional)

```powershell
cd app-movil
npm install
npm run dev -- --port 5174
```

Para usar un celular real, la cámara necesita HTTPS: expón la app con `ngrok http 5174` (`app-movil/vite.config.js` ya acepta los dominios de ngrok) y ábrela en el teléfono.

Servicios locales:

| Servicio | Dirección |
|---|---|
| Web administrativa | http://127.0.0.1:5173 |
| App móvil (PWA) | http://localhost:5174 |
| API | http://localhost:5007 |
| Swagger | http://localhost:5007/swagger |

## Primer usuario y datos de prueba

La base no trae usuarios ni una contraseña predeterminada. Mientras la API no exija autenticación, el primer usuario se crea desde Swagger:

```json
POST /api/gestion/usuarios
{
  "nombreUsuario": "supervisor",
  "correo": "supervisor@local.test",
  "nombreCompleto": "Supervisor de Prueba",
  "password": "Cambiar123",
  "rolId": 2
}
```

Los ids de rol son: 1 Administrador, 2 Supervisor, 3 Despachador, 4 Solicitante, 5 Auditor, 6 Consulta.

La web no crea catálogos, así que antes de registrar solicitudes crea por Swagger, en este orden: departamento, empleado, vehículo, estación, un tanque por tipo de combustible y un proveedor. El despacho elige el tanque automáticamente solo si hay exactamente uno compatible.

Estas credenciales son únicamente para desarrollo local.

## Desarrollo y comprobaciones

Compilar la API:

```powershell
dotnet build backend\TicketsCombustible.Api
```

Comprobar tipos y generar la web de producción (queda en `dist/`):

```powershell
pnpm build
pnpm preview
```

Generar la app móvil de producción (queda en `app-movil/dist`):

```powershell
cd app-movil
npm run build
```

La app móvil tiene un modo simulado: en `app-movil/src/services/ticketService.js`, `USE_MOCK = true` usa `mockData.js` (usuario `despachador1` / `1234`).

No hay pruebas automatizadas todavía.

## Estructura principal

```text
src/                               Web administrativa (React + TypeScript)
  pages/                           Resumen, solicitudes, tickets, despacho, inventario, movimientos, catálogos, reportes y administración
  services/api.ts                  Cliente de la API y adaptación de sus respuestas
  context/AppContext.tsx           Estado de la aplicación en modo demostración y conectado
  data/mock.ts                     Datos del modo demostración
backend/TicketsCombustible.Api/    API REST .NET 8
  Controllers/                     Login, catálogos, gestión, usuarios, solicitudes, tickets, despachos, inventario y recepciones
  Models/ y Data/                  Entidades y DbContext de Entity Framework
app-movil/                         PWA del despachador (Vite, JavaScript)
DATABASE_FINALLL                   Script de PostgreSQL: tablas, triggers, índices, datos iniciales y vistas
docs/INTEGRACION_BACKEND.md        Estado de la integración entre la web y la API
```

## Consideraciones

- El sistema se ejecuta completamente en local y no utiliza Docker.
- La numeración de tickets y todo el movimiento de inventario ocurren en la base mediante triggers: la API inserta el despacho, la recepción o el ajuste, y la base actualiza la existencia y el estado del ticket. No modifiques `tanques.existencia_actual_galones` a mano.
- Las bajas son lógicas (`activo = false`); tickets, despachos y movimientos no deben eliminarse físicamente.
- La web oculta opciones según el modo, pero la API no valida roles: la seguridad efectiva está pendiente.
- `appsettings.Development.json` y `.env` están excluidos de Git; revisa `git status` antes de subir cambios.
- Una compilación satisfactoria no implica que los módulos señalados como pendientes estén terminados.
