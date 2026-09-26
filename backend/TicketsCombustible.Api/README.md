# API LaVomVa

Backend en .NET 8, Entity Framework Core y PostgreSQL.

## Configuración local

1. Crea la base `tickets_combustible` y ejecuta `DATABASE_FINALLL` desde la raíz del proyecto.
2. Copia `appsettings.Development.example.json` a `appsettings.Development.json`. Reemplaza contraseña de PostgreSQL, clave JWT y secreto QR por valores privados; no subas este archivo a Git.
3. Configura `Bootstrap__Secret` con un valor aleatorio de al menos 32 caracteres para crear la primera cuenta administradora.
4. Ejecuta `dotnet restore` y `dotnet run --launch-profile http` desde esta carpeta. Swagger está disponible en `http://localhost:5007/swagger` durante desarrollo.
5. Haz `POST /api/login/inicializar-admin` una sola vez con `{ "secreto": "EL_SECRETO", "usuario": "admin", "correo": "admin@ejemplo.com", "nombreCompleto": "Administrador", "contrasena": "UNA_CONTRASEÑA_LARGA" }`. Luego elimina `Bootstrap__Secret` de la configuración y reinicia la API.

Las variables `ConnectionStrings__TicketsCombustible`, `Jwt__Key` y `Qr__SigningSecret` también pueden usarse en lugar de archivo local. El registro público `POST /api/login/registro` crea cuentas de solo consulta inactivas hasta que un administrador las active. Los administradores pueden crear usuarios con otros roles desde la web.

La API exige JWT en las rutas privadas y restringe las escrituras por rol. El despacho requiere una validación reciente del QR en la misma sesión; la prueba se guarda en memoria y debe migrarse a un almacén compartido antes de ejecutar varias instancias. La app móvil aún no envía `identidadConfirmada`, así que su despacho continúa pendiente de integración. No uses el sistema con datos sensibles ni lo expongas públicamente hasta resolver las brechas de seguridad restantes.
