# Informe de ejecución QA

Fecha: 2026-09-26. La suite integral final se ejecutó con `pnpm run test:all` sobre este repositorio. El script creó un clúster PostgreSQL temporal, cargó `DATABASE_FINALLL`, ejecutó todas las suites y lo eliminó al salir. No usó base configurada por el usuario, SMTP, SMS ni credenciales reales.

## Resumen

| Tipo | Casos/checks | Pasaron | Fallaron | Omitidos |
|---|---:|---:|---:|---:|
| Unitarias web (Vitest) | 5 | 5 | 0 | 0 |
| Unitarias de contrato móvil (Node test) | 3 | 3 | 0 | 0 |
| Integración API + PostgreSQL real (xUnit) | 4 métodos | 4 | 0 | 0 |
| **Total ejecutado** | **12** | **12** | **0** | **0** |
| E2E de navegador | 0 | — | — | — |

Las cuatro pruebas de integración recorren registro/login y persistencia; autorización con roles, token manipulado/vencido y cuenta desactivada; solicitud/aprobación/emisión/PNG/validación de QR y firma alterada; recepción/despacho/inventario/movimientos/reuso y dos operadores concurrentes.

## Cobertura

- Backend API con Coverlet/Cobertura: **67.72% líneas**, **38.42% ramas** en la ejecución integral final.
- Métodos: **80/116 cubiertos (68.97%)** según los nodos de método publicados por Cobertura.
- Frontend web y PWA: no se midió coverage de código; Vitest/Node ejecutaron los casos indicados, sin proveedor de cobertura configurado.
- La cobertura global no alcanza los objetivos orientativos de 80% de backend y 90% de lógica crítica del SRS. La cobertura de ramas (38.42%) es especialmente baja; los resultados no certifican validaciones de todos los DTOs ni roles.

## Estado de requisitos

- Implementación completa en el alcance declarado: **5** (RF-06, RF-12, RF-14, RF-16, RF-17).
- Implementación parcial: **21**.
- No implementados: **4** (RF-09, RF-18, RF-21, RS-06).
- Requisitos con pruebas automatizadas en parte de su alcance: **22 de 30**.
- Resultados de matriz: PASS **5**, PARTIAL **17**, NOT_IMPLEMENTED **4**, NOT_TESTABLE **4**, FAIL **0**.
- La correspondencia, estado y límites de cada requisito está en [SRS_TEST_MATRIX.md](SRS_TEST_MATRIX.md).

## E2E

No se crearon pruebas E2E de navegador. El proyecto no tiene Playwright/Cypress ni harness web móvil; la pantalla web de despacho es solo informativa y el escáner móvil depende de cámara/permisos de navegador. Los flujos de API del servidor se probaron con HTTP real de `WebApplicationFactory` y PostgreSQL, lo cual no simula navegador, cámara o interacción visual. Los E2E-01 a E2E-07 del SRS quedan NO EJECUTABLES en este entorno.

## Bugs

- Critical: **0**
- High: **2 corregidos** (integridad QR; despacho móvil).
- Medium: **2 corregidos** (contrato de lista móvil; carrera de doble despacho con respuesta controlada).
- Low: **0**

### Corregidos

- Firma QR anterior no cubría cantidad ni otros campos protegidos: ahora se comprueba HMAC-SHA-256 y el regresivo cambia cantidad persistida y exige QR inválido.
- La PWA no enviaba confirmación de identidad y no enseñaba el texto del error: ahora solicita confirmación explícita y muestra el error devuelto.
- La PWA interpretaba mal el esquema de la lista `/api/tickets`: ahora adapta el número, UUID y galones.
- Una carrera de despacho concurrente alcanzaba la restricción única sin conflicto de negocio controlado: ahora esa constraint responde 409; el regresivo confirma una sola operación exitosa y un solo descuento.

### Pendientes y riesgos

- Los tickets emitidos con el hash previo fallarán validación tras desplegar el nuevo verificador. Hay que decidir y ejecutar reemisión/invalidez controlada para cualquier base existente; QA se ejecutó sobre una base recién cargada sin tickets heredados.
- Auditoría, cierres diarios, notificaciones persistidas, email/SMS, exportación PDF/Excel, asignación recurrente programada y reportes del servidor faltan como funcionalidad; no se añadieron para satisfacer pruebas.
- RBAC y validación solo cubren casos principales; no es un pentest ni una matriz completa de permisos.
- El token QR validado se guarda en memoria de proceso; varias instancias API requieren caché compartida.
- No existe E2E visual; permisos de cámara en dispositivos reales no fueron verificados.
- Quedan mejoras de zonas horarias descritas en README. No se ampliaron cambios de fecha en este ciclo QA.

## Archivos

### Creados

- `tests/TicketsCombustible.Api.Tests/` — suite xUnit con PostgreSQL, factory y cuatro flujos de integración.
- `app-movil/src/services/ticketService.test.js` — tres pruebas de contrato móvil.
- `SRS_TEST_MATRIX.md`, `BUG_REPORT.md`, `TEST_REPORT.md`.
- `scripts/test-all.sh` — instancia temporal, compilación, pruebas y cobertura.

### Modificados para QA

- `backend/TicketsCombustible.Api/Program.cs` — expone `Program` para el host de pruebas.
- `backend/TicketsCombustible.Api/Controllers/TicketsController.cs` — firma y validación QR sobre campos protegidos.
- `backend/TicketsCombustible.Api/Controllers/DespachosController.cs` — mapea duplicado concurrente a 409.
- `app-movil/src/services/ticketService.js`, `app-movil/src/pages/despacho.js` — adapta contratos, confirmación y errores.
- `vite.config.ts`, `package.json`, `app-movil/package.json`, `README.md` — aislamiento de pruebas, scripts y guía.

El checkout ya tenía modificaciones visuales del trabajo previo (logo/paleta) en otros archivos. Se conservaron y no se atribuyen a esta auditoría.

## Comandos

Dependencias (una vez):

```bash
pnpm install --frozen-lockfile
npm --prefix app-movil ci
dotnet restore tests/TicketsCombustible.Api.Tests/TicketsCombustible.Api.Tests.csproj
```

Suites individuales:

```bash
pnpm test
pnpm --dir app-movil test
QA_TEST_CONNECTION='Host=127.0.0.1;Port=55432;Database=lavomva_test;Username=qa_runner' dotnet test tests/TicketsCombustible.Api.Tests/TicketsCombustible.Api.Tests.csproj
```

No hay comando E2E porque no existe suite de navegador configurada. Para compilar/probar todo y generar cobertura de backend en un PostgreSQL efímero:

```bash
pnpm test:all
```

Ese comando requiere Bash, Node, pnpm, .NET SDK 8, Python 3 y `initdb`, `pg_ctl`, `createdb`, `psql` instalados. La base temporal se limpia automáticamente. La conexión de `QA_TEST_CONNECTION` del ejemplo individual solo debe usarse con una base vacía dedicada que haya cargado el SQL de esquema.
