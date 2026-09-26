# LaVomVa — Tickets Digitales e Inventario de Combustible

Sistema para controlar el despacho e inventario de combustible mediante tickets digitales con código QR. Se compone de una web administrativa en React, una API REST en ASP.NET Core 8, una aplicación móvil PWA para el despachador y una base de datos PostgreSQL con la numeración de tickets y el inventario resueltos en triggers. Se ejecuta en local, en Windows, sin Docker.

Se basa en el documento *SRS Plataforma Web y Aplicación Móvil para Gestión de Tickets Digitales e Inventario de Combustible* (v1.0, agosto 2026), proyecto académico de INTEC.

> Estado actual: desde la web se crean, aprueban y rechazan solicitudes, y al aprobar se emite el ticket con su QR. Además se consultan tickets, catálogos, inventario y movimientos, se registran recepciones de combustible y se exportan reportes a CSV. La app móvil valida QR y puede registrar despachos con confirmación explícita de identidad. La API exige inicio de sesión y aplica roles en operaciones protegidas. Esta versión sigue siendo académica; no la expongas en una red pública ni la uses con datos reales.

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

## Pruebas automatizadas

Con Node.js, pnpm, .NET SDK 8, Python 3 y los binarios de PostgreSQL (`initdb`, `pg_ctl`, `createdb`, `psql`) instalados, ejecuta desde la raíz:

```bash
pnpm install --frozen-lockfile
npm --prefix app-movil ci
pnpm test:all
```

El comando crea una instancia PostgreSQL temporal, carga `DATABASE_FINALLL`, ejecuta Vitest, las pruebas de contrato móvil, las pruebas de integración de API con cobertura y las compilaciones web/PWA/API. Al terminar, detiene y elimina esa instancia. Nunca apunta a una base configurada por el usuario. Para las suites individuales: `pnpm test`, `pnpm --dir app-movil test` y `dotnet test tests/TicketsCombustible.Api.Tests/TicketsCombustible.Api.Tests.csproj` (esta última requiere `QA_TEST_CONNECTION` hacia una base aislada con el esquema cargado).

## Funcionalidades disponibles

**Web administrativa** (`src/`)

- Sesión con inicio de sesión, registro público de cuentas de consulta sujetas a activación administrativa y datos cargados exclusivamente desde PostgreSQL.
- Resumen: inventario total, despachado hoy, tickets activos, solicitudes pendientes, consumo de los últimos 7 días, nivel por tanque y aviso de tanques por debajo del nivel crítico.
- Solicitudes: listado con búsqueda y filtro por estado, alta con empleado, vehículo, departamento, combustible, galones, vencimiento, tipo (manual, automática o recurrente) y motivo, y aprobación (con galones autorizados) o rechazo. Aprobar emite el ticket automáticamente.
- Tickets digitales: listado con búsqueda y filtro por estado; administradores y supervisores pueden ver la imagen del QR generada por la API.
- Inventario: tarjetas por tanque (existencia, capacidad, nivel crítico, ocupación), últimos movimientos y registro de recepciones de combustible.
- Recepciones y movimientos: historial filtrable por tipo y exportación a CSV.
- Reportes: filtros por fecha, departamento y combustible; totales por departamento y por combustible; exportación de tickets a CSV.
- Catálogos: consulta, creación, edición y desactivación de empleados, vehículos y departamentos según el rol.
- Campana de notificaciones calculada en el navegador con los tanques en nivel crítico. También cuenta tickets vencidos o por vencer, pero la base nunca les asigna esos estados, así que en modo conectado no aparecen.

**API** (`backend/TicketsCombustible.Api/`)

- Inicio de sesión (`POST /api/login`) que devuelve un JWT de 8 horas, el id, el nombre y el rol del usuario.
- Creación y listado de usuarios, con un rol por usuario; seis roles precargados: Administrador, Supervisor, Despachador, Solicitante, Auditor y Consulta.
- Departamentos, empleados, vehículos, estaciones y tanques: crear, editar y desactivar (baja lógica) en `api/gestion`, y consulta de activos en `api/catalogos`.
- Solicitudes: crear (valida que empleado, vehículo, departamento y combustible existan y estén activos), aprobar y rechazar. Las fechas se aceptan en UTC (`...Z`), con desfase (`-04:00`) o sin zona.
- Tickets: emisión desde una solicitud aprobada con UUID, número `COM-AAAA-NNNNNN` (prefijo configurable, reinicio anual y sin duplicados gracias a un bloqueo de fila en la base), token aleatorio de 256 bits y HMAC-SHA-256 del token y los datos protegidos del ticket.
- Validación del QR (`POST /api/tickets/validar`): recalcula y compara el hash, e indica si el ticket es válido, vencido, consumido o anulado.
- Despacho: exige identidad confirmada, galones mayores que cero y no más de lo autorizado, tanque compatible e inventario suficiente. La base registra la salida y marca el ticket como consumido; un ticket no se puede despachar dos veces.
- Recepciones por proveedor y factura, con uno o varios tanques; ajustes positivos, negativos y mermas.
- Inventario por tanque y últimos 100 movimientos, con existencia anterior y nueva en cada uno. La base impide existencias negativas o por encima de la capacidad.

**App móvil del despachador** (`app-movil/`)

- Inicio de sesión, escaneo del QR con la cámara trasera, pantalla de ticket válido o inválido, formulario de despacho y lista de tickets.

## Guía paso a paso por requisito funcional

Los pasos de la web requieren la API y una cuenta autenticada. Lo que la web aún no cubre se hace desde Swagger (`http://localhost:5007/swagger`).

**RF-01 — Usuarios.** La pantalla de acceso permite solicitar una cuenta CONSULTA. Un administrador puede activarla, crear otras cuentas, editar, desactivar y restablecer contraseñas desde "Administración".

**RF-02, RF-03 y RF-04 — Empleados, vehículos y departamentos.** En la web, "Empleados y vehículos" permite consultarlos y, con rol autorizado, crearlos, editarlos o desactivarlos. Las estaciones y los tanques se crean por API.

**RF-05 — Solicitudes.** Web → "Solicitudes" → "Nueva solicitud": elige empleado, vehículo, departamento y combustible, indica galones, vencimiento, tipo y motivo, y registra. Queda en "PENDIENTE".

**RF-06, RF-07, RF-08 y RF-11 — Aprobación y emisión.** Con rol supervisor o administrador, abre una solicitud pendiente, ajusta los galones autorizados y pulsa "Aprobar y emitir ticket". El ticket aparece en "Tickets digitales"; esos roles pueden ver su QR.

**RF-10 — Estado del ticket.** "Tickets digitales" en la web o "Ver tickets" en la app móvil.

**RF-12 y RF-13 — Despacho.** El despacho se hace desde la app móvil escaneando el QR; la web no permite despachar por número. Antes de enviar el despacho, el operador debe marcar que verificó la identidad del conductor.

**RF-14 y RF-16 — Recepción.** Crea proveedor, estación y tanque desde "Administración"; luego ve a "Inventario" → "Registrar recepción" e indica proveedor, factura, tanque, volumen y fecha.

**RF-14 — Ajustes y mermas.** Solo por Swagger: `POST /api/inventario/ajustes` con tanque, tipo (`AJUSTE_POSITIVO`, `AJUSTE_NEGATIVO` o `MERMA`), cantidad, motivo y usuario.

**RF-15 y RF-17 — Inventario y movimientos.** Web → "Inventario" y "Recepciones y movimientos".

**RF-19 y RF-20 — Reportes.** Web → "Reportes": filtra y pulsa "Exportar CSV".

**RF-22 — Tablero.** Web → "Resumen".

## Limitaciones y notas conocidas

El detalle requisito por requisito está en el documento de brechas frente al SRS. En resumen:

- **Autenticación y roles.** La API exige JWT en las rutas privadas y restringe las escrituras por rol. Todavía faltan políticas de alcance por usuario y auditoría completa.
- **Prueba de validación QR en memoria.** `POST /api/despachos` exige que la misma sesión haya validado recientemente el QR. Una instalación con varias instancias necesita un almacén compartido para esta prueba.
- **Escaneo QR desde cámara real.** La integración con cámara y permisos del navegador no tiene prueba E2E automatizada; se valida el contrato móvil mediante pruebas unitarias y el flujo servidor mediante integración.
- **La web y la app móvil usan el mismo puerto (5173).** Para usarlas a la vez, arranca la app móvil con `npm run dev -- --port 5174`.
- **Zonas horarias mezcladas.** Algunas fechas se guardan en UTC (vencimiento, aprobación, movimientos) y otras con la hora local del servidor de PostgreSQL (creación del ticket, fecha de solicitud). La web interpreta todas como hora local, así que las fechas en UTC se ven desplazadas.
- **Migración de firma QR.** Los QR existentes firmados con el formato anterior no validan con el HMAC nuevo; antes de desplegar sobre una base con tickets activos, hay que definir una reemisión controlada.
- **Estados sin uso.** No hay envío ni anulación de tickets, así que `ENVIADO`, `PROXIMO_A_VENCER` y `ANULADO` nunca se asignan.
- **Tablas sin uso.** Auditoría, notificaciones, cierres diarios y envíos de ticket existen en la base, pero nada escribe en ellas.
- **Sin cierre diario, auditoría, correo, SMS, PDF, exportación a Excel ni reportes del servidor.** Los reportes de la web se calculan en el navegador con los datos ya cargados.
- **Solicitudes automáticas y recurrentes:** se guarda el tipo, pero no hay programación que las genere.
- **Sin transferencias entre tanques:** la base las permite, pero no hay endpoint.
- **Catálogos adicionales:** estaciones, tanques y proveedores se crean desde "Administración". La edición y desactivación de esos catálogos aún requiere la API.
- **Cobertura automatizada parcial.** Existen pruebas Vitest para la web, pruebas de contrato móvil y pruebas de integración API/PostgreSQL; falta E2E de navegador, pruebas amplias de validación y auditoría funcional.
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

La web requiere la API y PostgreSQL. El proxy de Vite envía `/api` a `http://localhost:5007`.

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

## Primer administrador

La base no trae usuarios ni contraseñas predeterminadas. Configura `Bootstrap:Secret` con un secreto temporal aleatorio de al menos 32 caracteres y crea el primer administrador desde Swagger, una sola vez:

```json
POST /api/login/inicializar-admin
{
  "secreto": "EL_VALOR_PRIVADO_DE_BOOTSTRAP",
  "usuario": "admin",
  "correo": "admin@ejemplo.com",
  "nombreCompleto": "Administrador",
  "contrasena": "UNA_CONTRASEÑA_PRIVADA_DE_12_CARACTERES_O_MAS"
}
```

Después elimina `Bootstrap:Secret` de la configuración y reinicia la API. Entra a la web con esta cuenta. Las cuentas públicas nuevas reciben el rol CONSULTA y permanecen inactivas hasta que un administrador las revise y active desde "Administración".

Antes de registrar solicitudes, crea en la web un departamento, empleado y vehículo. En "Administración" puedes crear proveedor, estación y tanque. El despacho elige el tanque automáticamente solo si hay exactamente uno compatible.

No reutilices las contraseñas ni el secreto de arranque entre instalaciones.

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
  context/AppContext.tsx           Estado de la aplicación conectado a la API
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
- La API valida la sesión y los roles de escritura; todavía quedan brechas de seguridad descritas arriba.
- `appsettings.Development.json` y `.env` están excluidos de Git; revisa `git status` antes de subir cambios.
- Una compilación satisfactoria no implica que los módulos señalados como pendientes estén terminados.
