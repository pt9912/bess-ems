-- RM-M6-04: optional TimescaleDB acceleration for high-volume telemetry.
--
-- PostgreSQL remains the default persistence backend. This migration is
-- deliberately safe on a plain Postgres image: when the TimescaleDB
-- extension is not available or the library is not preloaded, it records a
-- NOTICE and leaves the table as the regular row store created by
-- 0001_initial.sql.
--
-- When the extension is available and creatable/installed, telemetry is
-- converted to a hypertable on recorded_at. Timescale requires every
-- unique index on a hypertable to include the time dimension, so the
-- existing surrogate-key primary key is widened from (id) to
-- (id, recorded_at). Repository code does not address telemetry by id, so
-- this stays below the hexagonal persistence API.

DO $$
DECLARE
    timescale_available BOOLEAN;
    timescale_preloaded BOOLEAN;
    telemetry_is_hypertable BOOLEAN;
BEGIN
    SELECT EXISTS (
        SELECT 1
        FROM pg_available_extensions
        WHERE name = 'timescaledb'
    )
    INTO timescale_available;

    IF NOT timescale_available THEN
        RAISE NOTICE 'TimescaleDB extension is not available; telemetry remains a regular PostgreSQL table.';
        RETURN;
    END IF;

    SELECT EXISTS (
        SELECT 1
        FROM regexp_split_to_table(
            COALESCE(current_setting('shared_preload_libraries', TRUE), ''),
            ',') AS preload_library(name)
        WHERE lower(btrim(name)) = 'timescaledb'
    )
    INTO timescale_preloaded;

    IF NOT timescale_preloaded THEN
        RAISE NOTICE 'TimescaleDB extension is available but is not preloaded via shared_preload_libraries; telemetry remains a regular PostgreSQL table.';
        RETURN;
    END IF;

    BEGIN
        CREATE EXTENSION IF NOT EXISTS timescaledb;
    EXCEPTION
        WHEN insufficient_privilege THEN
            RAISE NOTICE 'TimescaleDB extension is available but cannot be created by this database user; telemetry remains a regular PostgreSQL table.';
            RETURN;
    END;

    EXECUTE
        'SELECT EXISTS (
            SELECT 1
            FROM timescaledb_information.hypertables
            WHERE hypertable_schema = ''public''
              AND hypertable_name = ''telemetry''
        )'
    INTO telemetry_is_hypertable;

    IF telemetry_is_hypertable THEN
        RETURN;
    END IF;

    IF EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'telemetry'::regclass
          AND conname = 'telemetry_pkey'
    ) THEN
        ALTER TABLE "telemetry" DROP CONSTRAINT "telemetry_pkey";
        ALTER TABLE "telemetry"
            ADD CONSTRAINT "telemetry_pkey"
            PRIMARY KEY ("id", "recorded_at");
    END IF;

    EXECUTE
        'SELECT create_hypertable(
            ''telemetry'',
            ''recorded_at'',
            if_not_exists => TRUE,
            migrate_data => TRUE
        )';
END
$$;
