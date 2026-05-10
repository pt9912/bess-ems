using BatteryEms.Application.Configuration;
using BatteryEms.Application.IO;
using BatteryEms.Application.Time;
using BatteryEms.Domain;
using Microsoft.Extensions.Logging;

namespace BatteryEms.Adapters.OpcUa;

// RM-M4-04-C: OPC-UA-Command-Sink. Implementiert `IBatteryCommandSink`
// per plan-RM-M4-04 §4 Sub-Slice C. Schlägt den `active_power_setpoint_kw`-
// Knoten (und ggf. `reactive_power_setpoint_kvar`) im Mapping nach
// (Filter: `direction=write` + `writable=true`), wendet den
// (umgekehrten) ScaleFactor an und ruft `IOpcUaClient.WriteAsync`. Der
// Server-StatusCode entscheidet das Ergebnis:
//
//   - Good (Severity-Bits 0x00xxxxxx) ⇒ `CommandDispatchResult.Ok`.
//   - Bad/Uncertain  ⇒ `CommandDispatchResult.Failed("opcua-write-bad-{name}")`.
//   - Setpoint-Knoten fehlt im Mapping ⇒ `Failed("opcua-mapping-not-writable")`.
//   - ScaleFactor==0 (post-Loader-Programmatik-Pfad) ⇒
//     `Failed("opcua-mapping-scale-zero")` statt Divide-by-Zero.
//
// Sicherheits-Defense-in-Depth (D-04): der Konstruktor ruft
// `Options.EnsureValid(logger)` — der AllowUnsecured-Startup-Guard
// fires hier wenn der Operator nicht opted-in hat. Der Sink ist nicht
// produktiv freigegeben bevor RM-M4-05 die volle Security-Härtung
// dranhängt (analog zur M4-06-MqttNetClient-Linie).
//
// IAsyncDisposable per D-09: post-Dispose returnt jeder weitere
// WriteAsync-Aufruf `Failed("opcua-sink-disposed")` ohne Throw.
public sealed class OpcUaCommandSink : IBatteryCommandSink, IAsyncDisposable
{
    private readonly IOpcUaClient _client;
    private readonly OpcUaMappingConfiguration _mapping;
    private readonly BatteryAsset _asset;
    private readonly OpcUaAdapterOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<OpcUaCommandSink> _logger;
    private readonly object _stateGate = new();
    private bool _disposed;

    public OpcUaCommandSink(
        IOpcUaClient client,
        OpcUaMappingConfiguration mapping,
        BatteryAsset asset,
        OpcUaAdapterOptions options,
        IClock clock,
        ILogger<OpcUaCommandSink> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        // D-04 Security-Startup-Guard fires here.
        options.EnsureValid(logger);
        _client = client;
        _mapping = mapping;
        _asset = asset;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031",
        Justification = "Adapter boundary captures arbitrary protocol errors and reports them via CommandDispatchResult so the control loop can react.")]
    public async Task<CommandDispatchResult> WriteAsync(
        BatteryCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (_disposed)
        {
            return CommandDispatchResult.Failed("opcua-sink-disposed", _clock.UtcNow);
        }

        var setpoint = FindWritable("active_power_setpoint_kw");
        if (setpoint is null)
        {
            return CommandDispatchResult.Failed("opcua-mapping-not-writable", _clock.UtcNow);
        }
        if (!RegisterAcceptsCyclicWrite(setpoint))
        {
            return CommandDispatchResult.Failed(
                $"opcua-write-cadence-{setpoint.WriteCadence}-not-supported",
                _clock.UtcNow);
        }
        if (!double.IsFinite(setpoint.ScaleFactor) || setpoint.ScaleFactor == 0.0)
        {
            return CommandDispatchResult.Failed("opcua-mapping-scale-zero", _clock.UtcNow);
        }

        // Defence-in-depth: the use-case has already passed through
        // ConstraintLimiter / RampLimiter; AdapterWriteLimiter is a
        // final asset-static clamp (analog zu Modbus-Sink — siehe
        // ModbusCommandSink-Linie). The sink writes the post-clamp
        // value to the wire.
        var limit = AdapterWriteLimiter.Apply(command, _asset);
        var effective = limit.Command;

        try
        {
            await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.ReadTimeout);

            var pType = OpcUaDataTypeParser.Parse(setpoint.DataType);
            var pWire = effective.ActivePowerKw / setpoint.ScaleFactor;
            var pResult = await _client
                .WriteAsync(setpoint.NodeId, BoxForDataType(pWire, pType), pType, cts.Token)
                .ConfigureAwait(false);
            if (!IsGoodStatusCode(pResult.StatusCode))
            {
                return CommandDispatchResult.Failed(
                    BuildBadReason(pResult.StatusCode, "active_power_setpoint_kw"),
                    _clock.UtcNow);
            }

            var qDropped = false;
            var qSetpoint = FindWritable("reactive_power_setpoint_kvar");
            if (qSetpoint is not null)
            {
                if (!double.IsFinite(qSetpoint.ScaleFactor) || qSetpoint.ScaleFactor == 0.0)
                {
                    return CommandDispatchResult.Failed("opcua-mapping-scale-zero", _clock.UtcNow);
                }
                var qType = OpcUaDataTypeParser.Parse(qSetpoint.DataType);
                var qWire = (effective.ReactivePowerKvar ?? 0) / qSetpoint.ScaleFactor;
                var qResult = await _client
                    .WriteAsync(qSetpoint.NodeId, BoxForDataType(qWire, qType), qType, cts.Token)
                    .ConfigureAwait(false);
                if (!IsGoodStatusCode(qResult.StatusCode))
                {
                    return CommandDispatchResult.Failed(
                        BuildBadReason(qResult.StatusCode, "reactive_power_setpoint_kvar"),
                        _clock.UtcNow);
                }
            }
            else if ((effective.ReactivePowerKvar ?? 0) != 0)
            {
                qDropped = true;
            }

            var baseReason = limit.WasLimited ? $"adapter-limited:{limit.Reason}" : effective.Reason;
            var reason = qDropped ? $"{baseReason};q-dropped:no-mapping" : baseReason;
            return CommandDispatchResult.Ok(_clock.UtcNow, reason);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return CommandDispatchResult.Failed($"opcua-write-failed: {ex.Message}", _clock.UtcNow);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_stateGate)
        {
            if (_disposed) { return ValueTask.CompletedTask; }
            _disposed = true;
        }
        // The IOpcUaClient is a shared singleton between the source +
        // sink; container-shutdown owns its disposal. The sink only
        // marks itself disposed and refuses subsequent WriteAsync.
        return ValueTask.CompletedTask;
    }

    private OpcUaNodeMapping? FindWritable(string name)
    {
        foreach (var node in _mapping.Nodes)
        {
            if (string.Equals(node.Name, name, StringComparison.Ordinal)
                && node.Writable
                && node.Direction.Equals("write", StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }
        }
        return null;
    }

    private static bool RegisterAcceptsCyclicWrite(OpcUaNodeMapping node) =>
        node.WriteCadence is "cyclic";

    private static bool IsGoodStatusCode(uint statusCode) => (statusCode & 0xC0000000u) == 0u;

    private static string BuildBadReason(uint statusCode, string nodeName)
    {
        var quality = OpcUaStatusCodeMapper.Map(statusCode);
        // OpcUaStatusCodeMapper produces e.g. "opcua-bad-not-connected"
        // or "opcua-uncertain-last-usable-value"; both are valid
        // failure-reasons here. Plan §4 row C lists "opcua-write-bad-
        // {statusCodeName}" — we keep the mapper-emitted reason and
        // just suffix the node-name so the audit trail shows which
        // setpoint missed.
        return $"{quality.Reason}:{nodeName}";
    }

    // Variant-style boxing for the SDK-side WriteAsync. The SDK accepts
    // an object?; the production OpcUaClient (Sub-Slice D) is expected
    // to wrap into the matching OPC-UA Variant. The FakeOpcUaClient
    // round-trips the boxed value as-is, so the data-type-aware coercion
    // here is mainly a forward-compat hook for the production binding.
    private static object BoxForDataType(double value, OpcUaDataType dataType) => dataType switch
    {
        OpcUaDataType.Bool => value >= 0.5,
        OpcUaDataType.Int16 => (short)value,
        OpcUaDataType.Int32 => (int)value,
        OpcUaDataType.Int64 => (long)value,
        OpcUaDataType.UInt16 => (ushort)Math.Max(0, value),
        OpcUaDataType.UInt32 => (uint)Math.Max(0, value),
        OpcUaDataType.UInt64 => (ulong)Math.Max(0, value),
        OpcUaDataType.Float => (float)value,
        OpcUaDataType.Double => value,
        OpcUaDataType.String => value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => value,
    };
}
