-- RF-18: detalle físico, resumen verificable e inmutabilidad del cierre diario.
ALTER TABLE cierres_diarios
    ADD COLUMN IF NOT EXISTS inventario_fisico_galones NUMERIC(12,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS cantidad_despachos INTEGER NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS estado VARCHAR(20) NOT NULL DEFAULT 'CERRADO',
    ADD COLUMN IF NOT EXISTS detalle_tanques JSONB NOT NULL DEFAULT '[]'::JSONB;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_cierre_despachos_no_negativos') THEN
        ALTER TABLE cierres_diarios ADD CONSTRAINT ck_cierre_despachos_no_negativos CHECK (cantidad_despachos >= 0);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_cierre_diario_finalizado') THEN
        ALTER TABLE cierres_diarios ADD CONSTRAINT ck_cierre_diario_finalizado CHECK (estado = 'CERRADO');
    END IF;
END;
$$;

ALTER TABLE despachos ALTER COLUMN fecha_hora SET DEFAULT (timezone('UTC', transaction_timestamp()));
ALTER TABLE movimientos_inventario ALTER COLUMN fecha_hora SET DEFAULT (timezone('UTC', transaction_timestamp()));

CREATE OR REPLACE FUNCTION fn_bloquear_movimiento_dia_cerrado()
RETURNS TRIGGER AS $$
DECLARE
    v_estacion_id BIGINT;
BEGIN
    NEW.fecha_hora := timezone('UTC', transaction_timestamp());
    SELECT id_estacion INTO v_estacion_id FROM tanques WHERE id_tanque = NEW.id_tanque;
    IF v_estacion_id IS NULL THEN
        RAISE EXCEPTION 'Tanque inexistente para registrar movimiento';
    END IF;

    -- Compartido con el POST de cierre: si el movimiento gana el lock queda
    -- incluido en el snapshot; si el cierre gana, el movimiento recibe 409.
    PERFORM 1 FROM estaciones WHERE id_estacion = v_estacion_id FOR UPDATE;
    IF EXISTS (SELECT 1 FROM cierres_diarios WHERE id_estacion = v_estacion_id AND fecha = NEW.fecha_hora::DATE) THEN
        RAISE EXCEPTION 'El día operacional ya está cerrado para la estación %', v_estacion_id;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_00_bloquear_movimiento_dia_cerrado ON movimientos_inventario;
CREATE TRIGGER trg_00_bloquear_movimiento_dia_cerrado
BEFORE INSERT ON movimientos_inventario
FOR EACH ROW EXECUTE FUNCTION fn_bloquear_movimiento_dia_cerrado();

CREATE OR REPLACE FUNCTION fn_cierre_diario_inmutable()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'Los cierres diarios finalizados son inmutables';
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_cierre_diario_inmutable ON cierres_diarios;
CREATE TRIGGER trg_cierre_diario_inmutable
BEFORE UPDATE OR DELETE ON cierres_diarios
FOR EACH ROW EXECUTE FUNCTION fn_cierre_diario_inmutable();

CREATE INDEX IF NOT EXISTS idx_cierres_fecha_estacion ON cierres_diarios(fecha, id_estacion);
CREATE INDEX IF NOT EXISTS idx_movimientos_fecha_tanque ON movimientos_inventario(fecha_hora, id_tanque);
