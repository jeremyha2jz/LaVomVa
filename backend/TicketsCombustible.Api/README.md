# API LaVomVa

Backend en .NET 8, Entity Framework Core y PostgreSQL.

## Configuración local

1. Crea la base `tickets_combustible` y ejecuta `DATABASE_FINALLL` desde la raíz del proyecto.
2. Copia `appsettings.Development.example.json` a `appsettings.Development.json`. Reemplaza contraseña de PostgreSQL, clave JWT y secreto QR por valores privados; no subas este archivo a Git.
3. Configura `Bootstrap__Secret` con un valor aleatorio de al menos 32 caracteres para crear la primera cuenta administradora.
4. Ejecuta `dotnet restore` y `dotnet run --launch-profile http` desde esta carpeta. Swagger está disponible en `http://localhost:5007/swagger` durante desarrollo.
5. Haz `POST /api/login/inicializar-admin` una sola vez con `{ "secreto": "EL_SECRETO", "usuario": "admin", "correo": "admin@ejemplo.com", "nombreCompleto": "Administrador", "contrasena": "UNA_CONTRASEÑA_LARGA" }`. Luego elimina `Bootstrap__Secret` de la configuración y reinicia la API.

## Programaciones de solicitudes (RF-11)

Después de cargar `DATABASE_FINALLL`, aplica en orden las migraciones SQL `001` a `006` de `Migrations/`. La migración `005_solicitud_scheduling.sql` agrega plantillas e historial de programaciones; `006_persistent_notifications.sql` completa la tabla existente `notificaciones` y añade el estado de episodios de inventario crítico.

`POST /api/programaciones`, `GET /api/programaciones`, `GET /api/programaciones/{id}`, `PUT /api/programaciones/{id}`, `POST /api/programaciones/{id}/activar`, `POST /api/programaciones/{id}/desactivar` y `GET /api/programaciones/{id}/ejecuciones` requieren el rol ADMINISTRADOR o SUPERVISOR. La web administra estas programaciones desde Solicitudes. El flujo manual existente se mantiene en `POST /api/solicitudes`; ese endpoint solo acepta `MANUAL`.

Una programación `AUTOMATICA` produce una solicitud una sola vez. Una `RECURRENTE` admite `DIARIA`, `SEMANAL` o `MENSUAL`. Todas las fechas se normalizan a UTC. Las fechas mensuales se calculan desde la fecha inicial original, por lo que una programación iniciada el día 31 puede ejecutarse el último día de febrero y volver al día 31 en marzo, sin desplazarse mes a mes. La fecha de fin es inclusiva.

El `SolicitudProgramacionWorker` procesa un lote al iniciar la aplicación y luego utiliza `Scheduling:IntervalSeconds` (variable de entorno `Scheduling__IntervalSeconds`); el valor predeterminado es 60 segundos y se limita al rango 15–86400. Al recuperarse de una caída se procesa como máximo una ocurrencia vencida por programación y se omiten los períodos ya perdidos. Cada solicitud generada queda `PENDIENTE` para aprobación normal. El selector usa transacción y bloqueo `FOR UPDATE SKIP LOCKED`; una clave única de programación+fecha protege el historial frente a workers concurrentes. Las asociaciones se revalidan en cada ejecución y una referencia inválida crea un resultado `FALLIDA` y pausa la programación para revisión. Tras reparar la referencia, una automática fallida se puede reactivar para un nuevo intento registrado; si ya generó su solicitud, permanece como ejecución única.

## Notificaciones persistentes (RF-23)

La API ofrece `GET /api/notificaciones` (filtros `tipo`, `leida`, `desde`, `hasta`, `pagina`, `tamano`), `GET /api/notificaciones/no-leidas`, `POST /api/notificaciones/{id}/leer` y `POST /api/notificaciones/leer-todas`. Todas las consultas y mutaciones están acotadas al usuario del JWT. ADMINISTRADOR, SUPERVISOR, DESPACHADOR y AUDITOR pueden consultar sus propias notificaciones; únicamente los primeros tres roles reciben eventos operativos. ADMINISTRADOR y SUPERVISOR reciben todos los tipos; DESPACHADOR recibe vencimientos e inventario crítico. AUDITOR es de solo lectura y no se fan-out de alertas operativas.

La notificación de ticket reutiliza el umbral de dos días de `TicketLifecycleService`. El worker existente evalúa tickets al iniciar y cada `Scheduling:IntervalSeconds`; una restricción única por destinatario+clave idempotente evita duplicados incluso con dos procesos concurrentes. El inventario crea una alerta al cruzar a crítico, conserva el episodio mientras siga bajo y lo reinicia al recuperarse. Fallos definitivos de correo/SMS y movimientos AJUSTE_POSITIVO, AJUSTE_NEGATIVO o MERMA quedan relacionados con su fila de envío/movimiento. Resultados de proveedor inciertos no generan fallo definitivo.

La campana se sincroniza con REST al abrirla, al volver al foco y cada 30 segundos. RF-15 todavía no ofrece SignalR/SSE, por lo que el servidor no envía push al instante; los registros persistidos se recuperan por API y se deduplican por ID en el navegador.

Las variables `ConnectionStrings__TicketsCombustible`, `Jwt__Key` y `Qr__SigningSecret` también pueden usarse en lugar de archivo local. El registro público `POST /api/login/registro` crea cuentas de solo consulta inactivas hasta que un administrador las active. Los administradores pueden crear usuarios con otros roles desde la web.

La API exige JWT en las rutas privadas y restringe las escrituras por rol. El despacho requiere una validación reciente del QR en la misma sesión; la prueba se guarda en memoria y debe migrarse a un almacén compartido antes de ejecutar varias instancias. La app móvil aún no envía `identidadConfirmada`, así que su despacho continúa pendiente de integración. No uses el sistema con datos sensibles ni lo expongas públicamente hasta resolver las brechas de seguridad restantes.
