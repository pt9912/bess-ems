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
            ModbusFixtures.SampleAsset(),
            ModbusFixtures.Defaults(),
            new ModbusFixtures.FixedClock());

        var result = await sink.WriteAsync(DischargeCommand(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, client.Writes.Count);
        Assert.Contains(client.Writes, w => w.Address == 200);
        Assert.Contains(client.Writes, w => w.Address == 202);
    }

    [Fact]
    public async Task WriteAsync_writes_reactive_power_setpoint_when_mapped()
    {
        // RM-M2-HIL-05: when the mapping declares a writable
        // reactive_power_setpoint_kvar register, the sink writes Q
        // alongside P. Existing M1 mappings that omit it stay
        // unchanged (the missing-register positive cover is in
        // WriteAsync_writes_setpoint_and_mode_registers above).
        var mapping = ModbusFixtures.VendorNeutralMapping() with
        {
            Registers = new List<ModbusRegisterMapping>
            {
                new("active_power_setpoint_kw", 200, "int16", 0.1, -100, 100, true, "cyclic", "none", null, null, null),
                new("reactive_power_setpoint_kvar", 204, "int16", 0.1, -100, 100, true, "cyclic", "none", null, null, null),
            },
        };
        var client = new FakeModbusClient();
        var sink = new ModbusCommandSink(
            client, mapping, ModbusFixtures.SampleAsset(),
            ModbusFixtures.Defaults(), new ModbusFixtures.FixedClock());

        var command = new BatteryCommand(
            CommandId: "c-q",
            Timestamp: ModbusFixtures.Now,
            AssetId: "asset-1",
            Mode: CommandMode.Discharge,
            ActivePowerKw: 25,
            ReactivePowerKvar: -5.5,
            ValidUntil: ModbusFixtures.Now + TimeSpan.FromSeconds(5),
            Reason: "schedule",
            Source: CommandSource.Optimization);

        var result = await sink.WriteAsync(command, CancellationToken.None);

        Assert.True(result.Success);
        var qWrite = Assert.Single(client.Writes, w => w.Address == 204);
        // -5.5 kvar at scale_factor 0.1 → wire value -55 → int16 sign-
        // extended to ushort 0xFFC9.
        Assert.Equal(unchecked((ushort)(short)-55), qWrite.Values[0]);
    }

    [Fact]
    public async Task WriteAsync_surfaces_q_dropped_in_reason_when_mapping_has_no_q_register()
    {
        // Carve-out Mn1: a non-zero Q in the command must not vanish
        // silently when the mapping has no reactive_power_setpoint_
        // kvar register. The dispatch result still reports success
        // (the P-write went through) but the Reason flags the loss
        // so the audit trail captures it.
        var mapping = ModbusFixtures.VendorNeutralMapping() with
        {
            Registers = new List<ModbusRegisterMapping>
            {
                new("active_power_setpoint_kw", 200, "int16", 0.1, -100, 100, true, "cyclic", "none", null, null, null),
                // No reactive_power_setpoint_kvar mapping.
            },
        };
        var sink = new ModbusCommandSink(
            new FakeModbusClient(), mapping, ModbusFixtures.SampleAsset(),
            ModbusFixtures.Defaults(), new ModbusFixtures.FixedClock());

        var command = new BatteryCommand(
            CommandId: "c-q-lost",
            Timestamp: ModbusFixtures.Now,
            AssetId: "asset-1",
            Mode: CommandMode.Discharge,
            ActivePowerKw: 25,
            ReactivePowerKvar: -5.5,
            ValidUntil: ModbusFixtures.Now + TimeSpan.FromSeconds(5),
            Reason: "schedule",
            Source: CommandSource.Optimization);

        var result = await sink.WriteAsync(command, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("q-dropped:no-mapping", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_does_not_flag_q_dropped_when_command_q_is_zero()
    {
        // Inverse of the test above: zero Q on the command means
        // no operator intent was lost, so q-dropped should not
        // appear in the reason.
        var mapping = ModbusFixtures.VendorNeutralMapping() with
        {
            Registers = new List<ModbusRegisterMapping>
            {
                new("active_power_setpoint_kw", 200, "int16", 0.1, -100, 100, true, "cyclic", "none", null, null, null),
            },
        };
        var sink = new ModbusCommandSink(
            new FakeModbusClient(), mapping, ModbusFixtures.SampleAsset(),
            ModbusFixtures.Defaults(), new ModbusFixtures.FixedClock());

        var result = await sink.WriteAsync(DischargeCommand(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.DoesNotContain("q-dropped", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_treats_null_reactive_power_as_zero()
    {
        // The control loop sometimes hands in a P-only command with Q
        // null. The HIL device must still receive 0 kvar so it does
        // not retain a stale non-zero Q from a previous command.
        var mapping = ModbusFixtures.VendorNeutralMapping() with
        {
            Registers = new List<ModbusRegisterMapping>
            {
                new("active_power_setpoint_kw", 200, "int16", 0.1, -100, 100, true, "cyclic", "none", null, null, null),
                new("reactive_power_setpoint_kvar", 204, "int16", 0.1, -100, 100, true, "cyclic", "none", null, null, null),
            },
        };
        var client = new FakeModbusClient();
        var sink = new ModbusCommandSink(
            client, mapping, ModbusFixtures.SampleAsset(),
            ModbusFixtures.Defaults(), new ModbusFixtures.FixedClock());

        var command = new BatteryCommand(
            CommandId: "c-no-q",
            Timestamp: ModbusFixtures.Now,
            AssetId: "asset-1",
            Mode: CommandMode.Discharge,
            ActivePowerKw: 10,
            ReactivePowerKvar: null,
            ValidUntil: ModbusFixtures.Now + TimeSpan.FromSeconds(5),
            Reason: "schedule",
            Source: CommandSource.Optimization);

        var result = await sink.WriteAsync(command, CancellationToken.None);

        Assert.True(result.Success);
        var qWrite = Assert.Single(client.Writes, w => w.Address == 204);
        Assert.Equal((ushort)0, qWrite.Values[0]);
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
            ModbusFixtures.SampleAsset(),
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
            ModbusFixtures.SampleAsset(),
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
            ModbusFixtures.SampleAsset(),
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
            ModbusFixtures.SampleAsset(),
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
            ModbusFixtures.SampleAsset(),
            ModbusFixtures.Defaults(),
            new ModbusFixtures.FixedClock());

        var charge = DischargeCommand() with { Mode = CommandMode.Charge, ActivePowerKw = -25 };
        var result = await sink.WriteAsync(charge, CancellationToken.None);

        Assert.True(result.Success);
        var setpointWrite = client.Writes.First(w => w.Address == 200);
        Assert.Equal(unchecked((ushort)(short)-250), setpointWrite.Values[0]);
    }

    [Fact]
    public async Task WriteAsync_clamps_overrange_setpoint_against_asset_limits_before_writing()
    {
        var client = new FakeModbusClient();
        var sink = new ModbusCommandSink(
            client,
            ModbusFixtures.VendorNeutralMapping(),
            ModbusFixtures.SampleAsset(maxDischarge: 50),
            ModbusFixtures.Defaults(),
            new ModbusFixtures.FixedClock());

        // 200 kW request against a 50 kW MaxDischarge asset must hit the wire
        // clamped to 50 kW (encoded as 500 with the 0.1 scale factor) and the
        // result must surface the clamp via result.Reason so audit can see it.
        var overrange = DischargeCommand() with { ActivePowerKw = 200 };
        var result = await sink.WriteAsync(overrange, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("adapter-limited:max-discharge-power", result.Reason);
        var setpointWrite = client.Writes.First(w => w.Address == 200);
        Assert.Equal(500, setpointWrite.Values[0]);
    }

    [Fact]
    public async Task WriteAsync_writes_zero_setpoint_when_mode_stop_carries_non_zero_power()
    {
        var client = new FakeModbusClient();
        var sink = new ModbusCommandSink(
            client,
            ModbusFixtures.VendorNeutralMapping(),
            ModbusFixtures.SampleAsset(),
            ModbusFixtures.Defaults(),
            new ModbusFixtures.FixedClock());

        var contradictory = DischargeCommand() with { Mode = CommandMode.Stop, ActivePowerKw = 25 };
        var result = await sink.WriteAsync(contradictory, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("adapter-limited:mode-stop-zero-power", result.Reason);
        var setpointWrite = client.Writes.First(w => w.Address == 200);
        Assert.Equal(0, setpointWrite.Values[0]);
    }

    [Fact]
    public void Constructor_rejects_non_static_unit_id_discovery()
    {
        var mapping = new ModbusMappingConfiguration(
            SchemaVersion: "v1",
            ProfileName: "x",
            UnitIdDiscovery: "dynamic",
            StaticUnitId: null,
            Registers: new List<ModbusRegisterMapping>());

        Assert.Throws<NotSupportedException>(() =>
            new ModbusCommandSink(new FakeModbusClient(), mapping, ModbusFixtures.SampleAsset(), ModbusFixtures.Defaults(), new ModbusFixtures.FixedClock()));
    }
}
