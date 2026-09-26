# Informe de bugs QA

Todos los flujos se validaron en una instancia PostgreSQL temporal con datos de prueba sintéticos. No se conectó producción ni se usaron credenciales personales.

## BUG-01

- **Severidad:** HIGH
- **Requisito:** RF-07, RS-04
- **Componente:** API, emisión y validación de QR
- **Descripción:** La huella anterior se calculaba con UUID de ticket, solicitud, token QR y secreto. No quedaban ligados a la firma el empleado, vehículo, departamento, combustible, cantidad autorizada ni fechas; alterar uno de esos datos persistidos no cambiaba el resultado esperado de la firma.
- **Pasos para reproducir:** emitir un ticket válido; cambiar `tickets.cantidad_autorizada_galones` en una base local; validar el mismo token QR.
- **Resultado esperado:** el QR se rechaza al variar cualquier dato protegido.
- **Resultado obtenido:** la fórmula anterior no incluía la cantidad; la nueva regresión altera el valor y comprueba que `valido` sea `false`.
- **Causa:** el hash autentificaba solo una fracción del ticket.
- **Corrección:** emitir y comprobar un HMAC-SHA-256 sobre token y campos de identidad/cantidad/fechas con la clave configurada; normalizar fechas a la precisión de PostgreSQL.
- **Regression test:** `ApiIntegrationTests.Solicitud_aprobacion_ticket_qr_y_persistencia_conservan_el_flujo`.
- **Estado:** Corregido y verificado. Los tickets firmados con el formato anterior dejan de validar; requiere reemisión controlada si existe una base con tickets activos.

## BUG-02

- **Severidad:** HIGH
- **Requisito:** RF-12, RF-13
- **Componente:** PWA móvil → `POST /api/despachos`
- **Descripción:** El cliente móvil no mandaba `identidadConfirmada`; la API requiere el valor `true`, así que rechazaba siempre el despacho móvil normal. Además, el cliente intentaba parsear el error de texto como JSON y la pantalla ocultaba la causa.
- **Pasos para reproducir:** validar QR desde el cliente; enviar despacho por el servicio móvil previo; observar que el body no contiene `identidadConfirmada` y que la API responde 400.
- **Resultado esperado:** confirmación explícita en la UI y body booleano requerido; presentar el mensaje del API cuando falle.
- **Resultado obtenido:** el servicio actualizado incluye el campo tras una casilla obligatoria; la prueba valida el body y valida que un HTTP 409 de texto se presenta como error legible.
- **Causa:** el DTO móvil quedó desactualizado respecto al contrato de la API.
- **Corrección:** casilla de verificación de identidad, transmisión de `identidadConfirmada`, lectura tolerante de JSON/texto y propagación del error HTTP.
- **Regression test:** `app-movil/src/services/ticketService.test.js` (contrato de despacho y error HTTP).
- **Estado:** Corregido y verificado a nivel de servicio móvil; el escaneo real de cámara no tiene E2E automatizado.

## BUG-03

- **Severidad:** MEDIUM
- **Requisito:** RF-10, RF-13
- **Componente:** PWA móvil → lista de tickets
- **Descripción:** La app esperaba `id` secuencial y `cantidadAutorizada`; la respuesta real de la API web entrega UUID en `id`, `numeroSecuencial` y `cantidadAutorizadaGalones`. La lista enseñaba UUID y cantidad `undefined`.
- **Pasos para reproducir:** consultar `GET /api/tickets` y renderizar la respuesta sin normalización en `consultaTickets.js`.
- **Resultado esperado:** mostrar número secuencial y cantidad autorizada usando el contrato real.
- **Resultado obtenido:** el servicio ahora adapta esos campos y conserva el UUID por separado.
- **Causa:** dos interfaces de cliente divergieron sin una adaptación del contrato.
- **Corrección:** normalizar `id`, `uuid` y `cantidadAutorizada` en `consultarTickets()`.
- **Regression test:** `app-movil/src/services/ticketService.test.js` (adaptación de lista).
- **Estado:** Corregido y verificado.

## BUG-04

- **Severidad:** MEDIUM
- **Requisito:** RF-12, RF-14, RF-15
- **Componente:** API de despacho y PostgreSQL
- **Descripción:** Dos operadores pueden validar QR en cachés de usuario diferentes y competir por insertar el mismo ticket. La restricción única de PostgreSQL impide el doble registro, pero el controlador no traducía esa violación esperada a un rechazo de negocio.
- **Pasos para reproducir:** dos operadores validan el mismo token; envían simultáneamente un despacho. La carrera llega a la restricción única `despachos_id_ticket_key`.
- **Resultado esperado:** un único despacho exitoso y un conflicto controlado para el otro operador; una sola salida de inventario.
- **Resultado obtenido:** prueba concurrente final observa exactamente un HTTP 200, un HTTP 409, dos despachos totales para dos tickets distintos, y una sola salida para el ticket disputado.
- **Causa:** la API no gestionaba la excepción de unicidad de un ticket ya consumido por otra transacción.
- **Corrección:** capturar únicamente la violación única de `despachos_id_ticket_key` y responder 409 Conflict.
- **Regression test:** `ApiIntegrationTests.Despacho_y_recepcion_actualizan_existencia_y_movimientos_persistidos`.
- **Estado:** Corregido; carrera verificada después del cambio. La base ya protegía de forma efectiva contra dos filas/descuentos del mismo ticket.

## Riesgo pendiente: firma de tickets antiguos

La firma robusta cambia el contenido cubierto; no existe columna de versión ni migración/reemisión de QR previos. El esquema QA estaba recién cargado y no contenía tickets antiguos. Antes de aplicar el cambio sobre una base con tickets vigentes, define cómo reemitirlos o invalidarlos. No se hizo una migración de datos.
