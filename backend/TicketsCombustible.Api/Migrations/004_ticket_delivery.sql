-- RF-09 ticket delivery tracking and lifecycle constraints.
BEGIN;

ALTER TABLE envios_ticket
    ADD COLUMN IF NOT EXISTS intento INTEGER,
    ADD COLUMN IF NOT EXISTS lote_id UUID,
    ADD COLUMN IF NOT EXISTS proveedor VARCHAR(80),
    ADD COLUMN IF NOT EXISTS resultado TEXT,
    ADD COLUMN IF NOT EXISTS idempotency_key UUID,
    ADD COLUMN IF NOT EXISTS solicitado_en TIMESTAMP;

WITH ranked AS (
    SELECT id_envio,
           row_number() OVER (PARTITION BY id_ticket, canal ORDER BY fecha_envio NULLS LAST, id_envio)::INTEGER AS numero,
           gen_random_uuid() AS lote,
           gen_random_uuid() AS idem
    FROM envios_ticket
)
UPDATE envios_ticket e SET
    intento = COALESCE(e.intento, ranked.numero),
    lote_id = COALESCE(e.lote_id, ranked.lote),
    idempotency_key = COALESCE(e.idempotency_key, ranked.idem),
    solicitado_en = COALESCE(e.solicitado_en, e.fecha_envio, timezone('UTC', transaction_timestamp()))
FROM ranked WHERE ranked.id_envio = e.id_envio;

ALTER TABLE envios_ticket
    ALTER COLUMN intento SET DEFAULT 1,
    ALTER COLUMN intento SET NOT NULL,
    ALTER COLUMN lote_id SET DEFAULT gen_random_uuid(),
    ALTER COLUMN lote_id SET NOT NULL,
    ALTER COLUMN idempotency_key SET DEFAULT gen_random_uuid(),
    ALTER COLUMN idempotency_key SET NOT NULL,
    ALTER COLUMN solicitado_en SET DEFAULT (timezone('UTC', transaction_timestamp())),
    ALTER COLUMN solicitado_en SET NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_envio_intento_positivo') THEN
        ALTER TABLE envios_ticket ADD CONSTRAINT ck_envio_intento_positivo CHECK (intento > 0);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_envio_ticket_canal_intento') THEN
        ALTER TABLE envios_ticket ADD CONSTRAINT uq_envio_ticket_canal_intento UNIQUE (id_ticket, canal, intento);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_envio_ticket_idempotencia_canal') THEN
        ALTER TABLE envios_ticket ADD CONSTRAINT uq_envio_ticket_idempotencia_canal UNIQUE (id_ticket, idempotency_key, canal);
    END IF;
END;
$$;

CREATE UNIQUE INDEX IF NOT EXISTS uq_envio_ticket_canal_pendiente
    ON envios_ticket(id_ticket, canal) WHERE estado_envio = 'PENDIENTE';
CREATE INDEX IF NOT EXISTS idx_envios_ticket_fecha ON envios_ticket(id_ticket, solicitado_en DESC);

CREATE OR REPLACE FUNCTION fn_validar_transicion_estado_ticket()
RETURNS TRIGGER AS $$
DECLARE
    v_ahora_utc TIMESTAMP := clock_timestamp() AT TIME ZONE 'UTC';
BEGIN
    IF NEW.estado = OLD.estado THEN RETURN NEW; END IF;

    IF NEW.estado = 'ANULADO'
       AND OLD.estado IN ('CREADO','ENVIADO','PENDIENTE','PROXIMO_A_VENCER','VENCIDO') THEN
        IF NEW.anulado_en IS NULL OR NEW.id_usuario_anulacion IS NULL
           OR NEW.motivo_anulacion IS NULL OR length(btrim(NEW.motivo_anulacion)) = 0
           OR length(NEW.motivo_anulacion) > 500 THEN
            RAISE EXCEPTION 'La anulación requiere usuario, fecha y motivo (1 a 500 caracteres)';
        END IF;
        RETURN NEW;
    END IF;

    IF NEW.estado = 'PENDIENTE'
       AND OLD.estado IN ('CREADO','ENVIADO','PENDIENTE','PROXIMO_A_VENCER')
       AND EXISTS (SELECT 1 FROM envios_ticket WHERE id_ticket = OLD.id_ticket AND estado_envio = 'PENDIENTE') THEN
        RETURN NEW;
    END IF;

    IF NEW.estado = 'ENVIADO'
       AND OLD.estado = 'PENDIENTE'
       AND EXISTS (SELECT 1 FROM envios_ticket WHERE id_ticket = OLD.id_ticket)
       AND NOT EXISTS (
           SELECT 1 FROM (
               SELECT DISTINCT ON (canal) estado_envio
               FROM envios_ticket WHERE id_ticket = OLD.id_ticket
               ORDER BY canal, intento DESC
           ) ultimos
           WHERE estado_envio <> 'ENVIADO'
       ) THEN
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

COMMIT;
