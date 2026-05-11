-- RM-M5-01-C step 4: worker-owned Idempotency-Tracker für Sidecar-
-- Aufrufe (plan-RM-M5 §Request-Idempotenz Und Retry). Pro request_id
-- höchstens ein atomarer Terminalzustand; CAS-Update beim Sidecar-
-- Result-/Fallback-Pfad.
--
-- Idempotente CREATE-Linie analog zu 0002: ein frisches v1.0.0-
-- Deployment hat nach dem regenerierten 0001_initial.sql bereits die
-- optimization_idempotency-Tabelle, also bleibt das Re-Apply von
-- 0003 dort ein No-op. Bestehende v1.0.0-Deployments (nur 0001 vor
-- der Schema-YAML-Erweiterung angewendet) materialisieren die
-- Tabelle hier.
--
-- DDL muss synchron mit dem schema/schema.yaml-Block für
-- optimization_idempotency bleiben — schema-drift-check setzt das
-- gegen das regenerierte 0001_initial.sql durch.

CREATE TABLE IF NOT EXISTS "optimization_idempotency" (
    "request_id" TEXT NOT NULL,
    "terminal_state" TEXT NOT NULL,
    "terminal_reason" TEXT NOT NULL,
    "run_id" UUID,
    "produced_version" INTEGER,
    "created_at" TIMESTAMP WITH TIME ZONE NOT NULL,
    "committed_at" TIMESTAMP WITH TIME ZONE,
    PRIMARY KEY ("request_id")
);

CREATE INDEX IF NOT EXISTS "idx_optimization_idempotency_terminal_state"
    ON "optimization_idempotency" ("terminal_state");

CREATE INDEX IF NOT EXISTS "idx_optimization_idempotency_created_at"
    ON "optimization_idempotency" ("created_at");
