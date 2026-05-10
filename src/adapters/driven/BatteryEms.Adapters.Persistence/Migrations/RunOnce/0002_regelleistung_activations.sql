-- RM-M4-03-B: persistent dedupe tracker for Regelleistung activation
-- signals (LH-MKT-005/006). First real consumer of the migration
-- pipeline reserved as RM-M3-FUP-01.
--
-- Idempotent CREATE: a fresh database bootstrapped via the regenerated
-- 0001_initial.sql snapshot already contains regelleistung_activations
-- and the index, so re-applying 0002 must be a no-op there. Existing
-- v1.0.0 deployments (only the prior 0001_initial.sql with no
-- regelleistung_activations applied) materialise the table here.
-- The DDL must stay aligned with the canonical schema/schema.yaml
-- (see the regelleistung_activations table block) — schema-drift-check
-- enforces parity against 0001_initial.sql.

CREATE TABLE IF NOT EXISTS "regelleistung_activations" (
    "activation_id" TEXT NOT NULL,
    "payload_hash" TEXT NOT NULL,
    "sequence_number" BIGINT NOT NULL,
    "signal_timestamp_utc" TIMESTAMP WITH TIME ZONE NOT NULL,
    "source_id" TEXT NOT NULL,
    "winner_chosen_at" TIMESTAMP WITH TIME ZONE NOT NULL,
    PRIMARY KEY ("source_id", "activation_id")
);

CREATE INDEX IF NOT EXISTS "idx_regelleistung_activations_source_chosen_at"
    ON "regelleistung_activations" ("source_id", "winner_chosen_at");
