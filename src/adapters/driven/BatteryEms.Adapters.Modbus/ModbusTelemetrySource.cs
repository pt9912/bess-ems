using System.Runtime.CompilerServices;
using BatteryEms.Application.Configuration;
using BatteryEms.Application.IO;
using BatteryEms.Application.Time;
using BatteryEms.Domain;

namespace BatteryEms.Adapters.Modbus;

public sealed class ModbusTelemetrySource : IBatteryTelemetrySource
{
    private readonly IModbusClient _client;
    private readonly ModbusMappingConfiguration _mapping;
    private readonly ModbusAdapterOptions _options;
    private readonly IClock _clock;
    private AdapterStatus _status = AdapterStatus.Disconnected;

    public ModbusTelemetrySource(
        IModbusClient client,
        ModbusMappingConfiguration mapping,
        ModbusAdapterOptions options,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);

        if (mapping.UnitIdDiscovery != "static" || mapping.StaticUnitId is null)
        {
            throw new NotSupportedException(
                $"M1 simulator path supports unit_id_discovery=static with explicit static_unit_id; got '{mapping.UnitIdDiscovery}'.");
        }

        // RM-M2-HIL-02: read path now branches on register_table
        // (Holding → FC03, Input → FC04). The schema already restricts
        // the JSON value to {holding, input}, but programmatic
        // construction (test fixtures) can hand in anything; the
        // explicit reject keeps the read-path if/else total. word_order
        // =low_high stays gated until HIL-03 wires the swapped decoder.
        foreach (var register in mapping.Registers)
        {
            if (register.Writable)
            {
                continue;
            }
            if (register.RegisterTable != ModbusRegisterTables.Holding
                && register.RegisterTable != ModbusRegisterTables.Input)
            {
                throw new NotSupportedException(
                    $"register '{register.Name}' specifies register_table='{register.RegisterTable}'; "
                    + $"only '{ModbusRegisterTables.Holding}' and '{ModbusRegisterTables.Input}' are supported.");
            }
            if (register.WordOrder != ModbusWordOrders.HighLow)
            {
                throw new NotSupportedException(
                    $"register '{register.Name}' specifies word_order='{register.WordOrder}'; "
                    + "swapped word order lands with RM-M2-HIL-03. Until then only 'high_low' is supported.");
            }
        }

        _client = client;
        _mapping = mapping;
        _options = options;
        _clock = clock;
    }

    public AdapterStatus Status => _status;

    public async IAsyncEnumerable<BatteryTelemetry> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);

        while (!cancellationToken.IsCancellationRequested)
        {
            var attempt = await TryReadOnceAsync(cancellationToken).ConfigureAwait(false);
            if (attempt is not null)
            {
                yield return attempt;
            }

            try
            {
                await Task.Delay(_options.PollingInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Adapter boundary captures arbitrary protocol errors and surfaces them via AdapterStatus so the worker can degrade gracefully.")]
    private async Task<BatteryTelemetry?> TryReadOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.ReadTimeout);

            var values = await ReadAllAsync(cts.Token).ConfigureAwait(false);
            var now = _clock.UtcNow;
            _status = new AdapterStatus(
                Connected: true,
                LastSuccessfulReadAt: now,
                LastError: null,
                ConsecutiveFailures: 0);
            return BuildTelemetry(values, now);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _status = new AdapterStatus(
                Connected: _client.IsConnected,
                LastSuccessfulReadAt: _status.LastSuccessfulReadAt,
                LastError: ex.Message,
                ConsecutiveFailures: _status.ConsecutiveFailures + 1);
            return null;
        }
    }

    private async Task<Dictionary<string, double>> ReadAllAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        var unitId = _mapping.StaticUnitId!.Value;
        foreach (var register in _mapping.Registers)
        {
            if (register.Writable)
            {
                continue;
            }

            var wordCount = RegisterDecoder.WordCount(register.Type);
            // RM-M2-HIL-02: branch on register_table — FC03 for the
            // existing M1 holding-register profile, FC04 for the HIL
            // input-register measurements. The constructor narrowed
            // RegisterTable to {Holding, Input}, so this if/else is
            // total.
            var words = register.RegisterTable == ModbusRegisterTables.Input
                ? await _client
                    .ReadInputRegistersAsync(unitId, register.Address, wordCount, cancellationToken)
                    .ConfigureAwait(false)
                : await _client
                    .ReadHoldingRegistersAsync(unitId, register.Address, wordCount, cancellationToken)
                    .ConfigureAwait(false);
            result[register.Name] = RegisterDecoder.Decode(register, words);
        }

        return result;
    }

    private BatteryTelemetry BuildTelemetry(Dictionary<string, double> values, DateTimeOffset timestamp)
    {
        var available = values.TryGetValue("available", out var availableValue) && availableValue >= 1.0;
        return new BatteryTelemetry(
            Timestamp: timestamp,
            AssetId: _options.AssetId,
            SocPercent: values.GetValueOrDefault("soc_percent"),
            SohPercent: values.GetValueOrDefault("soh_percent"),
            ActivePowerKw: values.GetValueOrDefault("active_power_kw"),
            ReactivePowerKvar: values.GetValueOrDefault("reactive_power_kvar"),
            DcVoltage: values.GetValueOrDefault("dc_voltage"),
            DcCurrent: values.GetValueOrDefault("dc_current"),
            TemperatureCelsius: values.GetValueOrDefault("temperature_celsius"),
            Available: available,
            FaultStatus: "ok",
            DataQuality: DataQuality.Valid);
    }
}
