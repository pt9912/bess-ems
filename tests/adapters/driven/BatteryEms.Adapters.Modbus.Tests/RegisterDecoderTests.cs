using BatteryEms.Adapters.Modbus;
using BatteryEms.Application.Configuration;
using Xunit;

namespace BatteryEms.Adapters.Modbus.Tests;

public sealed class RegisterDecoderTests
{
    [Theory]
    [InlineData("uint16", 1)]
    [InlineData("int16", 1)]
    [InlineData("uint32", 2)]
    [InlineData("int32", 2)]
    [InlineData("float32", 2)]
    [InlineData("string", 0)]
    public void WordCount_returns_expected(string type, int expected)
    {
        Assert.Equal(expected, RegisterDecoder.WordCount(type));
    }

    [Fact]
    public void Encode_decode_uint16_roundtrips_with_scale()
    {
        var mapping = new ModbusRegisterMapping(
            Name: "soc_percent",
            Address: 100,
            Type: "uint16",
            ScaleFactor: 0.1,
            RangeMin: 0,
            RangeMax: 100,
            Writable: false,
            WriteCadence: "cyclic",
            AuthRequired: "none",
            Enum: null,
            FirmwareConstraint: null,
            SunspecModel: null);

        var encoded = RegisterDecoder.Encode(mapping, 60.5);
        var decoded = RegisterDecoder.Decode(mapping, encoded);

        Assert.Equal(60.5, decoded, precision: 5);
    }

    [Fact]
    public void Encode_decode_int16_roundtrips_with_negative_value()
    {
        var mapping = new ModbusRegisterMapping(
            "active_power_kw", 110, "int16", 0.1, -100, 100, true, "cyclic", "none", null, null, null);

        var encoded = RegisterDecoder.Encode(mapping, -25.0);
        var decoded = RegisterDecoder.Decode(mapping, encoded);

        Assert.Equal(-25.0, decoded, precision: 5);
    }

    [Fact]
    public void Encode_decode_int32_roundtrips_through_negative_range()
    {
        var mapping = new ModbusRegisterMapping(
            "active_power_w", 200, "int32", 1, -1_000_000, 1_000_000, true, "cyclic", "none", null, null, null);

        var encoded = RegisterDecoder.Encode(mapping, -123_456.0);
        Assert.Equal(2, encoded.Length);
        var decoded = RegisterDecoder.Decode(mapping, encoded);
        Assert.Equal(-123_456.0, decoded, precision: 1);
    }

    [Fact]
    public void Encode_decode_float32_roundtrips()
    {
        var mapping = new ModbusRegisterMapping(
            "temperature_celsius", 300, "float32", 1, -100, 100, false, "cyclic", "none", null, null, null);

        var encoded = RegisterDecoder.Encode(mapping, 22.5);
        var decoded = RegisterDecoder.Decode(mapping, encoded);

        Assert.Equal(22.5, decoded, precision: 4);
    }

    [Fact]
    public void Encode_unsupported_type_throws()
    {
        var mapping = new ModbusRegisterMapping(
            "x", 0, "string", 1, 0, 0, false, "cyclic", "none", null, null, null);

        Assert.Throws<NotSupportedException>(() => RegisterDecoder.Encode(mapping, 0));
    }

    [Fact]
    public void Decode_unsupported_type_throws()
    {
        var mapping = new ModbusRegisterMapping(
            "x", 0, "string", 1, 0, 0, false, "cyclic", "none", null, null, null);

        Assert.Throws<NotSupportedException>(() => RegisterDecoder.Decode(mapping, new ushort[] { 0 }));
    }
}
