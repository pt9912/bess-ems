using BatteryEms.Application.Configuration;
using BatteryEms.Application.Time;
using BatteryEms.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.Adapters.OpcUa.Tests;

public sealed class OpcUaCommandSinkTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 9, 12, 0, 0, TimeSpan.Zero);

    private static readonly BatteryAsset Asset = new(
        assetId: "asset-1",
        capacityKwh: 100,
        maxChargePowerKw: 50,
        maxDischargePowerKw: 50,
        minSocPercent: 10,
        maxSocPercent: 90,
        chargeEfficiency: 0.95,
        dischargeEfficiency: 0.95,
        maxRampKwPerSecond: 100,
        minOperatingTemperatureCelsius: -20,
        maxOperatingTemperatureCelsius: 55);

    private static OpcUaAdapterOptions Options() => new()
    {
        EndpointUrl = new Uri("opc.tcp://localhost:4840"),
        AllowUnsecured = true,
        AllowUnsecuredReason = "command-sink-tests",
        ReadTimeout = TimeSpan.FromSeconds(2),
    };

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
    }

    private static OpcUaNodeMapping WriteNode(
        string name,
        string nodeId,
        string dataType = "float",
        double scaleFactor = 1.0,
        string writeCadence = "cyclic")
        => new(
            Name: name,
            NodeId: nodeId,
            Direction: "write",
            DataType: dataType,
            ScaleFactor: scaleFactor,
            Writable: true,
            AuthRequired: "none",
            WriteCadence: writeCadence);

    private static OpcUaMappingConfiguration Mapping(params OpcUaNodeMapping[] nodes)
        => new("v1", "test", nodes);

    private static OpcUaCommandSink BuildSink(
        IOpcUaClient client,
        OpcUaMappingConfiguration mapping)
        => new(client, mapping, Asset, Options(), new FakeClock(),
            NullLogger<OpcUaCommandSink>.Instance);

    private static BatteryCommand Command(double activeKw = 25, double? reactiveKvar = null)
        => new(
            CommandId: "cmd-1",
            Timestamp: Now,
            AssetId: "asset-1",
            Mode: activeKw > 0 ? CommandMode.Discharge
                : activeKw < 0 ? CommandMode.Charge
                : CommandMode.Idle,
            ActivePowerKw: activeKw,
            ReactivePowerKvar: reactiveKvar,
            ValidUntil: Now.AddSeconds(10),
            Reason: "test",
            Source: CommandSource.Optimization);

    // Plan §4 Sub-Slice C Sink-Write-Pin: Setpoint-Mapping +
    // ScaleFactor + Good-StatusCode → Ok.
    [Fact]
    public async Task Setpoint_with_scale_factor_writes_inverse_scaled_value_and_returns_ok()
    {
        var client = new FakeOpcUaClient();
        await client.ConnectAsync(CancellationToken.None);
        var mapping = Mapping(
            WriteNode("active_power_setpoint_kw", "ns=2;P", scaleFactor: 0.1));
        var sink = BuildSink(client, mapping);

        var result = await sink.WriteAsync(Command(activeKw: 25.0), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Single(client.Writes);
        var write = client.Writes[0];
        Assert.Equal("ns=2;P", write.NodeId);
        Assert.Equal(OpcUaDataType.Float, write.DataType);
        // 25 / 0.1 = 250 — der Sink dividiert beim Schreiben (Roundtrip
        // mit dem Source-Multiply auf 250 * 0.1 = 25).
        Assert.Equal(250f, (float)write.Value);
    }

    // Plan §4 Sub-Slice C Bad-StatusCode-Pfad: kebab-case-Reason mit
    // Knoten-Suffix.
    [Fact]
    public async Task Bad_status_on_setpoint_returns_failed_with_kebab_reason()
    {
        var client = new FakeOpcUaClient();
        await client.ConnectAsync(CancellationToken.None);
        // BadNotConnected = 0x80AB0000 → opcua-bad-not-connected
        client.SetWriteStatusCode("ns=2;P", 0x80AB0000u);
        var sink = BuildSink(client, Mapping(
            WriteNode("active_power_setpoint_kw", "ns=2;P")));

        var result = await sink.WriteAsync(Command(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(
            "opcua-bad-not-connected:active_power_setpoint_kw",
            result.Reason);
    }

    // Plan §4 Sub-Slice C Mapping-Mismatch-Pfad: Setpoint fehlt im
    // Mapping → opcua-mapping-not-writable.
    [Fact]
    public async Task Missing_setpoint_node_returns_opcua_mapping_not_writable()
    {
        var client = new FakeOpcUaClient();
        await client.ConnectAsync(CancellationToken.None);
        // Mapping enthält nur den Q-Setpoint, nicht den P-Setpoint.
        var sink = BuildSink(client, Mapping(
            WriteNode("reactive_power_setpoint_kvar", "ns=2;Q")));

        var result = await sink.WriteAsync(Command(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("opcua-mapping-not-writable", result.Reason);
        Assert.Empty(client.Writes);
    }

    // Plan §4 Sub-Slice C Mapping-Mismatch: Knoten ist nicht writable
    // (Operator hat write_cadence vergessen oder Writable=false gesetzt).
    [Fact]
    public async Task Setpoint_with_writable_false_is_treated_as_missing()
    {
        var client = new FakeOpcUaClient();
        await client.ConnectAsync(CancellationToken.None);
        var nonWritable = new OpcUaNodeMapping(
            Name: "active_power_setpoint_kw",
            NodeId: "ns=2;P",
            Direction: "write",
            DataType: "float",
            ScaleFactor: 1.0,
            Writable: false, // <-- hier liegt der Fehler
            AuthRequired: "none",
            WriteCadence: "cyclic");
        var sink = BuildSink(client, Mapping(nonWritable));

        var result = await sink.WriteAsync(Command(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("opcua-mapping-not-writable", result.Reason);
    }

    // Defensive Pin: ScaleFactor==0 würde durch Null teilen — Sink
    // failed-closed mit kebab-case-Reason. Plan §4 Sub-Slice C
    // explizite Test-Liste.
    [Fact]
    public async Task Zero_scale_factor_is_fail_closed_with_opcua_mapping_scale_zero()
    {
        var client = new FakeOpcUaClient();
        await client.ConnectAsync(CancellationToken.None);
        var sink = BuildSink(client, Mapping(
            WriteNode("active_power_setpoint_kw", "ns=2;P", scaleFactor: 0.0)));

        var result = await sink.WriteAsync(Command(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("opcua-mapping-scale-zero", result.Reason);
        Assert.Empty(client.Writes);
    }

    // Reactive setpoint optional: P fließt durch, Q wird gedropt mit
    // Reason-Suffix wenn der Knoten nicht im Mapping ist (analog zur
    // Modbus-q-dropped-Linie).
    [Fact]
    public async Task Reactive_dropped_when_no_mapping_emits_reason_suffix()
    {
        var client = new FakeOpcUaClient();
        await client.ConnectAsync(CancellationToken.None);
        var sink = BuildSink(client, Mapping(
            WriteNode("active_power_setpoint_kw", "ns=2;P")));

        var result = await sink.WriteAsync(
            Command(activeKw: 10, reactiveKvar: 5), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("q-dropped:no-mapping", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reactive_setpoint_with_mapping_writes_both()
    {
        var client = new FakeOpcUaClient();
        await client.ConnectAsync(CancellationToken.None);
        var sink = BuildSink(client, Mapping(
            WriteNode("active_power_setpoint_kw", "ns=2;P"),
            WriteNode("reactive_power_setpoint_kvar", "ns=2;Q")));

        var result = await sink.WriteAsync(
            Command(activeKw: 10, reactiveKvar: 4), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, client.Writes.Count);
        var qWrite = client.Writes.First(w => w.NodeId == "ns=2;Q");
        Assert.Equal(4f, (float)qWrite.Value);
    }

    // D-04 Konstruktor-Pin: ein Sink mit Default-Security-Options
    // failed beim Bau (EnsureValid wirft opcua-security-not-hardened).
    [Fact]
    public void Constructor_with_unsafe_default_options_throws_security_guard()
    {
        var unsafeOptions = new OpcUaAdapterOptions
        {
            EndpointUrl = new Uri("opc.tcp://localhost:4840"),
            // AllowUnsecured=false default
        };
        var client = new FakeOpcUaClient();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new OpcUaCommandSink(client, Mapping(
                WriteNode("active_power_setpoint_kw", "ns=2;P")),
                Asset, unsafeOptions, new FakeClock(),
                NullLogger<OpcUaCommandSink>.Instance));
        Assert.Contains("opcua-security-not-hardened", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_null_args_throw()
    {
        var client = new FakeOpcUaClient();
        var mapping = Mapping(WriteNode("active_power_setpoint_kw", "ns=2;P"));
        var options = Options();
        var clock = new FakeClock();
        var logger = NullLogger<OpcUaCommandSink>.Instance;
        Assert.Throws<ArgumentNullException>(() =>
            new OpcUaCommandSink(null!, mapping, Asset, options, clock, logger));
        Assert.Throws<ArgumentNullException>(() =>
            new OpcUaCommandSink(client, null!, Asset, options, clock, logger));
        Assert.Throws<ArgumentNullException>(() =>
            new OpcUaCommandSink(client, mapping, null!, options, clock, logger));
        Assert.Throws<ArgumentNullException>(() =>
            new OpcUaCommandSink(client, mapping, Asset, null!, clock, logger));
        Assert.Throws<ArgumentNullException>(() =>
            new OpcUaCommandSink(client, mapping, Asset, options, null!, logger));
        Assert.Throws<ArgumentNullException>(() =>
            new OpcUaCommandSink(client, mapping, Asset, options, clock, null!));
    }

    // D-09 Lifecycle-Pin: post-DisposeAsync returnt WriteAsync
    // Failed("opcua-sink-disposed") statt zu werfen.
    [Fact]
    public async Task Write_after_dispose_async_returns_failed_without_throwing()
    {
        var client = new FakeOpcUaClient();
        await client.ConnectAsync(CancellationToken.None);
        var sink = BuildSink(client, Mapping(
            WriteNode("active_power_setpoint_kw", "ns=2;P")));

        await sink.DisposeAsync();

        var result = await sink.WriteAsync(Command(), CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal("opcua-sink-disposed", result.Reason);
    }

    [Fact]
    public async Task Dispose_async_is_idempotent()
    {
        var sink = BuildSink(new FakeOpcUaClient(), Mapping(
            WriteNode("active_power_setpoint_kw", "ns=2;P")));
        await sink.DisposeAsync();
        await sink.DisposeAsync();
    }

    // Write-cadence-Filter: ein Knoten mit write_cadence != "cyclic"
    // wird heute nicht akzeptiert (Sub-Slice-C-Scope).
    [Fact]
    public async Task Setpoint_with_non_cyclic_cadence_is_rejected()
    {
        var client = new FakeOpcUaClient();
        await client.ConnectAsync(CancellationToken.None);
        var sink = BuildSink(client, Mapping(
            WriteNode("active_power_setpoint_kw", "ns=2;P", writeCadence: "one_shot")));

        var result = await sink.WriteAsync(Command(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("opcua-write-cadence-one_shot-not-supported",
            result.Reason, StringComparison.Ordinal);
    }

    // Sub-Slice-C review-fix #1: ein Charge-Befehl (negativer
    // ActivePower) auf einem fälschlich uint-gemappten Setpoint
    // surfaced den Mapping-Bug als typisierten Fehler statt silent
    // auf 0 zu klemmen.
    [Theory]
    [InlineData("uint16")]
    [InlineData("uint32")]
    [InlineData("uint64")]
    public async Task Negative_active_power_into_uint_setpoint_is_fail_closed(string dataType)
    {
        var client = new FakeOpcUaClient();
        await client.ConnectAsync(CancellationToken.None);
        var sink = BuildSink(client, Mapping(
            WriteNode("active_power_setpoint_kw", "ns=2;P", dataType: dataType)));

        var result = await sink.WriteAsync(
            Command(activeKw: -25), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("opcua-uint-cannot-encode-negative-value",
            result.Reason, StringComparison.Ordinal);
        Assert.Contains("active_power_setpoint_kw",
            result.Reason, StringComparison.Ordinal);
        // Pin: kein Write durchgegangen — Bug-State surfaced statt
        // silent auf 0 zu schreiben.
        Assert.Empty(client.Writes);
    }

    [Fact]
    public async Task Negative_reactive_power_into_uint_setpoint_is_fail_closed()
    {
        var client = new FakeOpcUaClient();
        await client.ConnectAsync(CancellationToken.None);
        var sink = BuildSink(client, Mapping(
            WriteNode("active_power_setpoint_kw", "ns=2;P", dataType: "float"),
            WriteNode("reactive_power_setpoint_kvar", "ns=2;Q", dataType: "uint16")));

        var result = await sink.WriteAsync(
            Command(activeKw: 10, reactiveKvar: -3), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("opcua-uint-cannot-encode-negative-value",
            result.Reason, StringComparison.Ordinal);
        Assert.Contains("reactive_power_setpoint_kvar",
            result.Reason, StringComparison.Ordinal);
    }

    // Sub-Slice-C review-fix #7: integer mappings truncate (don't
    // round). The mapping-side mechanism für Sub-Integer-Präzision
    // ist `scale_factor` — hier dokumentiert per Pin.
    [Fact]
    public async Task Integer_mapping_truncates_fractional_value_without_scale_factor()
    {
        var client = new FakeOpcUaClient();
        await client.ConnectAsync(CancellationToken.None);
        var sink = BuildSink(client, Mapping(
            WriteNode("active_power_setpoint_kw", "ns=2;P", dataType: "int16")));

        await sink.WriteAsync(Command(activeKw: 25.7), CancellationToken.None);

        Assert.Single(client.Writes);
        // 25.7 → (short)25.7 = 25; the 0.7 is lost. Operator uses
        // scale_factor=0.1 with int16 to widen precision to tenths.
        Assert.Equal((short)25, (short)client.Writes[0].Value);
    }

    [Fact]
    public async Task Integer_mapping_with_scale_factor_widens_precision()
    {
        var client = new FakeOpcUaClient();
        await client.ConnectAsync(CancellationToken.None);
        var sink = BuildSink(client, Mapping(
            WriteNode("active_power_setpoint_kw", "ns=2;P",
                dataType: "int16", scaleFactor: 0.1)));

        await sink.WriteAsync(Command(activeKw: 25.7), CancellationToken.None);

        // 25.7 / 0.1 = 257 → (short)257 = 257; round-trip via
        // source-multiply-by-0.1 = 25.7 again.
        Assert.Equal((short)257, (short)client.Writes[0].Value);
    }
}
