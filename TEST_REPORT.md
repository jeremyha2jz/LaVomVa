# Informe de ejecución QA

## Fase RS-01 / RS-05 — autenticación y seguridad API

Fecha: 2026-09-27. `pnpm test:all` terminó correctamente contra PostgreSQL temporal local. El script detuvo y eliminó el clúster temporal. No se usó producción, no hubo deploy/push y no se hicieron solicitudes a sistemas externos.

### Resultado

| Suite | Anterior | Nuevos | Total | Pasaron | Fallaron | Omitidos |
|---|---:|---:|---:|---:|---:|---:|
| Web (Vitest) | 49 | 4 | 53 | 53 | 0 | 0 |
| PWA (Node test) | 13 | 5 | 18 | 18 | 0 | 0 |
| API (xUnit + PostgreSQL) | 190 | 17 | 207 | 207 | 0 | 0 |
| **Total** | **252** | **26** | **278** | **278** | **0** | **0** |

Se conservaron y pasaron los 252 tests anteriores; no se eliminaron ni debilitaron asserts y no se añadieron skips. `dotnet build` de API, `pnpm build` web y build PWA también terminaron correctamente.

### Cobertura

| Área | Anterior | Nueva |
|---|---:|---:|
| API líneas | 94.59% | **94.56%** |
| API ramas | 79.22% | **78.48%** |
| API métodos | 96.78% (511/528) | **96.77% (540/558)** |
| Web líneas | 69.92% | **70.75%** |
| Web ramas | 50.72% | **51.21%** |
| PWA líneas | 87.58% | **90.95%** |
| PWA ramas | 60.87% | **70.91%** |

### RS-01 — autenticación

- Estado anterior: **PARTIAL**. Estado nuevo: **PASS**.
- Access JWT HS256 de 15 minutos con `iss`, `aud`, `iat`, `exp` y `jti`; firma, expiración con 30 segundos de tolerancia, cuenta activa y roles actuales se validan en cada solicitud.
- Refresh token de 32 bytes aleatorios (43 caracteres Base64URL); solo se persiste SHA-256 de 64 caracteres hexadecimales. Expira a los 30 días y rota cada vez.
- Rotación bloquea la fila anterior con `SELECT ... FOR UPDATE` y persiste ambas filas/transición en una transacción. La reutilización de un token reemplazado revoca toda la familia. Dos refresh simultáneos producen un único 200 y un 401; el replay concurrente revoca también el reemplazo, por lo que no deja una rama activa.
- Logout revoca su familia; logout-all revoca sesiones de la cuenta actual. Cambio/reset de contraseña, cambio de rol y desactivación invalidan refresh activos. Se probaron carreras de logout, reset y desactivación frente a refresh; quedan sin sesiones activas ni respuestas 5xx.
- La API no mantiene blacklist de access JWT. Un token emitido sigue válido como máximo 15 minutos después de logout, salvo que la cuenta se desactive o cambien sus roles, comprobados por request.
- Web comparte refresh concurrente, reintenta la operación una vez y limpia sesión al fallar. SignalR consulta el token actualizado. PWA comparte un refresh concurrente, reintenta una vez, vuelve al login al expirar y conserva solo los datos no relacionados al cerrar sesión.

### RS-05 — seguridad API

- Estado anterior: **PARTIAL**. Estado nuevo: **PASS** para la superficie y categorías descritas aquí; se realizó **security regression testing local; no sustituye pentest externo independiente**.
- Cubierto: 71 endpoints y sus políticas; 390 combinaciones de roles y 65 comprobaciones anónimas; actualización de rol/claims, over-posting de usuario, logout-all limitado al dueño, aislamiento de notificaciones ya probado, IDs inválidos, JSON truncado, content type incorrecto, entrada grande razonable, cadenas SQL/HTML como datos, traversal en filtros, CORS sin credenciales, headers, `no-store`, respuestas sin hash/secreto y errores sin stack trace.
- Se encontró y corrigió la interpolación de datos de la API en `innerHTML` de la PWA. Todas las plantillas con valores dinámicos ahora escapan entidades HTML; la clase derivada del estado admite únicamente caracteres seguros. React no usa `dangerouslySetInnerHTML`.
- El bootstrap serializa solicitudes concurrentes con bloqueo transaccional de PostgreSQL. Dos intentos generan un solo administrador y responden 200/409.
- No se encontró rate limiter/lockout de login; queda documentado como mejora operativa. La respuesta 403 de usuario inactivo se mantiene como parte del flujo de activación. Los tokens del navegador siguen expuestos a XSS porque se guardan en `sessionStorage`/`localStorage`.

### Requisitos y bugs

- RS-01: **PARTIAL → PASS**. RS-05: **PARTIAL → PASS**. RF-13 y RS-03 permanecen **PARTIAL** y fuera del trabajo funcional de esta fase.
- Bugs reales encontrados y corregidos: BUG-17 (orden de persistencia/fk en rotación) y BUG-18 (XSS por interpolación en PWA). Bugs pendientes de esta fase: **0**. Las mejoras operativas mencionadas no se registraron como bugs de severidad sin evidencia adicional.
- Suites API emitieron dos avisos EF1002 en fixtures SQL de fechas preexistentes (`ApiCoverageTests.cs:2132–2133`); no bloquearon pruebas y no pertenecen al código productivo.

Comando final: `pnpm test:all` — **278 passed, 0 failed, 0 skipped**.

## Fase RF-24 / RS-02 — inventario API y matriz RBAC

Fecha: 2026-09-27. Corrida final de `pnpm test:all` sobre PostgreSQL temporal reconstruido desde `DATABASE_FINALLL` y migraciones 001–006. El script terminó y eliminó la instancia temporal. No se usó producción, deploy, push ni proveedores externos.

### Resultado

| Suite | Anterior | Nuevos | Total | Pasaron | Fallaron | Omitidos |
|---|---:|---:|---:|---:|---:|---:|
| Web (Vitest) | 49 | 0 | 49 | 49 | 0 | 0 |
| PWA (Node test) | 13 | 0 | 13 | 13 | 0 | 0 |
| API (xUnit + PostgreSQL) | 186 | 4 | 190 | 190 | 0 | 0 |
| **Total** | **248** | **4** | **252** | **252** | **0** | **0** |

Se conservaron y pasaron los 248 tests anteriores. Los cuatro nuevos verifican inventario completo + 378 combinaciones RBAC, bootstrap de administrador, generación OpenAPI y protección contra mass assignment en altas de catálogos.

### Cobertura

| Área | Anterior | Nueva |
|---|---:|---:|
| API líneas | 91.86% | **94.59%** |
| API ramas | 77.03% | **79.22%** |
| API métodos | 93.10% (486/522) | **96.78% (511/528)** |
| Web líneas | 69.92% | **69.92%** |
| Web ramas | 50.72% | **50.72%** |
| PWA líneas | 87.58% | **87.58%** |
| PWA ramas | 60.87% | **60.87%** |

### RF-24 — API REST

- Estado anterior: **PARTIAL**. Estado nuevo: **PASS**.
- La prueba de inventario compara los 67 endpoints de controlador obtenidos de `IActionDescriptorCollectionProvider` con los 67 clasificados en `API_ENDPOINT_MATRIX.md`; rutas faltantes, duplicadas o nuevas hacen fallar la suite.
- La matriz contiene 4 rutas intencionalmente públicas y 63 protegidas. De las protegidas, 46 limitan por roles explícitos y 17 requieren cualquier rol autenticado. `/hubs/inventory` y el negotiate se inventarían aparte como superficie técnica protegida; Swagger se genera mediante `ISwaggerProvider`, y la UI/JSON HTTP solo se mapean en `Development`.
- Las pruebas existentes siguen cubriendo contratos de usuarios, catálogos, solicitudes, tickets, despacho, recepción, inventario, cierres, notificaciones, auditoría, programaciones, reportes, PDF/CSV/XLSX y QR. OpenAPI confirma rutas y verbos de tickets/cierres.
- Se reemplazó el retorno de entidad completa en `POST /api/tickets` con un recibo DTO que excluye token y hash QR. Se sustituyeron entidades EF entrantes para altas/ediciones de catálogo y proveedores por DTOs; claves y estado interno de tanque quedan fuera del contrato.

### RS-02 — autorización RBAC

- Estado anterior: **PARTIAL**. Estado nuevo: **PASS**.
- Roles reales confirmados: `ADMINISTRADOR`, `SUPERVISOR`, `DESPACHADOR`, `SOLICITANTE`, `AUDITOR`, `CONSULTA`.
- Combinaciones de ruta protegida × rol: **378** (63 × 6), además de **63** solicitudes sin JWT que deben recibir 401. Rutas públicas: 4, verificadas separadamente.
- Solicitudes permitidas / denegadas por rol: ADMINISTRADOR **63/0**; SUPERVISOR **55/8**; DESPACHADOR **28/35**; SOLICITANTE **18/45**; AUDITOR **26/37**; CONSULTA **17/46**. Cada rol autorizado llega a la ruta real con restricciones de GUID/long cumplidas; las no autorizadas reciben 403.
- Los metadatos efectivos de `IAuthorizeData` se comparan con la matriz, intersectando las restricciones declaradas a nivel de controlador y acción. La prueba cubre anónimos, fallback autenticado y acceso por roles.
- Bootstrap: secreto erróneo -> 401; primer uso válido -> 200; uso posterior -> 409; solo se conserva un administrador creado y la respuesta no revela el secreto. QR público firma/valida un token limitado; auditoría y cierres no tienen rutas de edición/borrado. SignalR conserva autenticación JWT y auditoría/notificaciones conservan aislamiento ya probado.
- CORS revisado en `Program.cs`: desarrollo permite origen/cabeceras/métodos amplios sin credenciales; fuera de desarrollo solo configura orígenes explícitos de `Cors:AllowedOrigins` y tampoco habilita credenciales. README ya documentaba configurar ese origen.

### Bugs

- Encontrados y corregidos: **2**. BUG-15: respuesta de emisión de ticket filtraba token/hash QR; BUG-16: model binding de entidades EF permitía enviar IDs y estado interno de tanque.
- Pendientes nuevos: **0**. Las regresiones específicas están en `ApiIntegrationTests` y `ApiEndpointMatrixTests`.

### Requisitos

- RF-24: **PARTIAL → PASS**.
- RS-02: **PARTIAL → PASS**.
- El total de requisitos en `SRS_TEST_MATRIX.md` queda en **26 PASS, 4 PARTIAL, 0 NOT_TESTABLE, 0 NOT_IMPLEMENTED**. Esta fase no modifica RS-01 ni RS-05.

La corrida integral ejecutó `pnpm test`, `pnpm build`, tests y build de PWA, `dotnet build` y pruebas API con cobertura. Sigue apareciendo el aviso de versiones mezcladas Vitest 5.0.2 / coverage-v8 5.0.1 y dos avisos EF1002 preexistentes de fixtures SQL, sin fallos de build ni de test.

Comando final ejecutado: `pnpm test:all` — **252 passed, 0 failed, 0 skipped**.

## Fase RF-19/RF-20 — reportes y exportaciones

Fecha: 2026-09-27. Corrida final de `pnpm test:all` con PostgreSQL temporal basado en `DATABASE_FINALLL` y migraciones 001–006. El script terminó y eliminó el clúster temporal. No se usó producción, deploy, push ni proveedores externos.

### Resultado

| Suite | Anterior | Nuevos | Total | Pasaron | Fallaron | Omitidos |
|---|---:|---:|---:|---:|---:|---:|
| Web (Vitest) | 45 | 4 | 49 | 49 | 0 | 0 |
| PWA (Node test) | 13 | 0 | 13 | 13 | 0 | 0 |
| API (xUnit + PostgreSQL) | 182 | 4 | 186 | 186 | 0 | 0 |
| **Total** | **240** | **8** | **248** | **248** | **0** | **0** |

Se conservaron los 240 tests anteriores. No se eliminaron pruebas, añadieron skips ni debilitaron asserts. Los ocho nuevos casos cubren consulta/API y exportación filtrada, persistencia de datos, CSV injection, XLSX válido, PDF vacío y multipágina, paginación y flujos web de cargar, filtrar, exportar, error y vacío.

### Cobertura

| Área | Anterior | Nueva |
|---|---:|---:|
| API líneas | 91.85% | **91.86%** |
| API ramas | 75.70% | **77.03%** |
| API métodos | 92.47% (430/465) | **93.10% (486/522)** |
| Web líneas | 69.24% | **69.92%** |
| Web ramas | 49.11% | **50.72%** |
| PWA líneas | 87.58% | **87.58%** |
| PWA ramas | 60.87% | **60.87%** |

La API supera los mínimos de esta fase de 90% en líneas y 70% en ramas. La suite reportó además el aviso existente de Vitest/coverage-v8 con versiones 5.0.2/5.0.1 y dos EF1002 en fixtures de SQL de pruebas; builds y suites terminaron correctamente.

### Requisitos

- **RF-19:** anterior PARTIAL; nuevo **PASS**. API server-side para consumo, tickets, despachos y movimientos; filtros inclusivos UTC, agregaciones, totales y paginación.
- **RF-20:** anterior PARTIAL; nuevo **PASS**. Exportaciones reales CSV, XLSX y PDF. Los cuatro formatos utilizan el mismo resultado filtrado y totales; CSV y Excel protegen texto introducido por usuarios.
- **RF-24:** sigue PARTIAL, porque la cobertura no alcanza todas las rutas REST del sistema.
- **RS-02:** sigue PARTIAL, porque la matriz RBAC todavía no cubre cada endpoint protegido.

### Validaciones

`GET /api/reportes` y `GET /api/reportes/exportar` requieren autenticación; todos los roles autenticados mantienen la lectura ya disponible en la pantalla Reportes. Las queries y agregaciones se realizan en PostgreSQL; la UI pagina hasta 200 filas y los archivos tienen un máximo de 10 000. El XLSX tiene hojas Resumen/Detalle y se volvió a abrir con ClosedXML. PDF usa el generador existente y pasó cabecera, contenido, vacío y multipágina. JSON/CSV/XLSX/PDF se compararon sobre el mismo dataset persistido.

### Bugs

Se encontró y corrigió BUG-14: el proveedor EF Core no traducía agrupaciones de un DTO para reportes de movimientos y podía responder 500. La regresión de 65 movimientos persistidos, paginación y PDF multipágina pasó. Las fallas iniciales de su fixture eran por fechas de inserción que el trigger reemplaza con la fecha UTC real y objetos EF rastreados; el test fija su fecha operacional explícitamente después del insert. No se registran como bugs adicionales del producto.

Comando final ejecutado: `pnpm test:all` — **248 passed, 0 failed, 0 skipped**.

## Fase RF-15 — inventario en tiempo real

Fecha: 2026-09-27. Validación final ejecutada con `pnpm test:all` contra un PostgreSQL temporal basado en `DATABASE_FINALLL` y migraciones 001–006. El script apagó y eliminó el clúster temporal al terminar. No se usó producción, deploy, push ni proveedores reales de correo/SMS.

### Resultado

| Suite | Anterior | Nuevos en RF-15 | Total | Pasaron | Fallaron | Omitidos |
|---|---:|---:|---:|---:|---:|---:|
| Web (Vitest) | 38 | 7 | 45 | 45 | 0 | 0 |
| PWA (Node test) | 13 | 0 | 13 | 13 | 0 | 0 |
| API (xUnit + PostgreSQL) | 176 | 6 | 182 | 182 | 0 | 0 |
| **Total** | **227** | **13** | **240** | **240** | **0** | **0** |

Se conservaron los 227 casos previos; no se eliminaron tests, no se añadieron skips ni se debilitaron asserts. Los 13 nuevos cubren conexión y eventos SignalR en la web, integración del contexto con Dashboard, reconexión/REST sync, deduplicación y secuencia por tanque, movimientos de despacho/recepción/ajustes/merma, transiciones críticas, rollback/cierre diario, aislamiento de fallos post-commit, autenticación del Hub y una conexión SignalR real de prueba que compara el evento con PostgreSQL.

### Cobertura

| Área | Anterior | Nueva | Cambio |
|---|---:|---:|---:|
| API líneas | 91.85% | **91.85%** | — |
| API ramas | 75.71% | **75.70%** | −0.01 pp |
| API métodos | 92.91% (393/423) | **92.47% (430/465)** | −0.44 pp |
| Web líneas | 62.92% | **69.24%** | +6.32 pp |
| Web ramas | 47.55% | **49.11%** | +1.56 pp |
| PWA líneas | 87.58% | **87.58%** | — |
| PWA ramas | 60.87% | **60.87%** | — |

La API conserva los mínimos de RF-15 (90% líneas, 70% ramas). PWA no se modificó y conserva cobertura de 87.58% líneas y 60.87% ramas.

### RF-15 — inventario en tiempo real

- Estado anterior: **PARTIAL**. Estado nuevo: **PASS**.
- Hub autenticado `/hubs/inventory` con SignalR; el navegador utiliza el JWT existente mediante `access_token` solo en esa ruta. Todos los roles autenticados reciben el mismo alcance de lectura que los GET REST actuales.
- `InventoryUpdated`, `InventoryMovementCreated` y `CriticalInventoryChanged` contienen snapshot de movimiento/stock persistido, secuencia `movementId`, estación y hora UTC. Solo se emiten tras commit; fallos de fan-out quedan en log y no alteran operaciones confirmadas.
- Despacho y recepción usan los movimientos automáticos insertados por los triggers. Ajuste positivo/negativo y merma publican el movimiento de `movimientos_inventario`. La transición crítica se publica únicamente al cambiar entre normal/crítico y convive con las notificaciones persistentes RF-23.
- El contexto web mantiene una sola conexión, aplica cambios por tanque, ignora eventos de stock antiguos, añade movimientos recientes, limpia listeners y sincroniza REST de inventario/movimientos y notificaciones tras reconectar. SignalR no almacena eventos ni sustituye REST.
- La suite incluye conexión Long Polling de Hub real dentro de `WebApplicationFactory`, operación PostgreSQL real y comprobación de saldo del evento contra el stock persistido. No requiere sockets o servicios externos.
- El Hub transmite dentro de la instancia actual; para escalar horizontalmente se necesitará backplane SignalR o servicio equivalente.

### RF-23 — regresión

- Estado: continúa **PASS**. Sus pruebas de persistencia, deduplicación, permisos, filtros y sincronización REST pasaron en esta suite. Se añade REST sync de notificaciones después de reconectar.

### Bugs

- Bugs de producto nuevos encontrados en RF-15: **0**. Bugs corregidos: **0**. Pendientes nuevos: **0**. Los casos de rollback y fallo del transporte son regresiones de RF-15; no se hallaron defectos adicionales.

Archivos RF-15: Hub/contratos/servicio API, controladores de inventario/despacho/recepción, configuración SignalR/JWT, cliente compartido/contexto web, pruebas, proxy Vite, README y matriz SRS.

Comando final ejecutado: `pnpm test:all` — **240 passed, 0 failed, 0 skipped**.

Fecha: 2026-09-27. Fase RF-11 ejecutada con `pnpm test:all` contra PostgreSQL temporal basado en `DATABASE_FINALLL` y migraciones 001–005. El script eliminó el clúster temporal al terminar. No se usó producción, deploy, push, SMTP real ni gateway SMS real.

## Resultado

| Suite | Antes | Nuevos en esta fase | Total | Pasaron | Fallaron | Omitidos |
|---|---:|---:|---:|---:|---:|---:|
| Web (Vitest) | 33 | 3 | 36 | 36 | 0 | 0 |
| PWA (Node test) | 13 | 0 | 13 | 13 | 0 | 0 |
| API (xUnit + PostgreSQL) | 146 | 18 | 164 | 164 | 0 | 0 |
| **Total** | **192** | **21** | **213** | **213** | **0** | **0** |

Los 192 tests de regresión se conservaron y pasaron. No se añadieron tests omitidos ni se quitaron o debilitaron asserts. Los 21 tests añadidos cubren el formulario web de programaciones, generación diaria y única, recurrencia semanal/mensual, catch-up, fin de mes/año bisiesto, pausa/reactivación/edición y reintento, referencias inactivas, historial, RBAC, concurrencia de workers, rollback de auditoría y ciclo de vida del BackgroundService. Los proveedores de la API de integración siguen siendo fakes sin capacidad de red; el adapter HTTP SMS se prueba con handler local.

## Cobertura

| Área | Anterior | Nueva | Cambio |
|---|---:|---:|---:|
| API líneas | 91.42% | **91.79%** | +0.37 puntos |
| API ramas | 75.83% | **75.84%** | +0.01 puntos |
| API métodos | 93.85% (290/309) | **94.13% (369/392)** | +0.28 puntos |
| Web líneas | 59.20% | **61.51%** | +2.31 puntos |
| Web ramas | 44.91% | **47.62%** | +2.71 puntos |
| PWA líneas | 87.58% | **87.58%** | — |
| PWA ramas | 60.87% | **60.87%** | — |

La API mantiene los mínimos acordados de 90% de líneas y 70% de ramas. Vitest reportó versiones mezcladas (Vitest 5.0.2 y `@vitest/coverage-v8` 5.0.1), pero la suite y cobertura terminaron correctamente. El proyecto de tests API conserva dos avisos EF1002 en un fixture UTC preexistente; no hubo errores de compilación.

## RF-11 — asignaciones automáticas y manuales

- Estado anterior: **PARTIAL**. Estado nuevo: **PASS**.
- `MANUAL` continúa por el flujo existente. `AUTOMATICA` crea una solicitud una vez; `RECURRENTE` acepta frecuencia DIARIA, SEMANAL y MENSUAL.
- El worker procesa al arrancar y luego según `Scheduling:IntervalSeconds` (predeterminado 60 segundos; límites 15 a 86400). La lógica usa `TimeProvider`; al recuperarse de una caída procesa una sola ocurrencia vencida y salta el backlog restante.
- Programaciones e historial se almacenan en tablas ligadas a solicitudes, con unicidad por programación+fecha y `FOR UPDATE SKIP LOCKED` en transacción. Dos procesadores simultáneos crean exactamente una solicitud; una repetición secuencial no genera otra.
- Fechas UTC. Las ocurrencias mensuales se calculan desde el ancla original: 31 de enero de 2024 → 29 de febrero → 31 de marzo. Fecha final inclusiva; al no quedar más ocurrencias, la programación se desactiva.
- Los roles ADMINISTRADOR y SUPERVISOR administran, consultan e inspeccionan historial. La web permite crear, editar, activar/desactivar y ver próximas/últimas ejecuciones e historial. Solicitudes generadas permanecen PENDIENTE.
- Se validan las referencias al crear y antes de cada generación; si una queda inválida, se registra FALLIDA y la programación se pausa. Auditoría transaccional cubre creación, edición, activación/desactivación, generación y fallo. El test de trigger confirma rollback si falla auditoría.
- Si falla una programación AUTOMATICA por referencias inválidas, el administrador puede corregirlas y reactivar un reintento; el historial FALLIDA se conserva y una ejecución GENERADA no se repite.

## RF-09 — entrega de tickets

- Estado anterior: **NOT_IMPLEMENTED**. Estado nuevo: **PASS**.
- Endpoints: `POST /api/tickets/{id}/enviar`, `POST /api/tickets/{id}/reenviar`, `GET /api/tickets/{id}/envios`, `POST /api/tickets/{id}/envios/{envioId}/reconciliar`; QR público limitado: `GET /api/tickets/public/qr?token=...`.
- Roles de envío, historial y conciliación: ADMINISTRADOR y SUPERVISOR. Sin JWT devuelve 401; roles sin permiso reciben 403.
- Los canales CORREO, SMS o AMBOS generan intentos persistidos en `envios_ticket`, clave de idempotencia por solicitud/canal, número de intento y lote. El flujo bloquea por ticket y el índice parcial impide dos envíos pendientes del mismo canal.
- El correo incluye el QR como imagen inline. SMS usa gateway configurable HTTPS y enlace al QR ya firmado. Los secretos se leen desde configuración de entorno; no se guardan en logs ni auditoría. Historial enmascara destinatarios.
- ENVIADO se registra al aceptar el proveedor para todos los canales solicitados. Un fallo definitivo queda FALLIDO y permite reintentar solo ese canal. Un timeout o resultado incierto conserva PENDIENTE y bloquea el reenvío automático; después de cinco minutos, un administrador/supervisor puede conciliar manualmente tras revisar el proveedor. La decisión queda auditada.
- La reserva, transición inicial y evento de auditoría son atómicos. Cada resultado de proveedor y evento correspondiente se persisten en la transacción final. Ante fallo de auditoría después de la aceptación externa, la reserva queda pendiente y la misma idempotency key evita repetir el envío; la conciliación resuelve el caso.
- RF-10 pasa de **PARTIAL** a **PASS**: quedan persistidos PENDIENTE/ENVIADO además de los estados previos y se mantienen las reglas de estados derivados/terminales.

## RF-21 / RS-06 auditoría

- RF-21: permanece **PASS**; se agregaron eventos de solicitud, envío aceptado, fallo, incertidumbre y conciliación.
- RS-06: permanece **PASS**; los eventos incluyen actor, entidad, resultado, lote/canal, destino enmascarado y datos no secretos. El registro se mantiene atómico con cada persistencia crítica.

- RF-11 agrega PROGRAMACION_CREADA, PROGRAMACION_MODIFICADA, PROGRAMACION_ACTIVADA, PROGRAMACION_DESACTIVADA, SOLICITUD_AUTOMATICA_GENERADA, SOLICITUD_RECURRENTE_GENERADA y EJECUCION_PROGRAMADA_FALLIDA; la suite demuestra rollback cuando la auditoría de una generación falla.
- RF-24 permanece **PARTIAL**: los nuevos endpoints tienen pruebas, pero aún no está cubierta cada ruta/contrato REST del sistema.

## Bugs

- En la fase RF-09 se documentó BUG-09. En RF-11 se encontraron y corrigieron **3 bugs reales**: BUG-10 (tipo de fecha PostgreSQL), BUG-11 (reactivación de automáticas fallidas) y BUG-12 (doble conversión de la hora sugerida en web).
- Bugs corregidos en RF-11: **3**. Bugs nuevos pendientes: **0**.
- Los fallos iniciales de fixtures/asserts se ajustaron según el comportamiento real del harness; no se registraron como bugs de producto.

## Archivos de evidencia

- API: `backend/TicketsCombustible.Api/Services/TicketDeliveryService.cs`, `TicketDeliveryProviders.cs`, `TicketQrSignature.cs`, `Controllers/TicketDeliveryController.cs`, migración `004_ticket_delivery.sql` y `tests/TicketsCombustible.Api.Tests/ApiCoverageTests.cs`.
- Web: `src/pages/Tickets.tsx`, `src/pages/Tickets.test.tsx`, `src/services/api.ts` y `src/services/api.test.ts`.
- RF-11: `backend/TicketsCombustible.Api/Services/SolicitudProgramacionService.cs`, `SolicitudProgramacionWorker.cs`, `Controllers/ProgramacionesController.cs`, migración `005_solicitud_scheduling.sql`, `src/components/ScheduleManager.tsx` y `tests/TicketsCombustible.Api.Tests/SolicitudProgramacionWorkerTests.cs`.
- Esquema y ejecución: `DATABASE_FINALLL`, `scripts/test-all.sh`.

Comando final ejecutado: `pnpm test:all` — **213 passed, 0 failed, 0 skipped**.
