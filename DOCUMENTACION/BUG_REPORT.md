> Nota de rutas: las rutas históricas de este informe describen fielmente el repositorio en la fecha de cada hallazgo. El repositorio se reorganizó después; el código actual está bajo `APPMOBILE/`, `APPWEB/`, `BACKEND/` y `BASEDATOS/`.

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
- **Estado:** Corregido y verificado a nivel de servicio móvil; el cierre de RF-13 añade E2E Chromium de cámara y despacho en `e2e/pwa-dispatch.spec.js`.

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

## BUG-05

- **Severidad:** HIGH
- **Requisito:** RF-12, RF-14, RF-15
- **Componente:** API de despacho e inventario PostgreSQL
- **Descripción:** Dos despachos simultáneos de tickets distintos contra el mismo tanque podían leer el mismo stock y competir dentro de los triggers, causando deadlock (`40P01`) en vez de completar uno y rechazar el que agotaba el inventario.
- **Pasos para reproducir:** fijar existencia inicial en 100 galones; enviar simultáneamente dos despachos por 60 y 50 galones desde operadores distintos.
- **Resultado esperado:** un despacho exitoso, el otro rechazado por inventario insuficiente y existencia nunca negativa.
- **Resultado obtenido:** la primera prueba ampliada reprodujo el deadlock. Con el arreglo, la prueba deja existencia en 40 o 50 galones, registra un único despacho y movimiento, y devuelve un conflicto al segundo operador.
- **Causa:** falta de serialización de las operaciones que compiten por un ticket y un tanque.
- **Corrección:** bloquear filas en orden estable ticket→tanque y leer el stock actualizado bajo bloqueo; convertir la regla de stock insuficiente a HTTP 409.
- **Regression test:** `ApiCoverageTests.Despachos_simultaneos_con_stock_limitado_no_producen_stock_negativo_ni_lost_update`.
- **Estado:** Corregido y verificado en PostgreSQL temporal.

## BUG-06

- **Severidad:** MEDIUM
- **Requisito:** RF-02, RF-03, RF-04
- **Componente:** API de gestión de catálogos
- **Descripción:** Duplicar código/nombre de departamento, código/cédula de empleado o placa/ficha de vehículo podía dejar que la restricción SQL respondiera con una excepción no controlada.
- **Resultado esperado:** rechazar el dato duplicado con conflicto de negocio y validar campos/referencias antes de guardar.
- **Resultado obtenido:** los nuevos casos de duplicado reciben HTTP 409 y las referencias/campos inválidos HTTP 400; las ediciones que conservan el valor del mismo registro continúan aceptándose.
- **Causa:** las acciones de catálogo no comprobaban todas las claves naturales ni los campos requeridos.
- **Corrección:** validaciones explícitas de duplicados, campos y departamento asociado en alta/edición.
- **Regression test:** `ApiCoverageTests.Catalogos_departamentos_empleados_y_vehiculos_validan_duplicados_edicion_y_bajas_logicas`.
- **Estado:** Corregido y verificado.

## BUG-07

- **Severidad:** MEDIUM
- **Requisito:** RF-14, RF-16
- **Componente:** API de ajustes, recepciones e inventario PostgreSQL
- **Descripción:** Las excepciones `P0001` de los triggers por capacidad máxima o stock insuficiente no se convertían en una respuesta de negocio controlada.
- **Resultado esperado:** rechazar el movimiento sin persistir recepción/movimiento parcial y responder HTTP 409.
- **Resultado obtenido:** exceso de capacidad y ajuste por encima de stock responden conflicto; la prueba confirma que no queda recepción ni movimiento parcial y el stock conserva su valor tras el rechazo.
- **Causa:** los controladores no traducían las reglas de integridad expresadas por los triggers PostgreSQL.
- **Corrección:** capturar la excepción de regla en los controladores de inventario/recepción y devolver conflicto.
- **Regression test:** `ApiCoverageTests.Ajustes_y_recepciones_rechazan_limites_y_preservan_movimientos`.
- **Estado:** Corregido y verificado.

## Riesgo pendiente: firma de tickets antiguos

La firma robusta cambia el contenido cubierto; no existe columna de versión ni migración/reemisión de QR previos. El esquema QA estaba recién cargado y no contenía tickets antiguos. Antes de aplicar el cambio sobre una base con tickets vigentes, define cómo reemitirlos o invalidarlos. No se hizo una migración de datos.

## Prioridad 1 — Auditoría

La ejecución integral finalizó con 75/75 pruebas pasando, incluidos 39 tests API (los 38 casos de regresión anteriores y el nuevo test de auditoría). No se identificaron bugs adicionales del producto durante esta fase. Dos fallas iniciales del test nuevo eran expectativas inválidas del arnés (`TestServer` no expone una IP remota y EF no persiste entidades no rastreadas); se corrigió la prueba y se verificó en PostgreSQL el rechazo de UPDATE/DELETE sobre entidades rastreadas.

La auditoría queda **PARTIAL** para RF-21 y RS-06: registra los flujos existentes, elimina secretos, limita y filtra la consulta, y protege la tabla contra UPDATE/DELETE. Todavía no puede registrar anulación de tickets ni cierre diario, porque esos flujos no existen (RF-10 y RF-18); no se comenzaron en esta fase.

## BUG-08

- **Severidad:** HIGH
- **Requisito:** RF-10
- **Componente:** API, contrato `AnularTicketRequest`
- **Descripción:** La metadata de validación del motivo aplicada sobre la propiedad posicional del record causaba una excepción en tiempo de ejecución al procesar solicitudes de anulación; la ruta devolvía 500 antes de completar el flujo.
- **Pasos para reproducir:** enviar un POST autenticado a `/api/tickets/{id}/anular` con un motivo, bajo la definición previa del DTO con atributo de validación en el parámetro posicional.
- **Resultado esperado:** validar el motivo y continuar con la transacción o devolver 400 si no es válido.
- **Resultado obtenido:** la inspección de metadata del modelo producía una excepción y respuesta 500.
- **Causa:** el atributo de validación se aplicaba al parámetro posicional del record en vez de una propiedad materializada para ASP.NET Core.
- **Corrección:** convertir `AnularTicketRequest` en record con propiedad explícita y `[StringLength(500)]`; mantener las validaciones requeridas de null, whitespace y longitud en el endpoint.
- **Regression test:** `ApiCoverageTests.Anulacion_rechaza_motivo_nulo_vacio_o_espacios`, `ApiCoverageTests.Anulacion_rechaza_motivo_mayor_a_500_caracteres` y `ApiCoverageTests.Anulacion_sin_JWT_responde_401`.
- **Estado:** Corregido y verificado; la suite completa pasa.

## Continuación de QA — RF-18

En la fase de RF-18 no se encontró un bug nuevo de producto que requiriera BUG-09. Los fallos durante las primeras ejecuciones fueron expectativas/configuración de fixtures y se corrigieron antes de la corrida final; no se registran como defectos. Se añadieron regresiones para cierre duplicado y concurrente, movimiento contra cierre, cálculo físico/teórico, límites UTC, permisos, rollback y PDF. Resultado de `pnpm test:all`: 141/141, sin fallos ni omitidos.

## BUG-09

- **Severidad:** HIGH
- **Requisito:** RF-09, RF-10
- **Componente:** API, `TicketDeliveryService`
- **Descripción:** El marcador interno `PENDIENTE_CONFIRMACION` de un destino validado se interpretaba como error de contacto. Las solicitudes guardaban un intento fallido y nunca invocaban el proveedor, incluso con correo/teléfono válidos.
- **Pasos para reproducir:** emitir un ticket con un correo válido, autenticarse como ADMINISTRADOR/SUPERVISOR y solicitar `POST /api/tickets/{id}/enviar` con canal `EMAIL`.
- **Resultado esperado:** registrar la reserva, invocar el proveedor y persistir el resultado recibido.
- **Resultado obtenido:** el intento se cerraba como `FALLIDO` con mensaje de correo inválido y el fake del proveedor no recibía la solicitud.
- **Causa:** el estado de marcador `PENDIENTE_CONFIRMACION` no nulo se comprobaba con un `Resultado is not null`, aunque solo los marcadores `DESTINATARIO_INVALIDO` y `TELEFONO_INVALIDO` indican realmente validación fallida.
- **Corrección:** comprobar explícitamente los dos marcadores de contacto inválido y conservar el marcador de confirmación para timeout/crash recuperable.
- **Regression test:** `ApiCoverageTests.Envio_email_entrega_qr_firmado_persiste_estado_historial_y_auditoria_y_es_idempotente`, `Envio_sms_usa_url_https_con_token_qr_existente_y_no_hace_request_externo_en_test`, `Envio_ambos_con_un_fallo_mantiene_pendiente_y_reintenta_solo_el_canal_fallido`, y timeout/reconciliación.
- **Estado:** Corregido y verificado en la suite integral `pnpm test:all` (192/192, 0 omitidos).

## Continuación de QA — RF-09

Se ejecutó `pnpm test:all`: 192/192 pruebas pasan y 0 omitidas. No quedan bugs nuevos pendientes. SMTP y SMS se reemplazaron por fakes durante las pruebas.

## BUG-10

- **Severidad:** HIGH
- **Requisito:** RF-11
- **Componente:** API, consulta de ejecuciones vencidas del scheduler PostgreSQL
- **Reproducción:** crear una programación válida con fecha inicial UTC, avanzar el `TimeProvider` a esa fecha y ejecutar `ISolicitudProgramacionProcessor.ProcessDueAsync`.
- **Resultado esperado:** seleccionar la programación vencida y generar su solicitud dentro de la transacción.
- **Resultado obtenido:** Npgsql rechazaba el parámetro de fecha por una incompatibilidad entre `DateTimeKind.Unspecified` y `timestamp with time zone`; el worker no generaba la solicitud.
- **Causa:** el parámetro de una consulta SQL interpolada no fijaba explícitamente el tipo `timestamp without time zone` usado por `proxima_ejecucion`.
- **Corrección:** enlazar los parámetros `now` y `batchLimit` con tipos Npgsql explícitos; se conserva el instante UTC representado como timestamp sin zona.
- **Regression test:** `ApiCoverageTests.Programacion_recurrente_genera_solicitud_pendiente_auditoria_y_salta_periodos_atrasados`, `Programacion_automatica_es_unica_y_dos_workers_no_generan_doble_solicitud`, `Programacion_mensual_conserva_el_ancla_en_fin_de_mes_y_ano_bisiesto` y los demás tests del procesador.
- **Estado:** Corregido y verificado en `pnpm test:all` (213/213, 0 omitidos).

## BUG-12

- **Severidad:** MEDIUM
- **Requisito:** RF-11
- **Componente:** Web, fecha sugerida de nueva programación
- **Reproducción:** abrir Nueva programación en un navegador cuya zona local no sea UTC.
- **Resultado esperado:** la fecha sugerida representa aproximadamente un minuto después del instante actual.
- **Resultado obtenido:** la conversión local se aplicaba dos veces y desplazaba el valor sugerido por varias horas.
- **Causa:** `defaultStart` devolvía un valor ya formateado para `datetime-local`, que luego volvía a pasar por el convertidor de instante a hora local.
- **Corrección:** `defaultStart` devuelve ISO UTC; el campo convierte ese instante a hora local una sola vez.
- **Regression test:** `ScheduleManager.test.tsx` comprueba que la hora sugerida permanezca entre 50 y 75 segundos después de ahora.
- **Estado:** Corregido y verificado en `pnpm test:all` (213/213, 0 omitidos).

## Continuación de QA — RF-11

La primera validación de RF-11 terminó en 212/212. La corrida final, tras incluir recuperación de automáticas fallidas y corregir la zona horaria sugerida en el formulario, terminó en 213/213, sin fallos ni omitidos. Se encontraron y corrigieron BUG-10, BUG-11 y BUG-12. No quedan bugs nuevos pendientes de RF-11. Los avisos EF1002 observados están en un fixture de fecha UTC preexistente; no son defectos del producto.

## BUG-11

- **Severidad:** MEDIUM
- **Requisito:** RF-11
- **Componente:** API, reactivación de programaciones automáticas fallidas
- **Reproducción:** ejecutar una programación AUTOMATICA con una referencia inactiva y luego corregir esa referencia e intentar reactivar.
- **Resultado esperado:** permitir un nuevo intento registrado después de corregir el catálogo, manteniendo la ejecución fallida en el historial.
- **Resultado obtenido:** la política inicial bloqueaba toda reactivación con historial, incluidas las fallidas, e impedía recuperarse.
- **Causa:** la condición de ejecución única no distinguía una solicitud generada con éxito de un intento fallido.
- **Corrección:** bloquear reactivación solo si ya existe ejecución `GENERADA`; programar los reintentos de ejecuciones fallidas para un instante distinto, conservando la clave única y el historial.
- **Regression test:** `ApiCoverageTests.Programacion_automatica_fallida_se_puede_reactivar_tras_corregir_referencia`.
- **Estado:** Corregido y verificado en `pnpm test:all` (213/213, 0 omitidos).

## Continuación RF-23 — notificaciones

Durante RF-23 se revisaron los fallos definitivos de entrega, las transacciones que modifican stock, la deduplicación concurrente y el aislamiento por usuario. Se encontró y corrigió el defecto de fecha temporal documentado como BUG-13. La falla inicial de la prueba de fecha sugerida fue una medición sensible a segundos reales en un campo que solo guarda minutos; se fijó el reloj del test conservando el rango y los asserts. Los resultados finales quedan en `TEST_REPORT.md`.

## BUG-13

- **Severidad:** HIGH
- **Requisito:** RF-23, RF-09, RF-14
- **Componente:** API, persistencia de notificaciones en PostgreSQL
- **Descripción:** crear una notificación desde un resultado definitivo de entrega o un cambio de inventario fallaba por incompatibilidad de tipo temporal, devolviendo una excepción y revirtiendo la operación transaccional.
- **Reproducción:** ejecutar un ajuste que cruce el nivel crítico o registrar un resultado FALLIDO definitivo de correo/SMS con destinatario ADMINISTRADOR/SUPERVISOR.
- **Resultado esperado:** guardar operación de negocio, auditoría y notificación atómicamente con fecha UTC.
- **Resultado obtenido:** Npgsql rechazaba un `DateTimeKind.Unspecified` enviado como `timestamp with time zone`, aunque `fecha_creacion` es `timestamp without time zone`.
- **Causa:** el insert SQL interpolado no convertía explícitamente el instante UTC al tipo temporal almacenado por el esquema.
- **Corrección:** pasar un instante UTC y convertirlo con `timezone('UTC', ...)` al insertar el timestamp sin zona; la notificación comparte la transacción de la operación.
- **Regression test:** `ApiCoverageTests.Fallos_de_proveedor_se_persisten_sanitizados_sin_marcar_enviado`, `Inventario_bajo_crea_un_aviso_por_episodio_y_los_ajustes_se_enlazan_al_movimiento` y `Fallo_de_auditoria_revierte_ajuste_y_notificaciones_asociadas`.
- **Estado:** Corregido; verificado en la suite integral RF-23.

## BUG-14

- **Severidad:** MEDIUM
- **Requisito:** RF-19, RF-20
- **Componente:** API de reportes de movimientos
- **Reproducción:** consultar `GET /api/reportes?tipo=movimientos` con movimientos persistidos.
- **Resultado esperado:** responder con filas, totales y agregaciones válidas para el informe.
- **Resultado obtenido:** EF Core no traducía el filtrado/agregado de campos del DTO proyectado y lanzaba una excepción, que terminaba como HTTP 500.
- **Causa:** aplicar agrupaciones y conteo condicional directamente sobre una proyección `ReporteFila` no era traducible por el proveedor PostgreSQL.
- **Corrección:** agregar combustible y tipo de movimiento sobre las entidades SQL; los campos no aplicables a movimientos quedan vacíos, y su total de tickets se fija en cero.
- **Regression test:** `ApiCoverageTests.Reporte_de_movimientos_es_paginado_y_el_PDF_de_muchos_registros_tiene_varias_paginas`.
- **Estado:** Corregido y verificado en `pnpm test:all` (248/248, 0 omitidos).

## BUG-15

- **Severidad:** HIGH
- **Requisito:** RF-06, RF-07, RF-24, RS-02
- **Componente:** API, `POST /api/tickets`
- **Reproducción:** emitir un ticket para una solicitud aprobada y revisar el JSON de respuesta.
- **Resultado esperado:** devolver los campos del recibo necesarios para consultar el ticket; mantener el token QR firmado y su hash privado.
- **Resultado obtenido:** la acción devolvía la entidad EF `Ticket` completa, incluyendo `QrToken` y `QrHash`.
- **Causa:** la respuesta `CreatedAtAction` serializaba directamente la entidad persistida.
- **Corrección:** devolver una proyección explícita de recibo que omite el token y la firma internos. La lectura del ticket también mantiene su DTO reducido; la generación de PNG continúa mediante endpoints QR autorizados o el QR público firmado ya existente.
- **Regression test:** `ApiIntegrationTests.Solicitud_aprobacion_ticket_qr_y_persistencia_conservan_el_flujo`, `ApiIntegrationTests.Despacho_y_recepcion_actualizan_existencia_y_movimientos_persistidos` y los casos de emisión de `ApiCoverageTests` comprueban que `qrToken` y `qrHash` no salen en la respuesta y que el QR sigue validándose.
- **Estado:** Corregido y verificado por `pnpm test:all` en la fase RF-24/RS-02 (252/252, 0 omitidos).

## BUG-16

- **Severidad:** MEDIUM
- **Requisito:** RF-24, RF-13, RF-14
- **Componente:** API, altas de catálogos (`/api/gestion/*` y `/api/recepciones/proveedores`)
- **Reproducción:** enviar a los endpoints de creación entidades JSON con `id` explícito; para tanques añadir `existenciaActualGalones`, `notificacionBajoActiva` y `numeroEpisodioBajo`.
- **Resultado esperado:** aceptar solo campos propios del contrato de alta, generar la clave en la base y crear un tanque vacío con contadores internos iniciales.
- **Resultado obtenido:** varios controladores recibían entidades EF completas; la solicitud podía establecer la clave primaria y campos de control internos de tanque.
- **Causa:** model binding directo de entidades persistidas en acciones HTTP de escritura.
- **Corrección:** introducir DTOs de alta/edición explícitos para departamentos, empleados, vehículos, estaciones, tanques y proveedores; proyectar respuestas sin estado interno del tanque. El nivel inicial se fija en cero y la base genera los IDs.
- **Regression test:** `ApiEndpointMatrixTests.Catalogos_ignoran_ids_y_estado_interno_en_los_contratos_de_creacion` intenta sobreescribir IDs y atributos internos y verifica respuesta y persistencia.
- **Estado:** Corregido y verificado por `pnpm test:all` en la fase RF-24/RS-02 (252/252, 0 omitidos).

## BUG-17

- **Severidad:** HIGH
- **Requisito:** RS-01
- **Componente:** API, `POST /api/login/refresh`
- **Reproducción:** iniciar sesión y rotar una vez el refresh token contra PostgreSQL temporal.
- **Resultado esperado:** persistir el token sustituto y enlazarlo con el token rotado dentro de una sola transacción.
- **Resultado obtenido:** la primera implementación intentó actualizar la referencia `reemplazado_por_id` antes de insertar la fila sustituta; la clave foránea rechazaba el guardado y el endpoint respondía 500.
- **Causa:** EF Core no tenía una relación de navegación para ordenar el insert/update de las dos filas.
- **Corrección:** insertar primero la fila sustituta y luego revocar/enlazar la anterior; ambas escrituras permanecen dentro de la misma transacción.
- **Regression test:** `AuthSecurityTests.Refresh_rota_token_hash_y_reutilizacion_revoca_toda_la_familia`, `Dos_refresh_concurrentes_del_mismo_token_solo_rotan_una_vez_y_no_generan_500`.
- **Estado:** Corregido y verificado en PostgreSQL temporal mediante rotación secuencial y concurrente.

## BUG-18

- **Severidad:** HIGH
- **Requisito:** RS-05
- **Componente:** PWA, renderizado de tickets y mensajes
- **Reproducción:** devolver desde la API campos como `<script>alert(1)</script>` en nombre del empleado, vehículo, estado, identificador o mensaje de error que la PWA insertaba directamente en `innerHTML`.
- **Resultado esperado:** tratar los valores de la API como texto inerte.
- **Resultado obtenido:** los valores interpolados podían interpretarse como HTML; React no participa en estas vistas móviles vanilla.
- **Causa:** interpolación directa de datos no confiables en las plantillas HTML de ticket, listado, despacho y error de cámara.
- **Corrección:** agregar escape de entidades HTML para todos los valores dinámicos y limitar el nombre de clase derivado del estado a caracteres seguros.
- **Regression test:** `ticketService.test.js` comprueba escape de `<script>`, comillas y nombre de clase manipulado; revisión `rg` de todas las interpolaciones de `innerHTML` confirma escape o valor estático.
- **Estado:** Corregido y verificado; la prueba E2E de la PWA se añadió después en el cierre de RF-13.

## BUG-19

- **Severidad:** MEDIUM
- **Requisito:** RF-13
- **Componente:** PWA, formulario de despacho
- **Reproducción:** activar un lector de pantalla o localizar por nombre accesible el campo de galones y observaciones en el formulario de despacho.
- **Resultado esperado:** los textos de etiqueta identifican programáticamente los controles correspondientes.
- **Resultado obtenido:** las etiquetas visibles de esos dos campos no estaban asociadas a los inputs/textarea; el lector de pantalla y las herramientas E2E no podían encontrarlos por su etiqueta.
- **Causa:** faltaban los atributos `for` asociados a `id`.
- **Corrección:** enlazar las etiquetas con `#galones` y `#observaciones`; la casilla de identidad permanece asociada por anidación.
- **Regression test:** `e2e/pwa-dispatch.spec.js` localiza los campos con `getByLabel` y completa el despacho en Chromium.
- **Estado:** Corregido y verificado en `pnpm test:e2e` (8/8).
