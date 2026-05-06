namespace BatteryEms.Adapters.Persistence;

// Idempotent DDL applied at startup. CREATE TABLE IF NOT EXISTS keeps
// the script safe to re-run on every boot, which is the migration
// strategy the architecture spec § 583 sanctions for M1
// (EF/FluentMigrator are explicitly listed as alternatives, not
// requirements). When the schema evolves we add an additional script
// and run it after this one; rolling forward is a deploy concern, not
// runtime.
//
// Reserved-word note: 'operator' is reserved in SQL, hence operator_id;
// 'timestamp' would type-collide so telemetry uses recorded_at,
// commands use issued_at, audit uses recorded_at — same pattern.
internal static class BessDbSchema
{
    public const string CreateScript = """
        CREATE TABLE IF NOT EXISTS telemetry (
            id BIGSERIAL PRIMARY KEY,
            asset_id TEXT NOT NULL,
            recorded_at TIMESTAMPTZ NOT NULL,
            soc_percent DOUBLE PRECISION NOT NULL,
            soh_percent DOUBLE PRECISION NOT NULL,
            active_power_kw DOUBLE PRECISION NOT NULL,
            reactive_power_kvar DOUBLE PRECISION NOT NULL,
            dc_voltage DOUBLE PRECISION NOT NULL,
            dc_current DOUBLE PRECISION NOT NULL,
            temperature_celsius DOUBLE PRECISION NOT NULL,
            available BOOLEAN NOT NULL,
            fault_status TEXT NOT NULL,
            data_quality_state TEXT NOT NULL,
            data_quality_reason TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_telemetry_asset_recorded_at
            ON telemetry(asset_id, recorded_at DESC);

        CREATE TABLE IF NOT EXISTS commands (
            command_id TEXT PRIMARY KEY,
            asset_id TEXT NOT NULL,
            issued_at TIMESTAMPTZ NOT NULL,
            mode TEXT NOT NULL,
            active_power_kw DOUBLE PRECISION NOT NULL,
            reactive_power_kvar DOUBLE PRECISION,
            valid_until TIMESTAMPTZ NOT NULL,
            reason TEXT NOT NULL,
            source TEXT NOT NULL,
            dispatch_success BOOLEAN NOT NULL,
            dispatch_reason TEXT NOT NULL,
            dispatched_at TIMESTAMPTZ NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_commands_asset_issued_at
            ON commands(asset_id, issued_at DESC);

        CREATE TABLE IF NOT EXISTS schedules (
            asset_id TEXT NOT NULL,
            type TEXT NOT NULL,
            market_bid_area TEXT NOT NULL,
            version INTEGER NOT NULL,
            PRIMARY KEY (asset_id, type)
        );

        CREATE TABLE IF NOT EXISTS schedule_windows (
            asset_id TEXT NOT NULL,
            type TEXT NOT NULL,
            window_start TIMESTAMPTZ NOT NULL,
            window_end TIMESTAMPTZ NOT NULL,
            target_power_kw DOUBLE PRECISION NOT NULL,
            PRIMARY KEY (asset_id, type, window_start),
            FOREIGN KEY (asset_id, type)
                REFERENCES schedules(asset_id, type) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS audit_events (
            id BIGSERIAL PRIMARY KEY,
            recorded_at TIMESTAMPTZ NOT NULL,
            operator_id TEXT NOT NULL,
            action TEXT NOT NULL,
            target_asset_id TEXT,
            reason TEXT NOT NULL,
            outcome TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_audit_recorded_at
            ON audit_events(recorded_at DESC);
        """;
}
