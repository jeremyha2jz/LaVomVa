-- Aplicar una vez sobre una base existente después de respaldarla.
BEGIN;

ALTER TABLE auditoria
    ADD COLUMN IF NOT EXISTS resultado VARCHAR(20) NOT NULL DEFAULT 'EXITO';

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_auditoria_resultado'
          AND conrelid = 'auditoria'::regclass
    ) THEN
        ALTER TABLE auditoria
            ADD CONSTRAINT ck_auditoria_resultado CHECK (resultado IN ('EXITO','FALLO'));
    END IF;
END;
$$;

CREATE OR REPLACE FUNCTION fn_bloquear_modificacion_auditoria()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'Los registros de auditoría son inmutables';
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_auditoria_inmutable ON auditoria;
CREATE TRIGGER trg_auditoria_inmutable
BEFORE UPDATE OR DELETE ON auditoria
FOR EACH ROW EXECUTE FUNCTION fn_bloquear_modificacion_auditoria();

COMMIT;
