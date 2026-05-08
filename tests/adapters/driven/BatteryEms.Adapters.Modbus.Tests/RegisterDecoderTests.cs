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

    [Fact]
    public void Encode_decode_float32_with_low_high_word_order_roundtrips()
    {
        // RM-M2-HIL-03: same value, swapped word order on the wire.
        // The roundtrip works as long as Decode and Encode share the
        // same WordOrder, which is the contract for a single mapping.
        var mapping = new ModbusRegisterMapping(
            "active_power_kw", 0, "float32", 1, -250, 250,
            false, "cyclic", "none", null, null, null)
        {
            WordOrder = ModbusWordOrders.LowHigh,
        };

        var encoded = RegisterDecoder.Encode(mapping, 22.5);
        var decoded = RegisterDecoder.Decode(mapping, encoded);

        Assert.Equal(22.5, decoded, precision: 4);
    }

    [Fact]
    public void Float32_high_low_and_low_high_produce_swapped_words_for_the_same_value()
    {
        // The two encodings differ exactly by swapping the two words —
        // that's the entire point of the WordOrder switch. Pinning
        // this property keeps a future Combine32 refactor honest.
        var highLow = new ModbusRegisterMapping(
            "x", 0, "float32", 1, -100, 100,
            false, "cyclic", "none", null, null, null);
        // ↑ default WordOrder is HighLow
        var lowHigh = new ModbusRegisterMapping(
            "x", 0, "float32", 1, -100, 100,
            false, "cyclic", "none", null, null, null)
        {
            WordOrder = ModbusWordOrders.LowHigh,
        };

        var encodedHighLow = RegisterDecoder.Encode(highLow, 1.5);
        var encodedLowHigh = RegisterDecoder.Encode(lowHigh, 1.5);

        Assert.Equal(2, encodedHighLow.Length);
        Assert.Equal(2, encodedLowHigh.Length);
        Assert.Equal(encodedHighLow[0], encodedLowHigh[1]);
        Assert.Equal(encodedHighLow[1], encodedLowHigh[0]);
    }

    [Fact]
    public void Encode_decode_int32_with_low_high_word_order_roundtrips_through_negative_range()
    {
        var mapping = new ModbusRegisterMapping(
            "active_power_w", 0, "int32", 1, -1_000_000, 1_000_000,
            true, "cyclic", "none", null, null, null)
        {
            WordOrder = ModbusWordOrders.LowHigh,
        };

        var encoded = RegisterDecoder.Encode(mapping, -123_456.0);
        Assert.Equal(2, encoded.Length);
        var decoded = RegisterDecoder.Decode(mapping, encoded);
        Assert.Equal(-123_456.0, decoded, precision: 1);
    }

    [Fact]
    public void Decode_unknown_word_order_throws()
    {
        // Programmatic construction can hand in a value that the
        // schema enum would reject; the decoder rejects it here so
        // the switch stays exhaustive.
        var mapping = new ModbusRegisterMapping(
            "x", 0, "float32", 1, 0, 0,
            false, "cyclic", "none", null, null, null)
        {
            WordOrder = "middle_endian",
        };

        Assert.Throws<NotSupportedException>(() =>
            RegisterDecoder.Decode(mapping, new ushort[] { 0, 0 }));
    }
}
