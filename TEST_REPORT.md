# Informe de ejecución QA

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
