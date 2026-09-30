-- Una factura de proveedor solo puede registrarse una vez y no puede estar vacía.
BEGIN;

UPDATE recepciones_combustible SET numero_factura = btrim(numero_factura)
WHERE numero_factura <> btrim(numero_factura);

DO $$
DECLARE duplicadas TEXT;
BEGIN
    SELECT string_agg(format('proveedor %s / factura %s', id_proveedor, numero_factura), '; ')
    INTO duplicadas
    FROM (SELECT id_proveedor, numero_factura FROM recepciones_combustible
          GROUP BY id_proveedor, numero_factura HAVING count(*) > 1) d;
    IF duplicadas IS NOT NULL THEN
        RAISE EXCEPTION 'Existen recepciones con factura duplicada; corríjalas antes de migrar: %', duplicadas;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_recepcion_factura_no_vacia') THEN
        ALTER TABLE recepciones_combustible
            ADD CONSTRAINT chk_recepcion_factura_no_vacia CHECK (btrim(numero_factura) <> '');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_recepcion_proveedor_factura') THEN
        ALTER TABLE recepciones_combustible
            ADD CONSTRAINT uq_recepcion_proveedor_factura UNIQUE (id_proveedor, numero_factura);
    END IF;
END $$;

COMMIT;
