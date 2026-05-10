using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Adapters.OpcUa.Tests;

public sealed class OpcUaStatusCodeMapperTests
{
    [Fact]
    public void Good_status_code_maps_to_valid()
    {
        var quality = OpcUaStatusCodeMapper.Map(0x00000000u);

        Assert.Equal(DataQualityState.Valid, quality.Flag);
        Assert.True(quality.IsUsableForControl);
    }

    // D-06 representative Bad: BadNotConnected=0x80AB0000 ⇒
    // ProtocolError("opcua-bad-not-connected") (severity word "bad-"
    // stripped from the dictionary value to avoid the doubled prefix).
    [Fact]
    public void Bad_not_connected_maps_to_protocol_error_with_named_reason()
    {
        var quality = OpcUaStatusCodeMapper.Map(0x80AB0000u);

        Assert.Equal(DataQualityState.ProtocolError, quality.Flag);
        Assert.Equal("opcua-bad-not-connected", quality.Reason);
    }

    [Theory]
    [InlineData(0x80050000u, "opcua-bad-internal-error")]
    [InlineData(0x800A0000u, "opcua-bad-timeout")]
    [InlineData(0x80080000u, "opcua-bad-communication-error")]
    [InlineData(0x801E0000u, "opcua-bad-type-mismatch")]
    public void Common_bad_codes_map_to_named_reason(uint statusCode, string expected)
    {
        var quality = OpcUaStatusCodeMapper.Map(statusCode);

        Assert.Equal(DataQualityState.ProtocolError, quality.Flag);
        Assert.Equal(expected, quality.Reason);
    }

    // D-06 representative Uncertain: UncertainLastUsableValue=
    // 0x40A40000 ⇒ Stale("opcua-uncertain-last-usable-value").
    [Fact]
    public void Uncertain_last_usable_value_maps_to_stale_with_named_reason()
    {
        var quality = OpcUaStatusCodeMapper.Map(0x40A40000u);

        Assert.Equal(DataQualityState.Stale, quality.Flag);
        Assert.Equal("opcua-uncertain-last-usable-value", quality.Reason);
    }

    [Theory]
    [InlineData(0x40A20000u, "opcua-uncertain-sensor-not-accurate")]
    [InlineData(0x40A10000u, "opcua-uncertain-sub-normal")]
    public void Common_uncertain_codes_map_to_named_reason(uint statusCode, string expected)
    {
        var quality = OpcUaStatusCodeMapper.Map(statusCode);

        Assert.Equal(DataQualityState.Stale, quality.Flag);
        Assert.Equal(expected, quality.Reason);
    }

    // D-06 unknown-code fallback: Bad code not in the lookup falls
    // through to a hex-encoded reason (no silent identity loss).
    [Fact]
    public void Unknown_bad_code_falls_back_to_hex_suffix()
    {
        var quality = OpcUaStatusCodeMapper.Map(0x80FF0000u);

        Assert.Equal(DataQualityState.ProtocolError, quality.Flag);
        Assert.Equal("opcua-bad-0x80ff0000", quality.Reason);
    }

    [Fact]
    public void Unknown_uncertain_code_falls_back_to_hex_suffix()
    {
        var quality = OpcUaStatusCodeMapper.Map(0x40FF0000u);

        Assert.Equal(DataQualityState.Stale, quality.Flag);
        Assert.Equal("opcua-uncertain-0x40ff0000", quality.Reason);
    }
}
