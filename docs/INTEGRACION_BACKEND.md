# Integración web/API de LaVomVa

La web usa `/api` como prefijo y Vite lo redirige a `http://localhost:5007` en desarrollo. `VITE_USE_MOCKS=true` (predeterminado) permite revisar la interfaz sin backend. Con `false`, la web exige login y usa exclusivamente datos de la API: no sustituye fallos de red por datos falsos.

| Módulo web | API utilizada | Estado |
| --- | --- | --- |
| Sesión | `POST /api/login` | Conectado |
| Catálogos | `GET /api/catalogos/{departamentos,empleados,vehiculos,tipos-combustible,tanques,estaciones}` | Consulta |
| Solicitudes | `GET/POST /api/solicitudes`, `PUT /api/solicitudes/{id}/aprobar` y `/rechazar` | Conectado |
| Tickets | `GET/POST /api/tickets`, `GET /api/tickets/{id}/qr` | Consulta y emisión |
| Inventario | `GET /api/inventario/movimientos`, `GET/POST /api/recepciones` | Consulta y recepción |
| Reportes | Datos ya cargados, exportación CSV en navegador | Parcial |
| Despacho | Sin acción web en modo conectado | Requiere validar QR en el flujo autorizado |

## Pendientes antes de usar datos reales

- Levantar PostgreSQL con el esquema `DATABASE_FINALLL` y probar el flujo completo: usuario, solicitud, aprobación, ticket, recepción y despacho.
- Aplicar autorización por roles a todos los endpoints. Hoy el JWT se emite en login, pero las rutas no están protegidas individualmente.
- Endurecer la validación criptográfica del QR y exigirla en el despacho. El endpoint actual acepta un identificador de ticket sin demostrar posesión del QR.
- Definir alta inicial segura de administrador, gestión web de usuarios/catálogos, envíos por correo o SMS, PDF, cierre diario y reportes de servidor.
- Configurar HTTPS, secretos privados, política CORS y auditoría antes de exponer el sistema fuera de localhost.

Estas tareas no quedan simuladas como funciones reales en la web. El proyecto sigue siendo una entrega académica en desarrollo, no una instalación lista para producción.
