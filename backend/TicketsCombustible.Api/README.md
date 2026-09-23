# TicketsCombustible.Api

API REST en .NET 8 para la Plataforma de Tickets Digitales e Inventario de Combustible.

## Configuración local

1. Crear la base `tickets_combustible` en PostgreSQL y ejecutar el script de Persona 3.
2. Copiar `appsettings.Development.example.json` como `appsettings.Development.json` y reemplazar `TU_CLAVE`.
3. Ejecutar `dotnet run` desde esta carpeta.
4. Abrir `http://localhost:5000/swagger` (el puerto exacto aparece en la terminal).

## Módulos iniciales

- `api/catalogos`: departamentos, empleados, vehículos, tipos de combustible y tanques.
- `api/solicitudes`: crear, consultar y aprobar solicitudes.
- `api/tickets`: crear, consultar y validar tickets.
- `api/despachos`: registrar un despacho validando ticket, tanque e inventario.

La base de datos genera el número correlativo del ticket y actualiza el inventario al registrar un despacho mediante sus triggers.
