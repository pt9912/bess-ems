namespace BatteryEms.Host;

// Bindable from IConfiguration section "Bess". The host validates these
// fields at start-up (LH-CONF-003 + LH-OPS-001) and refuses to spin up
// the regulation pipeline if anything mandatory is missing.
public sealed class BessHostOptions
{
    public const string SectionName = "Bess";

    // Absolute or relative path to the directory holding the JSON
    // schemas that ConfigurationLoader uses (asset.schema.json,
    // schedule.schema.json, …). Required.
    public string SchemaDirectory { get; set; } = string.Empty;

    // Absolute or relative path to the asset configuration JSON. The
    // file must validate against asset.schema.json. Required.
    public string AssetConfigPath { get; set; } = string.Empty;

    // Optional path to a schedule JSON. When set, the schedule is
    // loaded and stored in the IScheduleRepository at start-up so the
    // first regulation cycle already sees commitments.
    public string? ScheduleConfigPath { get; set; }

    // Optional path to a retention policy JSON. When set, the policy
    // is registered for the RetentionRunUseCase (RM-M1-14 trigger
    // wiring follows in a later RM-M1-19 step).
    public string? RetentionConfigPath { get; set; }

    // Optional Postgres connection string. When set, the host wires the
    // Dapper-backed adapters; otherwise the in-memory stores from the
    // Application layer take over so headless smoke tests can run.
    public string? PersistenceConnectionString { get; set; }
}
