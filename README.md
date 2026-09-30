# LaVomVa — Tickets Digitales e Inventario de Combustible


## Estructura del repositorio

- `APPMOBILE/`: PWA del despachador, assets, pruebas unitarias y E2E.
- `APPWEB/`: aplicación web, componentes, pruebas, Vite y TypeScript.
- `BACKEND/`: API ASP.NET Core y pruebas .NET.
- `BASEDATOS/`: esquema PostgreSQL y migraciones SQL.
- `DOCUMENTACION/`: matrices SRS, QA y guías técnicas.
- `scripts/`: instalación y orquestación de pruebas.


Sistema para controlar el despacho e inventario de combustible mediante tickets digitales con código QR. Se compone de una web administrativa en React, una API REST en ASP.NET Core 8, una aplicación móvil PWA para el despachador y una base de datos PostgreSQL con la numeración de tickets y el inventario resueltos en triggers. Se ejecuta en local, en Windows, sin Docker.

Se basa en el documento *SRS Plataforma Web y Aplicación Móvil para Gestión de Tickets Digitales e Inventario de Combustible* (v1.0, agosto 2026), proyecto académico de INTEC.

> Estado actual: desde la web se crean, aprueban y rechazan solicitudes, y al aprobar se emite el ticket con su QR. También se consultan tickets, catálogos, inventario, movimientos y reportes con datos de PostgreSQL; los reportes se pueden exportar a CSV, Excel y PDF. La app móvil valida QR y puede registrar despachos con confirmación explícita de identidad. La API exige inicio de sesión y aplica roles en operaciones protegidas. Esta versión sigue siendo académica; no la expongas en una red pública ni la uses con datos reales.

## Tecnologías

- .NET 8 y ASP.NET Core Web API (controladores)
- Entity Framework Core 8 y Npgsql
- ClosedXML para exportar archivos XLSX
- PostgreSQL 14 o superior (probado con PostgreSQL 18) y pgAdmin 4
- JWT Bearer HS256 de 15 minutos y PBKDF2-SHA256 (100 000 iteraciones) para las contraseñas; refresh tokens rotatorios almacenados como hash SHA-256 en PostgreSQL
- SignalR autenticado para propagar cambios confirmados de inventario a las sesiones web conectadas
- QRCoder para generar la imagen PNG del QR, y SHA-256 con secreto del servidor para su verificación
- Swagger / Swashbuckle para explorar y probar la API
- Web: React, TypeScript, Vite, lucide-react y pnpm
- App móvil: JavaScript sin framework, Vite, html5-qrcode y PWA (`manifest.json` + service worker)
- ngrok (opcional) para usar la cámara del celular por HTTPS

## Pruebas automatizadas

Con Node.js, pnpm, .NET SDK 8, Python 3 y los binarios de PostgreSQL (`initdb`, `pg_ctl`, `createdb`, `psql`) instalados, ejecuta desde la raíz:

```bash
pnpm install --frozen-lockfile
npm --prefix APPMOBILE ci
pnpm test:all
```

El comando crea una instancia PostgreSQL temporal, carga `BASEDATOS/schema/DATABASE_FINALLL`, ejecuta Vitest, las pruebas PWA/service worker, el escáner local de secretos, las pruebas API con cobertura y las compilaciones web/PWA/API. Al terminar, detiene y elimina esa instancia. Nunca apunta a una base configurada por el usuario. Para validar la interfaz móvil real en Chromium instala una vez el navegador con `pnpm exec playwright install chromium` y ejecuta `pnpm test:e2e`; este comando crea su propio PostgreSQL temporal, arranca API/PWA en loopback y limpia la infraestructura al terminar. La cámara sintética reproduce en Chromium PNG de tickets emitidos por la API temporal. `pnpm test:e2e` queda separado de `pnpm test:all` porque necesita Chromium.

Comprobaciones individuales: `pnpm test`, `npm --prefix APPMOBILE test`, `pnpm test:security` y `dotnet test BACKEND/Tests/API/TicketsCombustible.Api.Tests.csproj` (esta última requiere `QA_TEST_CONNECTION` hacia una base aislada con el esquema y migraciones cargados).

## Funcionalidades disponibles

**Web administrativa** (`APPWEB/src/`)

- Sesión con inicio de sesión, registro público de cuentas de consulta sujetas a activación administrativa y datos cargados exclusivamente desde PostgreSQL.
- Resumen: inventario total, despachado hoy, tickets activos, solicitudes pendientes, consumo de los últimos 7 días, nivel por tanque y aviso de tanques por debajo del nivel crítico.
- Solicitudes: listado con búsqueda y filtro por estado, alta con empleado, vehículo, departamento, combustible, galones, vencimiento, tipo (manual, automática o recurrente) y motivo, y aprobación (con galones autorizados) o rechazo. Aprobar emite el ticket automáticamente.
- Tickets digitales: listado con búsqueda y filtro por estado; administradores y supervisores pueden ver el QR y anular tickets elegibles indicando el motivo.
- Inventario: tarjetas por tanque (existencia, capacidad, nivel crítico, ocupación), últimos movimientos y registro de recepciones. Existencias, movimientos y alertas se actualizan en vivo mediante SignalR después del commit de PostgreSQL.
- Recepciones y movimientos: historial filtrable por tipo y exportación a CSV.
- Reportes: consulta de consumo, tickets, despachos y movimientos desde la API; filtros por fechas UTC, departamento, combustible, empleado, vehículo, estado y estación; totales y agregaciones paginadas; descarga de CSV, Excel y PDF con los mismos filtros.
- Catálogos: consulta, creación, edición y desactivación de empleados, vehículos y departamentos según el rol.
- Campana de notificaciones persistidas, cargada mediante REST y sincronizada al reconectar. También cuenta tickets vencidos o por vencer, pero la base nunca les asigna esos estados, así que en modo conectado no aparecen.

**API** (`BACKEND/API/`)

- Inicio de sesión (`POST /api/login`) que devuelve un access JWT de 15 minutos, refresh token opaco, vencimiento, id, nombre y rol. El access JWT valida firma HS256, issuer, audience, expiración, usuario activo y roles actuales.
- Sesiones: `POST /api/login/refresh` rota el refresh token; `POST /api/login/logout` revoca la familia actual; `POST /api/login/logout-all` revoca todas las sesiones; `POST /api/login/cambiar-contrasena` cambia la clave y revoca todas las sesiones. Un reset administrativo, cambio de rol o desactivación también revoca refresh tokens. Si un refresh ya rotado se reutiliza, se revoca toda la familia. Los access JWT existentes pueden seguir utilizándose hasta su expiración (máximo 15 minutos) salvo que la cuenta se desactive o cambien sus roles, que se validan en cada solicitud.
- Creación y listado de usuarios, con un rol por usuario; seis roles precargados: Administrador, Supervisor, Despachador, Solicitante, Auditor y Consulta.
- Departamentos, empleados, vehículos, estaciones y tanques: crear, editar y desactivar (baja lógica) en `api/gestion`, y consulta de activos en `api/catalogos`.
- Solicitudes: crear (valida que empleado, vehículo, departamento y combustible existan y estén activos), aprobar y rechazar. Las fechas se aceptan en UTC (`...Z`), con desfase (`-04:00`) o sin zona.
- Tickets: emisión desde una solicitud aprobada con UUID, número `COM-AAAA-NNNNNN` (prefijo configurable, reinicio anual y sin duplicados gracias a un bloqueo de fila en la base), token aleatorio de 256 bits y HMAC-SHA-256 del token y los datos protegidos del ticket.
- Tickets: `POST /api/tickets/{id}/anular` permite a administradores y supervisores anular tickets con motivo. La fecha, actor y motivo quedan persistidos y auditados; los estados vencido/próximo a vencer se calculan al consultar. La ventana de próximo a vencer es de dos días.
- Validación del QR (`POST /api/tickets/validar`): recalcula y compara el hash; rechaza tickets vencidos, consumidos y anulados.
- Despacho: exige identidad confirmada, galones mayores que cero y no más de lo autorizado, tanque compatible e inventario suficiente. La base registra la salida y marca el ticket como consumido; un ticket no se puede despachar dos veces.
- Recepciones por proveedor y factura, con uno o varios tanques; ajustes positivos, negativos y mermas.
- Inventario por tanque y últimos 100 movimientos, con existencia anterior y nueva en cada uno. La base impide existencias negativas o por encima de la capacidad.
- Hub SignalR autenticado en `/hubs/inventory`: emite `InventoryUpdated`, `InventoryMovementCreated` y `CriticalInventoryChanged` usando el libro mayor persistido por PostgreSQL. Requiere el JWT de la sesión; solo se aceptan eventos de movimientos después del commit. Todos los roles autenticados pueden leer inventario en REST y, por tanto, pueden conectar al Hub.
- Reportes autenticados: `GET /api/reportes` acepta `tipo` (`consumo`, `tickets`, `despachos`, `movimientos`), `desde`, `hasta`, `departamentoId`, `combustibleId`, `empleadoId`, `vehiculoId`, `estado`, `estacionId`, `pagina` y `tamanoPagina`. `GET /api/reportes/exportar` acepta los mismos filtros, excepto paginación, y `formato=csv|xlsx|pdf`. Todos los roles autenticados pueden consultarlos; la fecha inicial/final es UTC e inclusiva. Exportación limitada a 10 000 filas por archivo.

**App móvil del despachador** (`APPMOBILE/`)

- Inicio de sesión, escaneo del QR con la cámara trasera, pantalla de ticket válido o inválido, formulario de despacho y lista de tickets.

## Guía paso a paso por requisito funcional

Los pasos de la web requieren la API y una cuenta autenticada. Lo que la web aún no cubre se hace desde Swagger (`http://localhost:5007/swagger`).

**RF-01 — Usuarios.** La pantalla de acceso permite solicitar una cuenta CONSULTA. Un administrador puede activarla, crear otras cuentas, editar, desactivar y restablecer contraseñas desde "Administración".

**RF-02, RF-03 y RF-04 — Empleados, vehículos y departamentos.** En la web, "Empleados y vehículos" permite consultarlos y, con rol autorizado, crearlos, editarlos o desactivarlos. Las estaciones y los tanques se crean por API.

**RF-05 — Solicitudes.** Web → "Solicitudes" → "Nueva solicitud": elige empleado, vehículo, departamento y combustible, indica galones, vencimiento, tipo y motivo, y registra. Queda en "PENDIENTE".

**RF-06, RF-07, RF-08 y RF-11 — Aprobación y emisión.** Con rol supervisor o administrador, abre una solicitud pendiente, ajusta los galones autorizados y pulsa "Aprobar y emitir ticket". El ticket aparece en "Tickets digitales"; esos roles pueden ver su QR.

**RF-10 — Estado del ticket.** "Tickets digitales" en la web o "Ver tickets" en la app móvil.

**RS-01 — Sesión.** Web y PWA guardan access y refresh tokens en `sessionStorage` y `localStorage`, respectivamente; su JavaScript puede leerlos, por lo que una vulnerabilidad XSS podría exponerlos. Ambos clientes, ante un 401, intentan refresh una sola vez, actualizan la sesión y repiten la operación una vez; si refresh falla, limpian la sesión local. El logout envía el refresh token al API para revocar la familia. Los tokens se envían en JSON y en `Authorization: Bearer`, nunca en cookies automáticas; por eso el flujo no depende de cookies y reduce la exposición a CSRF clásico. La API no implementa blacklist para access tokens: logout bloquea renovaciones inmediatamente y el access JWT expira en 15 minutos.

**RF-09 — Entrega por correo/SMS.** En "Tickets digitales", un ADMINISTRADOR o SUPERVISOR abre el ticket, elige Correo, SMS o ambos y pulsa "Enviar ticket". La aplicación registra cada intento; si un canal falla de forma confirmada, se puede reintentar solo ese canal. Si el resultado del proveedor es incierto, el envío queda PENDIENTE: consulta el gateway y confirma en el historial si llegó o falló. Esa conciliación exige confirmación, espera al menos cinco minutos y queda auditada. API: `POST /api/tickets/{id}/enviar`, `POST /api/tickets/{id}/reenviar`, `POST /api/tickets/{id}/envios/{envioId}/reconciliar`, `GET /api/tickets/{id}/envios`. Solo ADMINISTRADOR/SUPERVISOR pueden enviar, consultar historial o reconciliar.

Para habilitar proveedores configura SMTP mediante `Smtp__Host`, `Smtp__Port`, `Smtp__Username`, `Smtp__Password`, `Smtp__From`, `Smtp__EnableSsl` y opcionalmente `Smtp__TimeoutMilliseconds`. El gateway SMS configurable requiere `Sms__Endpoint` (solo HTTPS), `Sms__ApiKey`, `Sms__Provider` y opcionalmente `Sms__TimeoutMilliseconds`; recibe JSON `{to,message,idempotencyKey}` y el header `Idempotency-Key`. `TicketDelivery__PublicBaseUrl` debe ser la URL pública HTTPS para el QR SMS. Guarda credenciales como secretos del entorno; no las agregues al repositorio. Las pruebas reemplazan ambos proveedores por fakes y no envían mensajes reales.

**RF-12 y RF-13 — Despacho.** El despacho se hace desde la app móvil escaneando el QR; la web no permite despachar por número. Antes de enviar el despacho, el operador debe marcar que verificó la identidad del conductor.

**RF-14 y RF-16 — Recepción.** Crea proveedor, estación y tanque desde "Administración"; luego ve a "Inventario" → "Registrar recepción" e indica proveedor, factura, tanque, volumen y fecha.

**RF-14 — Ajustes y mermas.** Solo por Swagger: `POST /api/inventario/ajustes` con tanque, tipo (`AJUSTE_POSITIVO`, `AJUSTE_NEGATIVO` o `MERMA`), cantidad, motivo y usuario.

**RF-15 y RF-17 — Inventario y movimientos.** Web → "Inventario" y "Recepciones y movimientos". La web recibe por SignalR cambios de despacho, recepción, ajuste y merma sin recargar. La conexión se reconecta con espera progresiva; al restablecerse consulta inventario y movimientos por REST para recuperar cambios ocurridos durante la desconexión. Los estados y el historial de la base siguen siendo la fuente de verdad.

**RF-19 y RF-20 — Reportes.** Web → "Reportes": elige consumo, tickets, despachos o movimientos, aplica filtros y descarga CSV, Excel o PDF. La API entrega filas, filtros y agregaciones; la pantalla y los archivos utilizan el mismo resultado. API: `GET /api/reportes` y `GET /api/reportes/exportar?formato=csv|xlsx|pdf`. Requiere sesión autenticada; todos los roles con acceso a la aplicación pueden consultar reportes.

**RF-22 — Tablero.** Web → "Resumen".

**RF-18 — Cierre diario.** En Web → "Cierres", el usuario autorizado elige estación y fecha operacional UTC, consulta el resumen persistido y registra el inventario físico de cada tanque antes de confirmar. El cierre es definitivo e inmutable. La vista incluye histórico con filtros y descarga del acta PDF. API: `GET /api/cierres-diarios/resumen?estacionId={id}&fecha=YYYY-MM-DD`, `POST /api/cierres-diarios`, `GET /api/cierres-diarios` (filtros `desde`, `hasta`, `estacionId`, `usuarioId`), `GET /api/cierres-diarios/{id}` y `GET /api/cierres-diarios/{id}/pdf`. ADMINISTRADOR, SUPERVISOR y DESPACHADOR crean cierres; esos roles y AUDITOR pueden consultar y descargar actas.

## Limitaciones y notas conocidas

El detalle requisito por requisito está en el documento de brechas frente al SRS. En resumen:

- **Autenticación y roles.** La API exige JWT en las rutas privadas, valida cuenta y roles actuales y soporta refresh rotatorio con revocación. Todavía faltan políticas de alcance por usuario; tokens en almacenamiento web/PWA conservan exposición a XSS y no hay límite de intentos de login por cuenta/IP.
- **Prueba de validación QR en memoria.** `POST /api/despachos` exige que la misma sesión haya validado recientemente el QR. Una instalación con varias instancias necesita un almacén compartido para esta prueba.
- **Escaneo QR y permisos de cámara.** El E2E de Chromium prueba el lector con un dispositivo sintético; no automatiza un teléfono/cámara física. El despliegue de la PWA requiere HTTPS para acceder a cámara en navegadores reales (`localhost` queda exento).
- **La web y la app móvil usan el mismo puerto (5173).** Para usarlas a la vez, arranca la app móvil con `npm run dev -- --port 5174`.
- **SignalR en producción.** La conexión `/hubs/inventory` usa el JWT de la sesión y comparte las reglas de lectura REST. Configura `Cors:AllowedOrigins` cuando web y API tengan orígenes distintos. La instancia actual transmite eventos solo a sus conexiones; un despliegue horizontal necesita un backplane SignalR o servicio equivalente.
- **Zonas horarias mezcladas.** El cierre define el día operacional como UTC, desde las 00:00:00 inclusive hasta las 00:00:00 del día siguiente exclusive. La base asigna nuevos movimientos y despachos a UTC. Otras fechas del sistema aún usan hora local de PostgreSQL; la web puede mostrarlas con desplazamiento.
- **Migración de firma QR.** Los QR existentes firmados con el formato anterior no validan con el HMAC nuevo; antes de desplegar sobre una base con tickets activos, hay que definir una reemisión controlada.
- **Estados del ticket.** `PROXIMO_A_VENCER` y `VENCIDO` se derivan de la fecha de vencimiento; `ANULADO`, `PENDIENTE` y `ENVIADO` se persisten. `ENVIADO` requiere confirmación del proveedor en todos los canales solicitados; los resultados de red inciertos requieren conciliación manual. El indicador derivado de vencimiento puede prevalecer sobre el estado persistido en la vista.
- **Límite de exportación.** Cada exportación general admite hasta 10 000 filas; aplica filtros para generar archivos más pequeños. La consulta web se pagina hasta 200 filas por página.
- **Solicitudes automáticas y recurrentes:** se guarda el tipo, pero no hay programación que las genere.
- **Sin transferencias entre tanques:** la base las permite, pero no hay endpoint.
- **Catálogos adicionales:** estaciones, tanques y proveedores se crean desde "Administración". La edición y desactivación de esos catálogos aún requiere la API.
- **Límite de despliegue TLS.** La API exige Kestrel HTTPS con certificado externo o una IP de proxy confiable al iniciar fuera de Development, y aplica redirección/HSTS. Esta política se prueba localmente; no se ha desplegado hosting externo ni instalado un certificado real.
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
psql -U postgres -h localhost -d tickets_combustible -v ON_ERROR_STOP=1 -f BASEDATOS/schema/DATABASE_FINALLL
```

Si `psql` no está en el PATH, usa la ruta completa, por ejemplo `"C:\Program Files\PostgreSQL\18\bin\psql.exe"`. El script deja 21 tablas y 5 vistas. No tiene sentencias `DROP`: ejecútalo una sola vez sobre una base vacía.

### 3. Crear la configuración privada de la API

```powershell
Copy-Item "BACKEND\API\appsettings.Development.example.json" "BACKEND\API\appsettings.Development.json"
notepad "BACKEND\API\appsettings.Development.json"
```

En `appsettings.Development.json` reemplaza:

- `TU_CLAVE` por la contraseña local del usuario `postgres`.
- `Qr:SigningSecret` por un secreto aleatorio de **al menos 32 caracteres** (sin él la API no emite ni valida tickets). Si lo cambias después, los tickets ya emitidos dejan de validar.
- `Jwt:Key` por otro secreto aleatorio de al menos 32 caracteres (sin él la API no arranca).
- Opcionalmente `Jwt:Issuer` y `Jwt:Audience` por los identificadores esperados para el emisor y el cliente.

El archivo está excluido del control de versiones. También se pueden usar variables de entorno: `ConnectionStrings__TicketsCombustible`, `Jwt__Key` y `Qr__SigningSecret`.

Aplica también las migraciones `BASEDATOS/migrations/007_auth_sessions.sql` y `008_recepcion_factura_unica.sql` después de cargar `BASEDATOS/schema/DATABASE_FINALLL`; crean la tabla de sesiones y la restricción de factura única por proveedor.

### 4. Arrancar la API

```powershell
dotnet run --project BACKEND\API\TicketsCombustible.Api.csproj --launch-profile http
```

### 5. Instalar y arrancar la web

```powershell
Copy-Item "APPWEB\.env.example" "APPWEB\.env"
pnpm install --frozen-lockfile
pnpm dev:web
```

La web requiere la API y PostgreSQL. El proxy de Vite envía `/api` a `http://localhost:5007`.

### 6. Arrancar la app móvil (opcional)

```powershell
npm --prefix APPMOBILE ci
npm --prefix APPMOBILE run dev -- --port 5174
```

Para usar un celular real, la cámara necesita HTTPS: expón la app con `ngrok http 5174` (`APPMOBILE/vite.config.js` ya acepta los dominios de ngrok) y ábrela en el teléfono.

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

## Configuración segura fuera de Development

No guardes secretos en `appsettings.json`, `.env.example`, archivos del frontend ni el repositorio. En desarrollo local usa variables de entorno o un gestor de secretos; en despliegue usa el almacén de secretos del entorno. La aplicación requiere `ConnectionStrings__TicketsCombustible`, `Jwt__Key` y `Qr__SigningSecret`; JWT y QR deben ser valores privados aleatorios de al menos 32 bytes. La API falla al iniciar si faltan o parecen placeholders. `Bootstrap__Secret` solo se necesita para crear el primer administrador y debe retirarse después.

Para correo configura, cuando vayas a usar ese canal, `Smtp__Host`, `Smtp__Port`, `Smtp__Username`, `Smtp__Password`, `Smtp__From` y `Smtp__EnableSsl`. Para SMS configura `Sms__Endpoint` (solo HTTPS), `Sms__ApiKey` y `Sms__Provider`. `TicketDelivery__PublicBaseUrl` es una URL HTTPS pública y no contiene credenciales. No coloques claves de servidor en variables `VITE_*`; son visibles en el navegador.

Configura una terminación TLS:

- **Kestrel:** usa `Kestrel__Endpoints__Https__Url` (por ejemplo `https://0.0.0.0:5001`), `Kestrel__Endpoints__Https__Certificate__Path` y, si el PFX lo requiere, `Kestrel__Endpoints__Https__Certificate__Password`. Monta el certificado privado desde fuera del repositorio; `.pfx`, `.p12`, `.pem` y `.key` se excluyen de Git.
- **Proxy inverso:** termina TLS en el proxy y configura `ReverseProxy__KnownProxies__0` con la IP exacta y confiable del proxy. La API procesa `X-Forwarded-Proto` únicamente desde esas IP configuradas; los demás requests HTTP se redirigen a HTTPS.

Fuera de Development la API aplica redirección HTTPS y HSTS. TestServer verifica la redirección y la cabecera HSTS; no simula el certificado ni confirma el hosting externo. La cámara de un teléfono requiere abrir la PWA por HTTPS; `localhost` es una excepción de desarrollo.

## Desarrollo y comprobaciones

Compilar la API:

```powershell
dotnet build BACKEND\API\TicketsCombustible.Api.csproj
```

Comprobar tipos y generar la web de producción (queda en `APPWEB/dist/`):

```powershell
pnpm build
pnpm preview:web
```

Generar la app móvil de producción (queda en `APPMOBILE/dist`):

```powershell
pnpm build:mobile
```

La app móvil tiene un modo simulado: en `APPMOBILE/src/services/ticketService.js`, `USE_MOCK = true` usa `mockData.js` (usuario `despachador1` / `1234`).

Las suites automatizadas se describen en "Pruebas automatizadas"; ejecuta `pnpm test:all` para validar API, web y PWA.

## Estructura principal

```text
APPMOBILE/
  src/                              PWA y servicios
  public/                           manifest, service worker, iconos y pruebas
  e2e/                              Playwright PWA
APPWEB/
  src/                              Aplicación React, páginas, componentes y tests
  public/                           Assets web
  vite.config.ts, tsconfig*.json    Configuración web
BACKEND/
  API/                              API REST .NET 8
  Tests/API/                        Pruebas unitarias e integración .NET
  Tests/E2ESupport/                 Fixtures utilizados por Playwright
BASEDATOS/
  schema/DATABASE_FINALLL           Esquema PostgreSQL
  migrations/                       Migraciones SQL ordenadas
DOCUMENTACION/                      Matrices SRS, QA y guías técnicas
scripts/                            Suites de pruebas globales y seguridad
package.json, pnpm-lock.yaml        Orquestación y dependencias web/E2E
README.md                           Entrada al repositorio`

## Consideraciones

- El sistema se ejecuta completamente en local y no utiliza Docker.
- La numeración de tickets y todo el movimiento de inventario ocurren en la base mediante triggers: la API inserta el despacho, la recepción o el ajuste, y la base actualiza la existencia y el estado del ticket. No modifiques `tanques.existencia_actual_galones` a mano.
- Las bajas son lógicas (`activo = false`); tickets, despachos y movimientos no deben eliminarse físicamente.
- La API valida la sesión y los roles de escritura; todavía quedan brechas de seguridad descritas arriba.
- `appsettings.Development.json` y `.env` están excluidos de Git; revisa `git status` antes de subir cambios.
- Una compilación satisfactoria no implica que los módulos señalados como pendientes estén terminados.
