-- RF-11: recurring/request schedules and immutable execution history, timestamps stored as UTC.
BEGIN;

CREATE TABLE IF NOT EXISTS programaciones_solicitud (
    id_programacion BIGSERIAL PRIMARY KEY,
    tipo_solicitud VARCHAR(20) NOT NULL CHECK (tipo_solicitud IN ('AUTOMATICA','RECURRENTE')),
    id_empleado BIGINT NOT NULL REFERENCES empleados(id_empleado),
    id_vehiculo BIGINT NOT NULL REFERENCES vehiculos(id_vehiculo),
    id_departamento BIGINT NOT NULL REFERENCES departamentos(id_departamento),
    id_tipo_combustible BIGINT NOT NULL REFERENCES tipos_combustible(id_tipo_combustible),
    cantidad_solicitada_galones NUMERIC(10,2) NOT NULL CHECK (cantidad_solicitada_galones > 0),
    fecha_inicial TIMESTAMP NOT NULL,
    fecha_final TIMESTAMP,
    frecuencia VARCHAR(20),
    proxima_ejecucion TIMESTAMP,
    ultima_ejecucion TIMESTAMP,
    activa BOOLEAN NOT NULL DEFAULT TRUE,
    id_usuario_creador BIGINT NOT NULL REFERENCES usuarios(id_usuario),
    creado_en TIMESTAMP NOT NULL DEFAULT (timezone('UTC', transaction_timestamp())),
    actualizado_en TIMESTAMP NOT NULL DEFAULT (timezone('UTC', transaction_timestamp())),
    CONSTRAINT ck_programacion_rango_fechas CHECK (fecha_final IS NULL OR fecha_final >= fecha_inicial),
    CONSTRAINT ck_programacion_frecuencia CHECK (
        (tipo_solicitud = 'AUTOMATICA' AND frecuencia IS NULL)
        OR (tipo_solicitud = 'RECURRENTE' AND frecuencia IN ('DIARIA','SEMANAL','MENSUAL'))
    ),
    CONSTRAINT ck_programacion_proxima_activa CHECK (activa = (proxima_ejecucion IS NOT NULL))
);

CREATE INDEX IF NOT EXISTS idx_programaciones_vencidas
    ON programaciones_solicitud(proxima_ejecucion, id_programacion) WHERE activa = TRUE;

CREATE TABLE IF NOT EXISTS ejecuciones_programadas (
    id_ejecucion BIGSERIAL PRIMARY KEY,
    id_programacion BIGINT NOT NULL REFERENCES programaciones_solicitud(id_programacion),
    fecha_programada TIMESTAMP NOT NULL,
    ejecutada_en TIMESTAMP NOT NULL,
    estado VARCHAR(12) NOT NULL CHECK (estado IN ('GENERADA','FALLIDA')),
    id_solicitud_generada BIGINT UNIQUE REFERENCES solicitudes_combustible(id_solicitud),
    detalle_error VARCHAR(500),
    CONSTRAINT uq_ejecucion_programacion_fecha UNIQUE (id_programacion, fecha_programada),
    CONSTRAINT ck_ejecucion_solicitud_estado CHECK (
        (estado = 'GENERADA' AND id_solicitud_generada IS NOT NULL AND detalle_error IS NULL)
        OR (estado = 'FALLIDA' AND id_solicitud_generada IS NULL AND detalle_error IS NOT NULL)
    )
);

CREATE INDEX IF NOT EXISTS idx_ejecuciones_programacion_fecha
    ON ejecuciones_programadas(id_programacion, ejecutada_en DESC);

COMMIT;
