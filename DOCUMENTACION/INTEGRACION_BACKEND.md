# Integración web/API de LaVomVa

La web usa `/api` y Vite lo redirige a `http://localhost:5007` en desarrollo. Los datos se cargan exclusivamente de PostgreSQL; no hay modo de demostración ni registros de prueba en la web.

| Módulo | Estado |
| --- | --- |
| Sesión | Inicio de sesión y solicitud pública de cuenta CONSULTA, pendiente de activación administrativa |
| Administración | Un administrador puede listar, crear, editar y desactivar usuarios, cambiar roles y restablecer contraseñas |
| Catálogos | Creación, edición y desactivación de empleados, vehículos y departamentos; alta de proveedores, estaciones y tanques |
| Solicitudes | Alta, aprobación, rechazo y emisión de ticket |
| Tickets | Consulta; el QR se carga con JWT para administrador y supervisor |
| Inventario | Consulta y recepción por supervisor o administrador |
| Reportes | Cálculo y exportación CSV en el navegador |
| Despacho | La web muestra la indicación del punto de suministro; la operación corresponde a la app móvil |

Para iniciar la instalación, ejecuta `BASEDATOS/schema/DATABASE_FINALLL`, configura los secretos privados y crea el primer administrador con `POST /api/login/inicializar-admin` y `Bootstrap__Secret` de al menos 32 caracteres. Solo se acepta cuando aún no hay ningún administrador.

## Límites actuales

- No se ha ejecutado aquí una prueba integrada con PostgreSQL. La API y la base deben levantarse para comprobar el flujo completo.
- El despacho exige que la misma sesión haya validado el QR durante los cinco minutos anteriores. La prueba de validación se guarda en memoria del servidor, por lo que una instalación con varias instancias requerirá un almacén compartido. Tampoco se han implementado correo, SMS, PDF, cierre diario, auditoría completa ni reportes del servidor.
- La app móvil no se modificó y conserva sus problemas de compatibilidad descritos en el README principal.
- Configura HTTPS, secretos privados y CORS restringido antes de publicar el sistema.
