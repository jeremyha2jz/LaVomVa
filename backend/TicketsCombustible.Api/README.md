# API LaVomVa

Backend académico en .NET 8, Entity Framework Core y PostgreSQL.

## Configuración local

1. Crea la base PostgreSQL `tickets_combustible` y ejecuta `DATABASE_FINALLL` desde la raíz del proyecto.
2. Copia `appsettings.Development.example.json` a `appsettings.Development.json`. Reemplaza contraseña de base de datos, clave JWT y secreto QR por valores privados y aleatorios; no subas este archivo a GitHub.
3. Ejecuta `dotnet restore` y `dotnet run --launch-profile http` desde esta carpeta.
4. Abre `http://localhost:5007/swagger`.

La configuración también puede proporcionarse mediante variables de entorno como `ConnectionStrings__TicketsCombustible`, `Jwt__Key` y `Qr__SigningSecret`.

## Estado

La API compila y contiene rutas de autenticación, catálogos, solicitudes, tickets, recepciones, inventario y despachos. El esquema SQL genera el correlativo y actualiza inventario mediante triggers. No hay cuenta inicial ni migración automática: se requiere preparar PostgreSQL y crear un usuario con rol para probar el modo conectado. La autorización por endpoint y otras garantías del SRS siguen pendientes; no expongas esta API públicamente ni la uses con datos sensibles hasta completarlas.
