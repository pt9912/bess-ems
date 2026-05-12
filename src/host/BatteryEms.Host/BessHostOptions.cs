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

    // RM-M4-04 (plan §4 Sub-Slice C) + RM-M4-05: optionales OPC-UA-
    // Adapter-Wiring. OpcUaMappingPath + OpcUaEndpointUrl müssen
    // gesetzt sein, damit `AddBessOpcUa` registriert. M4-05 ergänzt
    // RuntimeProfile + SecurityMode + SecurityPolicy als optionale
    // Override-Slots (Default `null` ⇒ Adapter-Defaults aus
    // `OpcUaAdapterOptions`: RuntimeProfile=Production,
    // SecurityMode=SignAndEncrypt, SecurityPolicy=Basic256Sha256).
    // Production verlangt `SecurityMode=Sign|SignAndEncrypt`; ein
    // unsecured Override (`SecurityMode=None` + `AllowUnsecured=true`
    // + Reason) ist nur in `RuntimeProfile=HilSimulator|Development`
    // gültig — siehe `OpcUaAdapterOptions.EnsureValid` (M4-05 D-02).
    public string? OpcUaMappingPath { get; set; }
    public Uri? OpcUaEndpointUrl { get; set; }
    public string? OpcUaSessionName { get; set; }
    public BatteryEms.Adapters.OpcUa.OpcUaRuntimeProfile? OpcUaRuntimeProfile { get; set; }
    public BatteryEms.Adapters.OpcUa.OpcUaSecurityMode? OpcUaSecurityMode { get; set; }
    public string? OpcUaSecurityPolicy { get; set; }
    public string? OpcUaApplicationCertificateSubject { get; set; }
    public string? OpcUaTrustedServerCertificatesPath { get; set; }
    public bool OpcUaAllowUnsecured { get; set; }
    public string? OpcUaAllowUnsecuredReason { get; set; }

    // RM-M5-01 (ADR 0005): optimization-core-Sidecar-Adapter-Wiring.
    // Wenn `ScheduleSolver.Backend = "optimization_core"`, baut der
    // Host die `OptimizationCoreOptions` aus den folgenden Slots
    // zusammen und registriert den gRPC-Sidecar-Adapter hinter dem
    // M2-`IScheduleOptimizer`-Port (RM-M5-01-A). Production verlangt
    // `unix://` oder `https://` als Endpoint (D-02 —
    // `optimization-core-not-hardened-in-production` bei plaintext-
    // `http://` und `RuntimeProfile=Production`).
    //
    // Wire-Integration ist seit RM-M5-01-B live (Health/Version-Probe
    // + Optimize-Streaming + Status-Mapping); RM-M5-01-C ergänzt
    // worker-owned Idempotency, Security-Pins und (Korrektur-Pass)
    // den lokalen `or_tools`-Fallback via
    // `OptimizationCoreFallbackBackend`.
    public Uri? OptimizationCoreSidecarEndpoint { get; set; }
    public BatteryEms.Adapters.OptimizationCore.OptimizationCoreRuntimeProfile?
        OptimizationCoreRuntimeProfile { get; set; }
    public string? OptimizationCoreExpectedContractVersion { get; set; }
    public string? OptimizationCoreClientCertificatePath { get; set; }
    public string? OptimizationCoreTrustedServerCertificatesPath { get; set; }
    public string? OptimizationCoreBearerTokenPath { get; set; }
    public TimeSpan? OptimizationCoreMaxFallbackScheduleAge { get; set; }

    // RM-M5-01-C Korrektur-Pass (plan-RM-M5 §Fallback-Matrix): wenn
    // Backend=optimization_core, kann hier ein **lokaler Fallback-
    // Optimizer** konfiguriert werden, der bei Sidecar-Failure
    // (Deadline/Unavailable/Stream-Crash) eine frische Optimierung
    // liefert. Default `null` ⇒ kein Fallback ⇒ Transport-Failure
    // führt direkt auf no_valid_plan + Safe-Stop. Heute unterstützter
    // Wert: `"or_tools"` (M2-OR-Tools-Adapter als
    // `IFallbackScheduleOptimizer`). Andere Backends sind explizit
    // Fehler.
    public string? OptimizationCoreFallbackBackend { get; set; }

    // RM-M5-02-B: optional MPC backend wiring. Default null keeps MPC
    // completely disabled: no IMpcDispatchOptimizer is registered and
    // the worker keeps the pre-MPC control path. Supported today:
    // "local_osqp". Reserved for F-M5-12 and rejected in this slice:
    // "optimization_core", "bi_modal".
    public string? MpcBackend { get; set; }
}

public sealed class BessScheduleSolverOptions
{
    public string? Backend { get; set; }
    public double? TimeLimitSeconds { get; set; }
    public double? GapTolerance { get; set; }
    public double? InitialSocPercent { get; set; }
}
