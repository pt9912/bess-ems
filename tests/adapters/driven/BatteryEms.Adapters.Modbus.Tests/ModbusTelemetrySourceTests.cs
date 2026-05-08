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
            ProfileName: "x",
            UnitIdDiscovery: "dynamic",
            StaticUnitId: null,
            Registers: new List<ModbusRegisterMapping>());

        Assert.Throws<NotSupportedException>(() =>
            new ModbusTelemetrySource(
                new FakeModbusClient(), mapping, ModbusFixtures.Defaults(), new ModbusFixtures.FixedClock()));
    }

    [Fact]
    public void Constructor_rejects_input_register_table_until_HIL_02_lands()
    {
        // RM-M2-HIL-01: schema + loader plumb register_table through,
        // but the read path still calls ReadHoldingRegistersAsync only.
        // The guard fails fast so an HIL profile that arrives early
        // cannot silently read FC03 against an FC04-only device.
        var mapping = new ModbusMappingConfiguration(
            ProfileName: "p",
            UnitIdDiscovery: "static",
            StaticUnitId: 1,
            Registers: new List<ModbusRegisterMapping>
            {
                new ModbusRegisterMapping(
                    "active_power_kw", 0, "float32", 1, -100, 100,
                    false, "cyclic", "none", null, null, null)
                {
                    RegisterTable = ModbusRegisterTables.Input,
                },
            });

        var ex = Assert.Throws<NotSupportedException>(() =>
            new ModbusTelemetrySource(
                new FakeModbusClient(), mapping, ModbusFixtures.Defaults(), new ModbusFixtures.FixedClock()));
        Assert.Contains("RM-M2-HIL-02", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_rejects_low_high_word_order_until_HIL_03_lands()
    {
        // RM-M2-HIL-01: word_order=low_high decoder support lands in
        // HIL-03; until then, accepting it would produce silently
        // mis-decoded 32-bit values.
        var mapping = new ModbusMappingConfiguration(
            ProfileName: "p",
            UnitIdDiscovery: "static",
            StaticUnitId: 1,
            Registers: new List<ModbusRegisterMapping>
            {
                new ModbusRegisterMapping(
                    "active_power_kw", 0, "float32", 1, -100, 100,
                    false, "cyclic", "none", null, null, null)
                {
                    WordOrder = ModbusWordOrders.LowHigh,
                },
            });

        var ex = Assert.Throws<NotSupportedException>(() =>
            new ModbusTelemetrySource(
                new FakeModbusClient(), mapping, ModbusFixtures.Defaults(), new ModbusFixtures.FixedClock()));
        Assert.Contains("RM-M2-HIL-03", ex.Message, StringComparison.Ordinal);
    }
}
