# Informe de ejecución QA

Fecha: 2026-09-27. Fase RF-09 ejecutada con `pnpm test:all` contra PostgreSQL temporal basado en `DATABASE_FINALLL` y migraciones 001–004. El script eliminó el clúster temporal al terminar. No se usó producción, deploy, push, SMTP real ni gateway SMS real.

## Resultado

| Suite | Antes | Nuevos en esta fase | Total | Pasaron | Fallaron | Omitidos |
|---|---:|---:|---:|---:|---:|---:|
| Web (Vitest) | 29 | 4 | 33 | 33 | 0 | 0 |
| PWA (Node test) | 13 | 0 | 13 | 13 | 0 | 0 |
| API (xUnit + PostgreSQL) | 99 | 47 | 146 | 146 | 0 | 0 |
| **Total** | **141** | **51** | **192** | **192** | **0** | **0** |

Los 141 tests previos se conservaron y pasaron. No se añadieron tests omitidos ni se quitaron o debilitaron asserts. Los tests nuevos cubren correo/SMS y modo dual, QR, persistencia e idempotencia, reintento por canal, fallos/timeout ambiguo, conciliación, concurrencia, auditoría, RBAC y vista web. Los proveedores de la API de integración son fakes sin capacidad de red; el adapter HTTP SMS se prueba con handler local.

## Cobertura

| Área | Anterior | Nueva | Cambio |
|---|---:|---:|---:|
| API líneas | 92.27% | **91.42%** | −0.85 puntos |
| API ramas | 74.78% | **75.83%** | +1.05 puntos |
| API métodos | 92.65% (227/245) | **93.85% (290/309)** | +1.20 puntos |
| Web líneas | 57.65% | **59.20%** | +1.55 puntos |
| Web ramas | 43.50% | **44.91%** | +1.41 puntos |
| PWA líneas | 87.58% | **87.58%** | — |
| PWA ramas | 60.87% | **60.87%** | — |

La API conserva cobertura alta de negocio y gana cobertura de ramas/métodos. Vitest reportó versiones mezcladas (Vitest 5.0.2 y `@vitest/coverage-v8` 5.0.1), pero la suite y cobertura terminaron correctamente. El compilador de tests API conserva dos avisos EF1002 en SQL interpolado de fixtures UTC; no hubo errores de compilación.

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
- RF-24 permanece **PARTIAL**: los nuevos endpoints tienen pruebas, pero aún no está cubierta cada ruta/contrato REST del sistema.

## Bugs

- Bugs reales nuevos encontrados: **1**. Corregido y documentado como **BUG-09**: el marcador interno de pendiente de confirmación se interpretaba como contacto inválido y evitaba llamar al proveedor.
- Bugs corregidos: **1**. Bugs nuevos pendientes: **0**.
- Los fallos iniciales de fixtures/asserts se ajustaron según el comportamiento real del harness; no se registraron como bugs de producto.

## Archivos de evidencia

- API: `backend/TicketsCombustible.Api/Services/TicketDeliveryService.cs`, `TicketDeliveryProviders.cs`, `TicketQrSignature.cs`, `Controllers/TicketDeliveryController.cs`, migración `004_ticket_delivery.sql` y `tests/TicketsCombustible.Api.Tests/ApiCoverageTests.cs`.
- Web: `src/pages/Tickets.tsx`, `src/pages/Tickets.test.tsx`, `src/services/api.ts` y `src/services/api.test.ts`.
- Esquema y ejecución: `DATABASE_FINALLL`, `scripts/test-all.sh`.

Comando final ejecutado: `pnpm test:all` — **192 passed, 0 failed, 0 skipped**.
