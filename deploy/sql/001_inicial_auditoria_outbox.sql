CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917013933_InicialAuditoriaOutbox') THEN
    CREATE TABLE auditoria (
        id bigint GENERATED ALWAYS AS IDENTITY,
        codigo_empresa character varying(4) NOT NULL,
        codigo_modulo character varying(2),
        codigo_transaccion character varying(10),
        usuario character varying(30) NOT NULL,
        entidad character varying(60) NOT NULL,
        clave_entidad character varying(60) NOT NULL,
        accion character varying(1) NOT NULL,
        valor_anterior text,
        valor_nuevo text,
        direccion_ip character varying(45),
        canal character varying(5) NOT NULL,
        fecha_registro timestamp with time zone NOT NULL,
        CONSTRAINT "PK_auditoria" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917013933_InicialAuditoriaOutbox') THEN
    CREATE TABLE outbox_messages (
        id uuid NOT NULL,
        tipo character varying(200) NOT NULL,
        payload jsonb NOT NULL,
        occurred_at timestamp with time zone NOT NULL,
        processed_at timestamp with time zone,
        attempts integer NOT NULL DEFAULT 0,
        error character varying(2000),
        next_attempt_at timestamp with time zone,
        CONSTRAINT "PK_outbox_messages" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917013933_InicialAuditoriaOutbox') THEN
    CREATE INDEX ix_auditoria_empresa_fecha ON auditoria (codigo_empresa, fecha_registro DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917013933_InicialAuditoriaOutbox') THEN
    CREATE INDEX ix_auditoria_empresa_usuario ON auditoria (codigo_empresa, usuario);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917013933_InicialAuditoriaOutbox') THEN
    CREATE INDEX ix_outbox_pendientes ON outbox_messages (next_attempt_at, occurred_at) WHERE processed_at IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917013933_InicialAuditoriaOutbox') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260917013933_InicialAuditoriaOutbox', '8.0.11');
    END IF;
END $EF$;
COMMIT;

