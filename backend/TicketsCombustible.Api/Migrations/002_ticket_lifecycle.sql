-- Agrega la trazabilidad del actor que anula y protege las transiciones de estado.
-- Compatible con DATABASE_FINALLL y seguro de volver a ejecutar.
BEGIN;

ALTER TABLE tickets ADD COLUMN IF NOT EXISTS id_usuario_anulacion BIGINT;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_tickets_usuario_anulacion'
          AND conrelid = 'tickets'::regclass
    ) THEN
        ALTER TABLE tickets
            ADD CONSTRAINT fk_tickets_usuario_anulacion
            FOREIGN KEY (id_usuario_anulacion) REFERENCES usuarios(id_usuario);
    END IF;
END;
$$;

CREATE OR REPLACE FUNCTION fn_validar_transicion_estado_ticket()
RETURNS TRIGGER AS $$
DECLARE
    v_ahora_utc TIMESTAMP := clock_timestamp() AT TIME ZONE 'UTC';
BEGIN
    IF NEW.estado = OLD.estado THEN
        RETURN NEW;
    END IF;

    IF NEW.estado = 'ANULADO'
       AND OLD.estado IN ('CREADO','ENVIADO','PENDIENTE','PROXIMO_A_VENCER','VENCIDO') THEN
        IF NEW.anulado_en IS NULL OR NEW.id_usuario_anulacion IS NULL
           OR NEW.motivo_anulacion IS NULL OR length(btrim(NEW.motivo_anulacion)) = 0
           OR length(NEW.motivo_anulacion) > 500 THEN
            RAISE EXCEPTION 'La anulación requiere usuario, fecha y motivo (1 a 500 caracteres)';
        END IF;
        RETURN NEW;
    END IF;

    IF NEW.estado = 'CONSUMIDO'
       AND OLD.estado IN ('CREADO','ENVIADO','PENDIENTE','PROXIMO_A_VENCER')
       AND OLD.fecha_vencimiento > v_ahora_utc
       AND NEW.consumido_en IS NOT NULL
       AND EXISTS (SELECT 1 FROM despachos WHERE id_ticket = OLD.id_ticket) THEN
        RETURN NEW;
    END IF;

    RAISE EXCEPTION 'Transición inválida de ticket: % -> %', OLD.estado, NEW.estado;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_validar_transicion_estado_ticket ON tickets;
CREATE TRIGGER trg_validar_transicion_estado_ticket
BEFORE UPDATE OF estado ON tickets
FOR EACH ROW EXECUTE FUNCTION fn_validar_transicion_estado_ticket();

CREATE OR REPLACE VIEW vw_tickets_alerta AS
SELECT
    id_ticket, numero_secuencial, id_empleado, id_vehiculo, fecha_vencimiento, estado,
    CASE
        WHEN fecha_vencimiento <= (NOW() AT TIME ZONE 'UTC') AND estado NOT IN ('CONSUMIDO','ANULADO') THEN 'VENCIDO'
        WHEN fecha_vencimiento <= (NOW() AT TIME ZONE 'UTC') + INTERVAL '2 days' AND estado NOT IN ('CONSUMIDO','ANULADO') THEN 'PROXIMO_A_VENCER'
        ELSE estado
    END AS estado_calculado
FROM tickets;

COMMIT;
