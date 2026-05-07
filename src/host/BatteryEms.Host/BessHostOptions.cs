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

    // Optional horizon-solver wiring. Empty / "noop" keeps the
    // Application-layer NoOpScheduleOptimizer; "or_tools" enables the
    // production LP backend from BatteryEms.Adapters.Optimization.
    public BessScheduleSolverOptions ScheduleSolver { get; set; } = new();

    // Optional Modbus driven-adapter wiring (RM-M1-19c). When all three
    // fields are set, the host loads the mapping JSON and registers
    // ModbusTelemetrySource + ModbusCommandSink; otherwise the NoOp
    // adapters from the Application layer stay in place.
    public string? ModbusMappingPath { get; set; }
    public string? ModbusHost { get; set; }
    public int ModbusPort { get; set; }

    // Optional MQTT driven-adapter wiring. Same semantics as the Modbus
    // block — all four fields must be set or the host falls back to the
    // NoOp adapters.
    public string? MqttMappingPath { get; set; }
    public string? MqttBrokerHost { get; set; }
    public int MqttBrokerPort { get; set; }
    public string? MqttClientId { get; set; }
}

public sealed class BessScheduleSolverOptions
{
    public string? Backend { get; set; }
    public double? TimeLimitSeconds { get; set; }
    public double? GapTolerance { get; set; }
    public double? InitialSocPercent { get; set; }
}
