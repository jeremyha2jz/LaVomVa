# Inventario de API y matriz RBAC

Inventario generado contra los 15 controladores y contrastado en pruebas con `IActionDescriptorCollectionProvider`. Cada fila de ruta MVC requiere clasificación explícita en `ApiEndpointMatrixTests`; la prueba falla si aparece o desaparece una acción. La matriz aplica las políticas actuales del código, no deduce acceso a partir de nombres de rol.

## Resumen

- Rutas MVC de negocio: **71**.
- Públicas por diseño: **6** (`POST /api/login`, `POST /api/login/registro`, `POST /api/login/inicializar-admin`, `POST /api/login/refresh`, `POST /api/login/logout`, `GET /api/tickets/public/qr`). Refresh y logout solo aceptan credenciales de sesión en el body y responden sin caché.
- Protegidas: **65**; de ellas, **46** tienen roles restringidos y **19** admiten a cualquiera de los seis roles autenticados.
- SeñalR: `/hubs/inventory` y su transporte `POST /hubs/inventory/negotiate` son rutas técnicas protegidas por `[Authorize]`; no son acciones REST MVC.
- Swagger/UI y `/swagger/v1/swagger.json` solo se registran en `Development`; `Testing` no registra health checks ni otras rutas técnicas HTTP.
- Roles sembrados por `DATABASE_FINALLL`: `ADMINISTRADOR`, `SUPERVISOR`, `DESPACHADOR`, `SOLICITANTE`, `AUDITOR`, `CONSULTA`.
- Todas las rutas protegidas: sin JWT -> 401. Rol no permitido -> 403. Para cada uno de los 6 roles se ejecuta una solicitud autenticada por ruta; el rol permitido debe pasar autorización y no puede recibir 401, 403 ni 5xx. La prueba da tolerancia a respuestas funcionales 2xx/400/404/409 según el recurso o cuerpo del caso.

`*` significa que cualquiera de los seis roles puede acceder tras autenticarse. Las listas son roles autorizados explícitos. Las rutas anónimas se indican como `Pública`.

## Rutas REST

| Método | Ruta | Controlador / acción | Auth / roles | Request | Respuesta principal y estados documentados | Requisito |
|---|---|---|---|---|---|---|
| GET | `/api/auditoria` | Auditoria / Consultar | ADMINISTRADOR, AUDITOR | query: filtros/paginación | JSON paginado; 200, 400 | RS-06 |
| POST | `/api/login` | Auth / Login | Pública | `LoginRequest` | token de sesión; 200, 400, 401, 403 | RS-02 |
| POST | `/api/login/registro` | Auth / Registro | Pública | `RegistroRequest` | mensaje; 200, 400, 409, 500 | RF-01 |
| POST | `/api/login/inicializar-admin` | Auth / InicializarAdmin | Pública + secreto bootstrap | `InicializarAdminRequest` | mensaje; 200, 400, 401, 404, 409, 500 | RF-01, RS-02 |
| POST | `/api/login/refresh` | Auth / Refresh | Pública + refresh token en body | `TokenRequest` | access token breve + refresh rotado; 200, 401; `Cache-Control: no-store` | RS-01 |
| POST | `/api/login/logout` | Auth / Logout | Pública + refresh token en body | `TokenRequest` | 204 idempotente; revoca la familia de sesión | RS-01 |
| POST | `/api/login/logout-all` | Auth / LogoutAll | * | ninguno | 204; revoca todas las sesiones del usuario | RS-01 |
| POST | `/api/login/cambiar-contrasena` | Auth / ChangePassword | * | `ChangePasswordRequest` | 204, 400; cambia clave y revoca todas las sesiones | RS-01 |
| GET | `/api/catalogos/departamentos` | Catalogos / Departamentos | * | ninguno | departamentos activos; 200 | RF-02, RF-04 |
| GET | `/api/catalogos/empleados` | Catalogos / Empleados | * | ninguno | empleados activos; 200 | RF-02 |
| GET | `/api/catalogos/vehiculos` | Catalogos / Vehiculos | * | ninguno | vehículos activos; 200 | RF-03 |
| GET | `/api/catalogos/tipos-combustible` | Catalogos / TiposCombustible | * | ninguno | combustibles activos; 200 | RF-12 |
| GET | `/api/catalogos/tanques` | Catalogos / Tanques | * | ninguno | tanques activos; 200 | RF-13, RF-14 |
| GET | `/api/catalogos/estaciones` | Catalogos / Estaciones | * | ninguno | estaciones activas; 200 | RF-18 |
| GET | `/api/catalogos/roles` | Catalogos / Roles | * | ninguno | roles activos; 200 | RS-02 |
| GET | `/api/cierres-diarios/resumen` | CierresDiarios / Resumen | ADMINISTRADOR, SUPERVISOR, DESPACHADOR, AUDITOR | `estacionId`, `fecha` | resumen persistido/calculado; 200, 400, 404 | RF-18 |
| POST | `/api/cierres-diarios` | CierresDiarios / Crear | ADMINISTRADOR, SUPERVISOR, DESPACHADOR | `CrearCierreDiarioRequest` | cierre; 201, 400, 404, 409 | RF-18 |
| GET | `/api/cierres-diarios` | CierresDiarios / Listar | ADMINISTRADOR, SUPERVISOR, DESPACHADOR, AUDITOR | filtros fecha/estación/usuario | lista; 200, 400 | RF-18 |
| GET | `/api/cierres-diarios/{id}` | CierresDiarios / Obtener | ADMINISTRADOR, SUPERVISOR, DESPACHADOR, AUDITOR | id | cierre; 200, 404 | RF-18 |
| GET | `/api/cierres-diarios/{id}/pdf` | CierresDiarios / Pdf | ADMINISTRADOR, SUPERVISOR, DESPACHADOR, AUDITOR | id | `application/pdf`; 200, 404 | RF-18 |
| POST | `/api/despachos` | Despachos / Registrar | ADMINISTRADOR, DESPACHADOR | `RegistrarDespachoRequest` | despacho; 200, 400, 404, 409 | RF-14 |
| POST | `/api/gestion/departamentos` | GestionCatalogos / CrearDepartamento | ADMINISTRADOR, SUPERVISOR | `DepartamentoRequest` | DTO de departamento; 201, 400, 409 | RF-04 |
| PUT | `/api/gestion/departamentos/{id}` | GestionCatalogos / EditarDepartamento | ADMINISTRADOR, SUPERVISOR | `DepartamentoRequest` | DTO de departamento; 200, 400, 404, 409 | RF-04 |
| POST | `/api/gestion/empleados` | GestionCatalogos / CrearEmpleado | ADMINISTRADOR, SUPERVISOR | `EmpleadoRequest` | DTO de empleado; 201, 400, 409 | RF-02 |
| PUT | `/api/gestion/empleados/{id}` | GestionCatalogos / EditarEmpleado | ADMINISTRADOR, SUPERVISOR | `EmpleadoRequest` | DTO de empleado; 200, 400, 404, 409 | RF-02 |
| POST | `/api/gestion/vehiculos` | GestionCatalogos / CrearVehiculo | ADMINISTRADOR, SUPERVISOR | `VehiculoRequest` | DTO de vehículo; 201, 400, 409 | RF-03 |
| PUT | `/api/gestion/vehiculos/{id}` | GestionCatalogos / EditarVehiculo | ADMINISTRADOR, SUPERVISOR | `VehiculoRequest` | DTO de vehículo; 200, 400, 404, 409 | RF-03 |
| POST | `/api/gestion/estaciones` | GestionCatalogos / CrearEstacion | ADMINISTRADOR, SUPERVISOR | `EstacionRequest` | DTO de estación; 201, 400 | RF-18 |
| POST | `/api/gestion/tanques` | GestionCatalogos / CrearTanque | ADMINISTRADOR, SUPERVISOR | `TanqueRequest` | DTO de tanque; existencias forzadas a cero; 201, 400 | RF-13 |
| DELETE | `/api/gestion/{tipo}/{id}` | GestionCatalogos / Desactivar | ADMINISTRADOR, SUPERVISOR | tipo + id | baja lógica; 204, 400, 404 | RF-02, RF-03, RF-04 |
| GET | `/api/inventario` | Inventario / Consultar | * | ninguno | existencias por tanque; 200 | RF-13 |
| GET | `/api/inventario/movimientos` | Inventario / Movimientos | * | `tanqueId` opcional | últimos 100 movimientos; 200 | RF-14 |
| POST | `/api/inventario/ajustes` | Inventario / Ajustar | ADMINISTRADOR, SUPERVISOR | `AjusteInventarioRequest` | movimiento; 201, 400, 404, 409 | RF-14 |
| GET | `/api/notificaciones` | Notificaciones / Listar | ADMINISTRADOR, SUPERVISOR, DESPACHADOR, AUDITOR | `tipo`, `leida` opcionales | notificaciones del usuario autenticado; 200 | RF-23 |
| GET | `/api/notificaciones/no-leidas` | Notificaciones / NoLeidas | ADMINISTRADOR, SUPERVISOR, DESPACHADOR, AUDITOR | ninguno | notificaciones del usuario autenticado; 200 | RF-23 |
| POST | `/api/notificaciones/{id}/leer` | Notificaciones / MarcarLeida | ADMINISTRADOR, SUPERVISOR, DESPACHADOR, AUDITOR | id | notificación actualizada; 200, 404 | RF-23 |
| POST | `/api/notificaciones/leer-todas` | Notificaciones / MarcarTodasLeidas | ADMINISTRADOR, SUPERVISOR, DESPACHADOR, AUDITOR | ninguno | total actualizado; 200 | RF-23 |
| GET | `/api/programaciones` | Programaciones / Listar | ADMINISTRADOR, SUPERVISOR | ninguno | programaciones; 200 | RF-11 |
| GET | `/api/programaciones/{id}` | Programaciones / Obtener | ADMINISTRADOR, SUPERVISOR | id | programación; 200, 404 | RF-11 |
| GET | `/api/programaciones/{id}/ejecuciones` | Programaciones / Ejecuciones | ADMINISTRADOR, SUPERVISOR | id | historial; 200, 404 | RF-11 |
| POST | `/api/programaciones` | Programaciones / Crear | ADMINISTRADOR, SUPERVISOR | `CrearProgramacionSolicitudRequest` | programación; 201, 400, 404, 409 | RF-11 |
| PUT | `/api/programaciones/{id}` | Programaciones / Editar | ADMINISTRADOR, SUPERVISOR | `CrearProgramacionSolicitudRequest` | programación; 200, 400, 404, 409 | RF-11 |
| POST | `/api/programaciones/{id}/activar` | Programaciones / Activar | ADMINISTRADOR, SUPERVISOR | ninguno | programación; 200, 400, 404, 409 | RF-11 |
| POST | `/api/programaciones/{id}/desactivar` | Programaciones / Desactivar | ADMINISTRADOR, SUPERVISOR | ninguno | programación; 200, 400, 404, 409 | RF-11 |
| GET | `/api/recepciones/proveedores` | Recepciones / Proveedores | * | ninguno | proveedores activos; 200 | RF-14 |
| POST | `/api/recepciones/proveedores` | Recepciones / CrearProveedor | ADMINISTRADOR, SUPERVISOR | `CrearProveedorRequest` | proveedor; ID generado y estado activo; 201, 400 | RF-14 |
| POST | `/api/recepciones` | Recepciones / Crear | ADMINISTRADOR, SUPERVISOR | `CrearRecepcionRequest` | recepción; 201, 400, 404, 409 | RF-14 |
| GET | `/api/reportes` | Reportes / Consultar | * | filtros/paginación | reporte JSON; 200, 400 | RF-19 |
| GET | `/api/reportes/exportar` | Reportes / Exportar | * | filtros + formato | CSV/XLSX/PDF según formato; 200, 400 | RF-20 |
| GET | `/api/solicitudes` | Solicitudes / Listar | * | ninguno | solicitudes; 200 | RF-05 |
| GET | `/api/solicitudes/{id}` | Solicitudes / Obtener | * | id | solicitud; 200, 404 | RF-05 |
| POST | `/api/solicitudes` | Solicitudes / Crear | ADMINISTRADOR, SUPERVISOR, SOLICITANTE | `CrearSolicitudRequest` | solicitud; 201, 400, 409 | RF-05 |
| PUT | `/api/solicitudes/{id}/aprobar` | Solicitudes / Aprobar | ADMINISTRADOR, SUPERVISOR | `AprobarSolicitudRequest` | solicitud; 200, 400, 404, 409 | RF-05 |
| PUT | `/api/solicitudes/{id}/rechazar` | Solicitudes / Rechazar | ADMINISTRADOR, SUPERVISOR | ninguno | solicitud; 200, 404, 409 | RF-05 |
| POST | `/api/tickets/{id}/enviar` | TicketDelivery / Enviar | ADMINISTRADOR, SUPERVISOR | `EnviarTicketRequest` | resultado de envío; 200, 400, 404, 409 | RF-09 |
| POST | `/api/tickets/{id}/reenviar` | TicketDelivery / Reenviar | ADMINISTRADOR, SUPERVISOR | `EnviarTicketRequest` | resultado de envío; 200, 400, 404, 409 | RF-09 |
| GET | `/api/tickets/{id}/envios` | TicketDelivery / Historial | ADMINISTRADOR, SUPERVISOR | id | intentos con destinos enmascarados; 200, 404 | RF-09 |
| POST | `/api/tickets/{id}/envios/{envioId}/reconciliar` | TicketDelivery / Reconciliar | ADMINISTRADOR, SUPERVISOR | `ReconciliarEnvioTicketRequest` | envío reconciliado; 200, 400, 404, 409 | RF-09 |
| GET | `/api/tickets/public/qr` | TicketDelivery / PublicQr | Pública; QR firmado limitado | query `token` | `image/png`, `Cache-Control: no-store`; 200, 404 | RF-07, RF-09 |
| GET | `/api/tickets` | Tickets / Listar | * | ninguno | listado sin token/firma QR; 200 | RF-06 |
| GET | `/api/tickets/{id}` | Tickets / Obtener | * | GUID | detalle sin token/firma QR; 200, 404 | RF-06 |
| POST | `/api/tickets/{id}/anular` | Tickets / Anular | ADMINISTRADOR, SUPERVISOR | `AnularTicketRequest` | estado anulado; 200, 400, 404, 409 | RF-10 |
| GET | `/api/tickets/{id}/qr` | Tickets / ObtenerQr | ADMINISTRADOR, SUPERVISOR | GUID | `image/png`; 200, 404 | RF-07 |
| POST | `/api/tickets` | Tickets / Crear | ADMINISTRADOR, SUPERVISOR | `CrearTicketRequest` | recibo sin secreto QR; 201, 404, 409, 500 | RF-06 |
| POST | `/api/tickets/validar` | Tickets / Validar | * | `ValidarTicketRequest` | resultado de validación; 200 | RF-07 |
| GET | `/api/gestion/usuarios` | Usuarios / Listar | ADMINISTRADOR | ninguno | usuarios sin hash; 200 | RF-01 |
| POST | `/api/gestion/usuarios` | Usuarios / Crear | ADMINISTRADOR | `CrearUsuarioRequest` | usuario sin hash; 201, 400, 409 | RF-01 |
| PUT | `/api/gestion/usuarios/{id}` | Usuarios / Editar | ADMINISTRADOR | `ActualizarUsuarioRequest` | usuario sin hash; 200, 400, 404, 409 | RF-01 |
| POST | `/api/gestion/usuarios/{id}/restablecer-contrasena` | Usuarios / Restablecer | ADMINISTRADOR | request contraseña | 204, 400, 404 | RF-01 |
| DELETE | `/api/gestion/usuarios/{id}` | Usuarios / Desactivar | ADMINISTRADOR | id | baja lógica; 204, 404, 409 | RF-01 |
| POST | `/api/gestion/usuarios/{id}/activar` | Usuarios / Activar | ADMINISTRADOR | id | 204, 404 | RF-01 |

## Matriz compacta por política

Cada una de las 65 rutas protegidas se prueba sin credencial y contra los seis roles (390 combinaciones de rol/ruta, más 65 comprobaciones anónimas). La prueba parametrizada comprueba que la lista de rutas real coincide exactamente con este inventario, compara roles esperados con `IAuthorizeData`/`IAllowAnonymous`, y llama a cada ruta protegida con token vigente para comprobar 401/403 y el paso por autorización. Los 19 endpoints `*` están marcados como herencia de política fallback autenticada; su autorización se establece en `Program.cs`.

| Ámbito | Rutas | Roles que pasan | Otros roles |
|---|---:|---|---|
| Público | 6 | Sin JWT; refresh/logout requieren refresh token en body | No aplica |
| Solo autenticación (`*`) | 19 | Los seis roles | No aplica |
| Auditoría | 1 | ADMINISTRADOR, AUDITOR | 403 |
| Creación de cierre | 1 | ADMINISTRADOR, SUPERVISOR, DESPACHADOR | 403 |
| Lectura/PDF de cierre | 4 | ADMINISTRADOR, SUPERVISOR, DESPACHADOR, AUDITOR | 403 |
| Catálogos de escritura | 9 | ADMINISTRADOR, SUPERVISOR | 403 |
| Despacho | 1 | ADMINISTRADOR, DESPACHADOR | 403 |
| Ajuste inventario | 1 | ADMINISTRADOR, SUPERVISOR | 403 |
| Notificaciones | 4 | ADMINISTRADOR, SUPERVISOR, DESPACHADOR, AUDITOR | 403 |
| Programaciones | 7 | ADMINISTRADOR, SUPERVISOR | 403 |
| Recepción y proveedores (escritura) | 2 | ADMINISTRADOR, SUPERVISOR | 403 |
| Solicitudes: creación | 1 | ADMINISTRADOR, SUPERVISOR, SOLICITANTE | 403 |
| Solicitudes: aprobación/rechazo | 2 | ADMINISTRADOR, SUPERVISOR | 403 |
| Entrega de tickets | 4 | ADMINISTRADOR, SUPERVISOR | 403 |
| Operaciones de ticket restringidas | 3 | ADMINISTRADOR, SUPERVISOR | 403 |
| Gestión de usuarios | 6 | ADMINISTRADOR | 403 |

Las operaciones históricas de auditoría y cierre solo exponen GET/POST de creación; no hay acciones PUT/PATCH/DELETE para esos recursos. Las credenciales inválidas no distinguen entre usuario inexistente y contraseña incorrecta; una cuenta inactiva conserva la respuesta 403 del flujo de activación. Bootstrap exige secreto de configuración, compara en tiempo constante, serializa su creación y rechaza nuevos administradores cuando ya existe uno. El QR público valida el token firmado, limita su tamaño y evita caché/referer.

## Superficie técnica

| Ruta | Política | Evidencia / estado |
|---|---|---|
| `/hubs/inventory` | `[Authorize]`; JWT en cabecera o query `access_token` solo para este prefijo | Negotiate anónimo -> 401; token inválido -> 401; token vigente -> conexión autorizada. Cubierto también por `ApiCoverageTests.Hub_de_inventario_exige_jwt_valido_en_negotiate` y conexión SignalR real. |
| `/swagger`, `/swagger/index.html`, `/swagger/v1/swagger.json` | Solo se mapean en Development | Generación se valida con OpenAPI en pruebas de matriz; Testing no expone Swagger HTTP. |
| `/hubs/inventory/negotiate` | Negotiate de SignalR; no acción MVC ni API REST | Se incluye en la verificación del Hub, no en las 71 rutas MVC. |

## Pruebas contractuales relacionadas

La matriz completa complementa, sin reemplazar, los tests de integración ya existentes para usuarios y baja lógica, catálogo y duplicados, solicitudes/aprobación/rechazo, despacho/recepción y movimientos, cierres/PDF/inmutabilidad, reportes CSV/XLSX/PDF, envíos/reintentos, notificaciones/IDOR, auditoría de solo lectura, QR, bootstrap y SignalR. Todos los contratos se prueban con PostgreSQL temporal sembrado desde `DATABASE_FINALLL`.
