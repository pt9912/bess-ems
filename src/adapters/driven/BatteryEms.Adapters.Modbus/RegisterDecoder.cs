using BatteryEms.Application.Configuration;

namespace BatteryEms.Adapters.Modbus;

public static class RegisterDecoder
{
    public static int WordCount(string type) => type switch
    {
        "uint16" or "int16" => 1,
        "uint32" or "int32" or "float32" => 2,
        _ => 0,
    };

    public static double Decode(ModbusRegisterMapping mapping, ReadOnlySpan<ushort> words)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        var raw = mapping.Type switch
        {
            "uint16" => (double)words[0],
            "int16" => (short)words[0],
            "uint32" => Combine32(words),
            "int32" => unchecked((int)Combine32(words)),
            "float32" => DecodeFloat32(words),
            _ => throw new NotSupportedException($"Unsupported register type '{mapping.Type}'."),
        };
        return raw * mapping.ScaleFactor;
    }

    public static ushort[] Encode(ModbusRegisterMapping mapping, double engineeringValue)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        var scaled = mapping.ScaleFactor != 0 ? engineeringValue / mapping.ScaleFactor : engineeringValue;
        return mapping.Type switch
        {
            "uint16" => new[] { (ushort)scaled },
            "int16" => new[] { unchecked((ushort)(short)scaled) },
            "uint32" => Split32((uint)scaled),
            "int32" => Split32(unchecked((uint)(int)scaled)),
            "float32" => SplitFloat32((float)scaled),
            _ => throw new NotSupportedException($"Unsupported register type '{mapping.Type}'."),
        };
    }

    private static uint Combine32(ReadOnlySpan<ushort> words) =>
        ((uint)words[0] << 16) | words[1];

    private static float DecodeFloat32(ReadOnlySpan<ushort> words) =>
        BitConverter.UInt32BitsToSingle(Combine32(words));

    private static ushort[] Split32(uint value) =>
        new[] { (ushort)(value >> 16), (ushort)(value & 0xFFFF) };

    private static ushort[] SplitFloat32(float value) =>
        Split32(BitConverter.SingleToUInt32Bits(value));
}
