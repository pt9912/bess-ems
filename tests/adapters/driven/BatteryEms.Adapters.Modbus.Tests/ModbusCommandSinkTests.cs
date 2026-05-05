using BatteryEms.Adapters.Modbus;
using BatteryEms.Application.Configuration;
using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Adapters.Modbus.Tests;

public sealed class ModbusCommandSinkTests
{
    private static BatteryCommand DischargeCommand() => new(
        CommandId: "c-1",
        Timestamp: ModbusFixtures.Now,
        AssetId: "asset-1",
        Mode: CommandMode.Discharge,
        ActivePowerKw: 25,
        ReactivePowerKvar: 0,
        ValidUntil: ModbusFixtures.Now + TimeSpan.FromSeconds(5),
        Reason: "schedule",
        Source: CommandSource.Optimization);

    [Fact]
    public async Task WriteAsync_writes_setpoint_and_mode_registers()
    {
        var client = new FakeModbusClient();
        var sink = new ModbusCommandSink(
            client,
            ModbusFixtures.VendorNeutralMapping(),
            ModbusFixtures.Defaults(),
            new ModbusFixtures.FixedClock());

        var result = await sink.WriteAsync(DischargeCommand(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, client.Writes.Count);
        Assert.Contains(client.Writes, w => w.Address == 200);
        Assert.Contains(client.Writes, w => w.Address == 202);
    }

    [Fact]
    public async Task WriteAsync_rejects_non_cyclic_setpoint_cadence()
    {
        var mapping = ModbusFixtures.VendorNeutralMapping() with
        {
            Registers = new List<ModbusRegisterMapping>
            {
                new("active_power_setpoint_kw", 200, "int16", 0.1, -100, 100, true, "once_per_day", "none", null, null, null),
            },
        };
        var sink = new ModbusCommandSink(
            new FakeModbusClient(),
            mapping,
            ModbusFixtures.Defaults(),
            new ModbusFixtures.FixedClock());

        var result = await sink.WriteAsync(DischargeCommand(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("once_per_day", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_rejects_setpoint_with_token_auth()
    {
        var mapping = ModbusFixtures.VendorNeutralMapping() with
        {
            Registers = new List<ModbusRegisterMapping>
            {
                new("active_power_setpoint_kw", 200, "int16", 0.1, -100, 100, true, "cyclic", "token", null, null, null),
            },
        };
        var sink = new ModbusCommandSink(
            new FakeModbusClient(),
            mapping,
            ModbusFixtures.Defaults(),
            new ModbusFixtures.FixedClock());

        var result = await sink.WriteAsync(DischargeCommand(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("token", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_returns_failure_when_setpoint_register_missing()
    {
        var mapping = ModbusFixtures.VendorNeutralMapping() with
        {
            Registers = new List<ModbusRegisterMapping>
            {
                new("soc_percent", 100, "uint16", 0.1, 0, 100, false, "cyclic", "none", null, null, null),
            },
        };
        var sink = new ModbusCommandSink(
            new FakeModbusClient(),
            mapping,
            ModbusFixtures.Defaults(),
            new ModbusFixtures.FixedClock());

        var result = await sink.WriteAsync(DischargeCommand(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("mapping-missing-setpoint", result.Reason);
    }

    [Fact]
    public async Task WriteAsync_reports_write_failure()
    {
        var client = new FakeModbusClient
        {
            OnWrite = () => throw new InvalidOperationException("modbus write failed"),
        };
        var sink = new ModbusCommandSink(
            client,
            ModbusFixtures.VendorNeutralMapping(),
            ModbusFixtures.Defaults(),
            new ModbusFixtures.FixedClock());

        var result = await sink.WriteAsync(DischargeCommand(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("modbus write failed", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_charge_command_writes_negative_setpoint()
    {
        var client = new FakeModbusClient();
        var sink = new ModbusCommandSink(
            client,
            ModbusFixtures.VendorNeutralMapping(),
            ModbusFixtures.Defaults(),
            new ModbusFixtures.FixedClock());

        var charge = DischargeCommand() with { Mode = CommandMode.Charge, ActivePowerKw = -25 };
        var result = await sink.WriteAsync(charge, CancellationToken.None);

        Assert.True(result.Success);
        var setpointWrite = client.Writes.First(w => w.Address == 200);
        Assert.Equal(unchecked((ushort)(short)-250), setpointWrite.Values[0]);
    }

    [Fact]
    public void Constructor_rejects_non_static_unit_id_discovery()
    {
        var mapping = new ModbusMappingConfiguration(
            ProfileName: "x",
            UnitIdDiscovery: "dynamic",
            StaticUnitId: null,
            Registers: new List<ModbusRegisterMapping>());

        Assert.Throws<NotSupportedException>(() =>
            new ModbusCommandSink(new FakeModbusClient(), mapping, ModbusFixtures.Defaults(), new ModbusFixtures.FixedClock()));
    }
}
