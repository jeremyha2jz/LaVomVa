# LaVomVa

Plataforma web de gestión de solicitudes de combustible, tickets digitales e inventario para el proyecto académico de INTEC.

## Contenido

- `src/`: web administrativa en React, TypeScript y Vite.
- `backend/TicketsCombustible.Api/`: API .NET 8 y PostgreSQL.
- `DATABASE_FINALLL`: esquema SQL recibido para la base de datos.
- `app-movil/`: proyecto móvil independiente. No forma parte de la validación ni de los cambios de esta entrega.

## Probar la web sin base de datos

Requiere Node.js y pnpm. Desde la raíz:

```powershell
pnpm install
pnpm dev
```

Abre la dirección que indique Vite. Por defecto se usa el modo demostración con datos locales. Estos datos no se envían a PostgreSQL ni deben tratarse como reales. `pnpm build` comprueba los tipos y genera la web de producción en `dist/`.

## Conectar la web con la API

1. Instala PostgreSQL y crea la base `tickets_combustible`. Ejecuta el script `DATABASE_FINALLL` en esa base.
2. Copia `backend/TicketsCombustible.Api/appsettings.Development.example.json` a `backend/TicketsCombustible.Api/appsettings.Development.json` y reemplaza contraseña y secretos. Ese archivo local está excluido de Git.
3. Inicia la API desde `backend/TicketsCombustible.Api` con `dotnet run --launch-profile http`. Swagger queda en `http://localhost:5007/swagger`.
4. Copia `.env.example` a `.env` en la raíz y establece `VITE_USE_MOCKS=false`. Inicia la web con `pnpm dev`. El proxy de Vite envía `/api` al puerto 5007.

En modo conectado la web exige inicio de sesión. Para una prueba local, consulta los roles sembrados con `GET /api/catalogos/roles` en Swagger y crea una cuenta mediante `POST /api/gestion/usuarios`; el proyecto no incluye una contraseña predeterminada. Este alta inicial todavía está abierta en la API, por lo que **no debes exponerla en una red pública**. La API debe endurecer su autorización antes de usar datos reales.

## Estado de la integración

La web consulta catálogos, solicitudes, tickets e inventario; crea solicitudes, aprueba/rechaza, emite tickets y registra recepciones. Los reportes CSV se generan con datos visibles. El despacho conectado por número está deshabilitado: requiere la validación del QR en el flujo autorizado. La administración de usuarios, envíos de tickets, PDF, cierres y controles de seguridad avanzados aún no están completos; véase [docs/INTEGRACION_BACKEND.md](docs/INTEGRACION_BACKEND.md).

Se verificó la compilación de web y API. No se ejecutó una prueba integral con PostgreSQL local en este entorno.

## Antes de subir a GitHub

Sube los archivos fuente y los lockfiles. `.gitignore` excluye `node_modules`, `dist`, binarios de .NET, `.env` y la configuración privada de desarrollo. Revisa los cambios antes de hacer `git add`; esta carpeta no se inicializó como repositorio ni se publicó automáticamente.
