using BatteryEms.Domain;

namespace BatteryEms.Adapters.OpcUa;

// In-memory cache + assembler that consolidates per-Knoten Read- und
// Subscribe-Werte zu einem `BatteryTelemetry`-Sample (plan-RM-M4-04
// §4 Sub-Slice B). Mapping-`name` (z. B. "soc_percent",
// "active_power_kw") wird auf das passende `BatteryTelemetry`-Feld
// gemappt; ein nicht-erkannter `name` wird ignoriert (das ist
// erlaubt — der Mapping-Loader lässt arbiträre Knoten durch, und
// nicht alle bedienen Telemetry-Felder. Schreibseitige Knoten z. B.
// erreichen den Assembler erst gar nicht, weil der Source sie
// herausfiltert).
//
// DataQuality-Aggregation per LH-OPCUA-004 ist worst-of: ein
// einzelner ProtocolError-Knoten dominiert über Stale, und Stale
// dominiert über Valid. Ein Sample mit **allen** Knoten Good ist
// `DataQuality.Valid`; mindestens ein Bad → `ProtocolError(...)`;
// kein Bad aber mindestens ein Uncertain → `Stale(...)`. Der
// Reason-String des dominanten Knotens wird durchgereicht (das
// Sub-Slice ist ein Sample-Aggregat; der Operator bekommt das
// Worst-Case-Detail im Log/Health).
internal sealed class OpcUaTelemetryAssembler
{
    public const string SocPercent = "soc_percent";
    public const string SohPercent = "soh_percent";
    public const string ActivePowerKw = "active_power_kw";
    public const string ReactivePowerKvar = "reactive_power_kvar";
    public const string DcVoltage = "dc_voltage";
    public const string DcCurrent = "dc_current";
    public const string TemperatureCelsius = "temperature_celsius";
    public const string Available = "available";
    public const string FaultCode = "fault_code";

    private readonly object _gate = new();
    private readonly Dictionary<string, FieldEntry> _values = new(StringComparer.Ordinal);
    private readonly string _assetId;

    public OpcUaTelemetryAssembler(string assetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        _assetId = assetId;
    }

    public void Update(string mappingName, double scaledValue, DataQuality quality)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mappingName);
        ArgumentNullException.ThrowIfNull(quality);
        lock (_gate)
        {
            _values[mappingName] = new FieldEntry(scaledValue, quality);
        }
    }

    // Snapshot the latest known values into a BatteryTelemetry. The
    // additionalQualityFloor is used by the source to inject the
    // sticky-overflow-flag (Stale("opcua-subscription-overflow")) into
    // the worst-of aggregation without touching the per-field cache.
    public BatteryTelemetry Build(DateTimeOffset timestamp, DataQuality? additionalQualityFloor = null)
    {
        lock (_gate)
        {
            var aggregated = additionalQualityFloor ?? DataQuality.Valid;
            foreach (var entry in _values.Values)
            {
                aggregated = WorstOf(aggregated, entry.Quality);
            }
            var available = TryGet(Available, out var availableValue) && availableValue >= 1.0;
            var fault = TryGet(FaultCode, out var faultValue)
                ? FormatFault(faultValue)
                : "ok";
            return new BatteryTelemetry(
                Timestamp: timestamp,
                AssetId: _assetId,
                SocPercent: GetOrZero(SocPercent),
                SohPercent: GetOrZero(SohPercent),
                ActivePowerKw: GetOrZero(ActivePowerKw),
                ReactivePowerKvar: GetOrZero(ReactivePowerKvar),
                DcVoltage: GetOrZero(DcVoltage),
                DcCurrent: GetOrZero(DcCurrent),
                TemperatureCelsius: GetOrZero(TemperatureCelsius),
                Available: available,
                FaultStatus: fault,
                DataQuality: aggregated);
        }
    }

    public bool HasAnyEntry
    {
        get { lock (_gate) { return _values.Count > 0; } }
    }

    private double GetOrZero(string key) => TryGet(key, out var v) ? v : 0.0;

    private bool TryGet(string key, out double value)
    {
        if (_values.TryGetValue(key, out var entry))
        {
            value = entry.Value;
            return true;
        }
        value = 0;
        return false;
    }

    private static string FormatFault(double value)
    {
        // 0 ⇒ "ok", anything else ⇒ "fault-{code}"; the per-mapping
        // value_explanation lookup is a Sub-Slice-D / observability
        // refinement (cross-cutting with the Health endpoint).
        return value < 0.5 ? "ok" : $"fault-{(int)value}";
    }

    private static DataQuality WorstOf(DataQuality a, DataQuality b)
    {
        // Severity ranking: ProtocolError > Substituted > Stale > Valid.
        // The dominant DataQuality wins; ties keep `a`.
        return Severity(a) >= Severity(b) ? a : b;
    }

    private static int Severity(DataQuality q) => q.Flag switch
    {
        DataQualityState.ProtocolError => 3,
        DataQualityState.Substituted => 2,
        DataQualityState.Stale => 1,
        DataQualityState.Valid => 0,
        _ => 0,
    };

    private sealed record FieldEntry(double Value, DataQuality Quality);
}
