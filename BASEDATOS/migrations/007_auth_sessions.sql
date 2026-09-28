CREATE TABLE IF NOT EXISTS sesiones_usuario (
    id_sesion UUID PRIMARY KEY,
    id_familia UUID NOT NULL,
    id_usuario BIGINT NOT NULL REFERENCES usuarios(id_usuario) ON DELETE CASCADE,
    hash_refresh_token CHAR(64) NOT NULL UNIQUE,
    creado_en TIMESTAMPTZ NOT NULL,
    expira_en TIMESTAMPTZ NOT NULL,
    revocado_en TIMESTAMPTZ NULL,
    reemplazado_por_id UUID NULL REFERENCES sesiones_usuario(id_sesion),
    ultimo_uso_en TIMESTAMPTZ NULL,
    CONSTRAINT ck_sesiones_usuario_expiracion CHECK (expira_en > creado_en),
    CONSTRAINT ck_sesiones_usuario_no_autorreemplazo CHECK (reemplazado_por_id IS NULL OR reemplazado_por_id <> id_sesion)
);

CREATE INDEX IF NOT EXISTS ix_sesiones_usuario_usuario_activa
    ON sesiones_usuario(id_usuario) WHERE revocado_en IS NULL;
CREATE INDEX IF NOT EXISTS ix_sesiones_usuario_familia
    ON sesiones_usuario(id_familia, revocado_en);
