CREATE TABLE IF NOT EXISTS mpc_runs (
    mpc_request_id TEXT PRIMARY KEY,
    asset_id TEXT NOT NULL,
    control_cycle_tick_utc TIMESTAMPTZ NOT NULL,
    sample_time_ms BIGINT NOT NULL CHECK (sample_time_ms > 0),
    mpc_model_version TEXT NOT NULL,
    state_estimator_variant TEXT NOT NULL,
    solver_config_hash TEXT NOT NULL,
    estimator_config_hash TEXT NOT NULL,
    random_seed BIGINT NOT NULL,
    numerik_stamp_json JSONB NOT NULL,
    p0_frobenius_display DOUBLE PRECISION NOT NULL CHECK (p0_frobenius_display >= 0),
    deterministic_mode TEXT NOT NULL,
    is_usable BOOLEAN NOT NULL,
    terminal_reason TEXT NOT NULL,
    trajectory_json JSONB NULL,
    terminal_state_json JSONB NULL,
    created_at_utc TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_mpc_runs_asset_tick
    ON mpc_runs (asset_id, control_cycle_tick_utc DESC);

CREATE INDEX IF NOT EXISTS ix_mpc_runs_created_at
    ON mpc_runs (created_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_mpc_runs_identity
    ON mpc_runs (
        asset_id,
        control_cycle_tick_utc,
        sample_time_ms,
        mpc_model_version,
        state_estimator_variant,
        solver_config_hash,
        estimator_config_hash,
        random_seed);
