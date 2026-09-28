-- RF-23: persistent, per-user notifications and inventory low-stock episodes.
BEGIN;

ALTER TABLE notificaciones
    ADD COLUMN IF NOT EXISTS severidad VARCHAR(12) NOT NULL DEFAULT 'INFO'
        CHECK (severidad IN ('INFO','AVISO','CRITICA')),
    ADD COLUMN IF NOT EXISTS fecha_lectura TIMESTAMP,
    ADD COLUMN IF NOT EXISTS clave_deduplicacion VARCHAR(220),
    ADD COLUMN IF NOT EXISTS metadata JSONB NOT NULL DEFAULT '{}'::JSONB;

CREATE UNIQUE INDEX IF NOT EXISTS uq_notificaciones_usuario_deduplicacion
    ON notificaciones(id_usuario, clave_deduplicacion)
    WHERE id_usuario IS NOT NULL AND clave_deduplicacion IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_notificaciones_usuario_fecha
    ON notificaciones(id_usuario, fecha_creacion DESC, id_notificacion DESC);
CREATE INDEX IF NOT EXISTS idx_notificaciones_usuario_no_leidas
    ON notificaciones(id_usuario, fecha_creacion DESC) WHERE estado <> 'LEIDA';

-- State is stored beside tank stock so stock-change transactions can serialize
-- NORMAL -> CRÍTICO transitions and close an episode when stock recovers.
ALTER TABLE tanques
    ADD COLUMN IF NOT EXISTS notificacion_bajo_activa BOOLEAN NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS numero_episodio_bajo BIGINT NOT NULL DEFAULT 0;
UPDATE tanques SET notificacion_bajo_activa = (existencia_actual_galones <= nivel_critico_galones)
WHERE numero_episodio_bajo = 0;

COMMIT;
