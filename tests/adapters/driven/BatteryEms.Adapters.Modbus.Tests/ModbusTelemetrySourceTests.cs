using BatteryEms.Adapters.Modbus;
using BatteryEms.Application.Configuration;
using BatteryEms.Application.IO;
using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Adapters.Modbus.Tests;

public sealed class ModbusTelemetrySourceTests
{
    [Fact]
    public async Task ReadAsync_yields_telemetry_built_from_register_values()
    {
        var client = new FakeModbusClient
        {
            ReadResponses =
            {
                [100] = new ushort[] { 605 }, // soc 60.5
                [110] = new ushort[] { unchecked((ushort)(short)-250) }, // -25 kW
                [120] = new ushort[] { 1 }, // available
            },
        };
        var source = new ModbusTelemetrySource(
            client,
            ModbusFixtures.VendorNeutralMapping(),
            ModbusFixtures.Defaults(),
            new ModbusFixtures.FixedClock());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        BatteryTelemetry? telemetry = null;
        await foreach (var sample in source.ReadAsync(cts.Token))
        {
            telemetry = sample;
            cts.Cancel();
            break;
        }

        Assert.NotNull(telemetry);
        Assert.Equal("asset-1", telemetry!.AssetId);
        Assert.Equal(60.5, telemetry.SocPercent, precision: 4);
        Assert.Equal(-25, telemetry.ActivePowerKw, precision: 4);
        Assert.True(telemetry.Available);
        Assert.Equal(DataQualityState.Valid, telemetry.DataQuality.Flag);
    }

    [Fact]
    public async Task ReadAsync_recovers_after_transient_read_error()
    {
        var attempts = 0;
        var client = new FakeModbusClient
        {
            ReadResponses =
            {
                [100] = new ushort[] { 700 },
                [110] = new ushort[] { 0 },
                [120] = new ushort[] { 1 },
            },
        };
        client.OnRead = () =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new InvalidOperationException("transient");
            }
            return Task.CompletedTask;
        };

        var source = new ModbusTelemetrySource(
            client,
            ModbusFixtures.VendorNeutralMapping(),
            ModbusFixtures.Defaults(),
            new ModbusFixtures.FixedClock());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        BatteryTelemetry? telemetry = null;
        await foreach (var sample in source.ReadAsync(cts.Token))
        {
            telemetry = sample;
            cts.Cancel();
            break;
        }

        Assert.NotNull(telemetry);
        Assert.Equal(70, telemetry!.SocPercent, precision: 4);
        Assert.Equal(0, source.Status.ConsecutiveFailures);
        Assert.Null(source.Status.LastError);
    }

    [Fact]
    public async Task ReadAsync_records_failure_status_when_read_throws()
    {
        var client = new FakeModbusClient
        {
            OnRead = () => throw new InvalidOperationException("modbus down"),
        };
        var source = new ModbusTelemetrySource(
            client,
            ModbusFixtures.VendorNeutralMapping(),
            ModbusFixtures.Defaults(),
            new ModbusFixtures.FixedClock());

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await foreach (var _ in source.ReadAsync(cts.Token))
        {
            // expect no yield because every attempt fails
        }

        Assert.True(source.Status.ConsecutiveFailures >= 1);
        Assert.Contains("modbus down", source.Status.LastError, StringComparison.Ordinal);
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
            new ModbusTelemetrySource(
                new FakeModbusClient(), mapping, ModbusFixtures.Defaults(), new ModbusFixtures.FixedClock()));
    }

    [Fact]
    public async Task ReadAsync_routes_input_register_table_to_FC04_and_holding_to_FC03()
    {
        // RM-M2-HIL-02: a mixed-table HIL profile reads measurements
        // from input registers (FC04) and only the writable setpoint
        // from holding (FC03 read on the read-back side is moot — the
        // test asserts the read path routes by register_table).
        var client = new FakeModbusClient
        {
            InputReadResponses =
            {
                [0] = new ushort[] { 0, 0 }, // active_power_kw, two words for float32
                [2] = new ushort[] { 0, 0 }, // reactive_power_kvar
            },
            ReadResponses =
            {
                [110] = new ushort[] { 0 }, // unrelated holding-table read (M1 sentinel)
            },
        };

        var mapping = new ModbusMappingConfiguration(
            SchemaVersion: "v1",
            ProfileName: "hil",
            UnitIdDiscovery: "static",
            StaticUnitId: 1,
            Registers: new List<ModbusRegisterMapping>
            {
                new ModbusRegisterMapping(
                    "active_power_kw", 0, "float32", 1000, -250, 250,
                    false, "cyclic", "none", null, null, null)
                {
                    RegisterTable = ModbusRegisterTables.Input,
                },
                new ModbusRegisterMapping(
                    "reactive_power_kvar", 2, "float32", 1000, -250, 250,
                    false, "cyclic", "none", null, null, null)
                {
                    RegisterTable = ModbusRegisterTables.Input,
                },
                new ModbusRegisterMapping(
                    "available", 110, "uint16", 1, 0, 1,
                    false, "cyclic", "none", null, null, null),
                // No register_table set → defaults to Holding via the
                // record's init-only default.
            });

        var source = new ModbusTelemetrySource(
            client, mapping, ModbusFixtures.Defaults(), new ModbusFixtures.FixedClock());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await foreach (var _ in source.ReadAsync(cts.Token))
        {
            cts.Cancel();
            break;
        }

        // Two FC04 reads at the input addresses, one FC03 read at the
        // holding address. Order doesn't matter — the loop walks the
        // register list in declaration order, but the assertion is
        // table+address agnostic to ordering churn.
        Assert.Contains(("input", 0, 2), client.Reads);
        Assert.Contains(("input", 2, 2), client.Reads);
        Assert.Contains(("holding", 110, 1), client.Reads);
    }

    [Fact]
    public void Constructor_rejects_unknown_register_table_value()
    {
        // RM-M2-HIL-02: 'holding' and 'input' are the supported
        // values; anything else (typo, vendor-specific table) must
        // fail fast so the read-path branch stays total.
        var mapping = new ModbusMappingConfiguration(
            SchemaVersion: "v1",
            ProfileName: "p",
            UnitIdDiscovery: "static",
            StaticUnitId: 1,
            Registers: new List<ModbusRegisterMapping>
            {
                new ModbusRegisterMapping(
                    "active_power_kw", 0, "float32", 1, -100, 100,
                    false, "cyclic", "none", null, null, null)
                {
                    RegisterTable = "discrete_input",
                },
            });

        Assert.Throws<NotSupportedException>(() =>
            new ModbusTelemetrySource(
                new FakeModbusClient(), mapping, ModbusFixtures.Defaults(), new ModbusFixtures.FixedClock()));
    }

}
