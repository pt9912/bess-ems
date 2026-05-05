using BatteryEms.Application.Configuration;
using BatteryEms.Application.IO;
using BatteryEms.Application.Time;
using BatteryEms.Domain;

namespace BatteryEms.Adapters.Modbus;

public sealed class ModbusCommandSink : IBatteryCommandSink
{
    private readonly IModbusClient _client;
    private readonly ModbusMappingConfiguration _mapping;
    private readonly ModbusAdapterOptions _options;
    private readonly IClock _clock;

    public ModbusCommandSink(
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

        _client = client;
        _mapping = mapping;
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

        try
        {
            await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.ReadTimeout);

            var unitId = _mapping.StaticUnitId!.Value;
            var setpointWords = RegisterDecoder.Encode(setpoint, command.ActivePowerKw);
            await _client
                .WriteHoldingRegistersAsync(unitId, setpoint.Address, setpointWords, cts.Token)
                .ConfigureAwait(false);

            var mode = FindRegister("operating_mode");
            if (mode is not null)
            {
                var modeWords = RegisterDecoder.Encode(mode, MapModeToValue(command.Mode));
                await _client
                    .WriteHoldingRegistersAsync(unitId, mode.Address, modeWords, cts.Token)
                    .ConfigureAwait(false);
            }

            return CommandDispatchResult.Ok(_clock.UtcNow, command.Reason);
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
