using BatteryEms.Application.Configuration;
using BatteryEms.Application.IO;
using BatteryEms.Application.Time;
using BatteryEms.Domain;

namespace BatteryEms.Adapters.Modbus;

public sealed class ModbusCommandSink : IBatteryCommandSink
{
    private readonly IModbusClient _client;
    private readonly ModbusMappingConfiguration _mapping;
    private readonly BatteryAsset _asset;
    private readonly ModbusAdapterOptions _options;
    private readonly IClock _clock;

    public ModbusCommandSink(
        IModbusClient client,
        ModbusMappingConfiguration mapping,
        BatteryAsset asset,
        ModbusAdapterOptions options,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);

        if (mapping.UnitIdDiscovery != "static" || mapping.StaticUnitId is null)
        {
            throw new NotSupportedException(
                $"M1 simulator path supports unit_id_discovery=static with explicit static_unit_id; got '{mapping.UnitIdDiscovery}'.");
        }

        // RM-M2-HIL-02/03: writes target holding registers (Modbus
        // has no write-to-input operation). Word order is now handled
        // by RegisterDecoder.Encode, so any value the schema accepts
        // is fine — the only check left is the register_table guard
        // against an HIL profile that mistakenly marks a setpoint
        // input.
        foreach (var register in mapping.Registers)
        {
            if (!register.Writable)
            {
                continue;
            }
            if (register.RegisterTable != ModbusRegisterTables.Holding)
            {
                throw new NotSupportedException(
                    $"register '{register.Name}' specifies register_table='{register.RegisterTable}'; "
                    + "Modbus writes target holding registers — input-register writes are not a Modbus operation.");
            }
        }

        _client = client;
        _mapping = mapping;
        _asset = asset;
        _options = options;
        _clock = clock;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Adapter boundary captures arbitrary protocol errors and reports them via CommandDispatchResult so the control loop can react.")]
    public async Task<CommandDispatchResult> WriteAsync(BatteryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var setpoint = FindRegister("active_power_setpoint_kw");
        if (setpoint is null)
        {
            return CommandDispatchResult.Failed("mapping-missing-setpoint", _clock.UtcNow);
        }

        if (!RegisterAcceptsCyclicWrite(setpoint))
        {
            return CommandDispatchResult.Failed(
                $"setpoint-write-cadence-{setpoint.WriteCadence}-not-supported",
                _clock.UtcNow);
        }

        if (setpoint.AuthRequired != "none")
        {
            return CommandDispatchResult.Failed(
                $"setpoint-auth-{setpoint.AuthRequired}-not-supported",
                _clock.UtcNow);
        }

        // Final asset-static clamp (RM-M1-11, LH-SAFE-007). Whatever the
        // application produced, the value that crosses the wire respects
        // the asset's MaxCharge/MaxDischarge ratings and the Mode==Stop/Idle
        // contract that "no power" means literally 0 kW.
        var limit = AdapterWriteLimiter.Apply(command, _asset);
        var effective = limit.Command;

        try
        {
            await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.ReadTimeout);

            var unitId = _mapping.StaticUnitId!.Value;
            var setpointWords = RegisterDecoder.Encode(setpoint, effective.ActivePowerKw);
            await _client
                .WriteHoldingRegistersAsync(unitId, setpoint.Address, setpointWords, cts.Token)
                .ConfigureAwait(false);

            // RM-M2-HIL-05: optional Q setpoint. Only writes when the
            // mapping declares a writable reactive_power_setpoint_kvar
            // register. Null Q on the command is treated as 0 kvar so
            // the device never sees a stale Q from a previous command.
            var qSetpoint = FindRegister("reactive_power_setpoint_kvar");
            if (qSetpoint is not null)
            {
                var qWords = RegisterDecoder.Encode(qSetpoint, effective.ReactivePowerKvar ?? 0);
                await _client
                    .WriteHoldingRegistersAsync(unitId, qSetpoint.Address, qWords, cts.Token)
                    .ConfigureAwait(false);
            }

            var mode = FindRegister("operating_mode");
            if (mode is not null)
            {
                var modeWords = RegisterDecoder.Encode(mode, MapModeToValue(effective.Mode));
                await _client
                    .WriteHoldingRegistersAsync(unitId, mode.Address, modeWords, cts.Token)
                    .ConfigureAwait(false);
            }

            var reason = limit.WasLimited ? $"adapter-limited:{limit.Reason}" : effective.Reason;
            return CommandDispatchResult.Ok(_clock.UtcNow, reason);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return CommandDispatchResult.Failed($"write-failed: {ex.Message}", _clock.UtcNow);
        }
    }

    private ModbusRegisterMapping? FindRegister(string name)
    {
        foreach (var register in _mapping.Registers)
        {
            if (register.Name == name && register.Writable)
            {
                return register;
            }
        }
        return null;
    }

    private static bool RegisterAcceptsCyclicWrite(ModbusRegisterMapping register) =>
        register.WriteCadence is "cyclic";

    private static double MapModeToValue(CommandMode mode) => mode switch
    {
        CommandMode.Stop => 0,
        CommandMode.Idle => 1,
        CommandMode.Charge => 2,
        CommandMode.Discharge => 3,
        _ => 0,
    };
}
