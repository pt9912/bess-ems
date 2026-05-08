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

        // RM-M2-HIL-01: register_table is plumbed through the loader
        // but the read path still uses ReadHoldingRegistersAsync only;
        // HIL-02 lifts this guard once the FC04 (input register) read
        // path lands. Same for word_order — HIL-03 wires low_high.
        foreach (var register in mapping.Registers)
        {
            if (register.Writable)
            {
                continue;
            }
            if (register.RegisterTable != ModbusRegisterTables.Holding)
            {
                throw new NotSupportedException(
                    $"register '{register.Name}' specifies register_table='{register.RegisterTable}'; "
                    + "input-register reads land with RM-M2-HIL-02. Until then only 'holding' is supported.");
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

            var words = await _client
                .ReadHoldingRegistersAsync(unitId, register.Address, RegisterDecoder.WordCount(register.Type), cancellationToken)
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
