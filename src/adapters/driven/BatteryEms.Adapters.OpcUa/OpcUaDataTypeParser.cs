using System.Globalization;

namespace BatteryEms.Adapters.OpcUa;

// Translates the v1 OPC-UA-Mapping-Schema's `data_type` strings to
// the adapter-side enum (plan-RM-M4-04 §4 Sub-Slice A reference). The
// Application-side OpcUaNodeMapping carries the raw string; the
// adapter uses this parser at construction time so a malformed value
// fails fast with a structured error rather than later in a hot path.
//
// The accepted strings come straight from the JSON schema — see
// config/schema/opcua-mapping.schema.json data_type enum. Strukturen,
// Arrays und Enums sind out-of-scope (F-15 in plan-RM-M4-04 §9).
internal static class OpcUaDataTypeParser
{
    public static OpcUaDataType Parse(string raw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(raw);
        return raw.ToUpperInvariant() switch
        {
            "BOOL" => OpcUaDataType.Bool,
            "INT16" => OpcUaDataType.Int16,
            "INT32" => OpcUaDataType.Int32,
            "INT64" => OpcUaDataType.Int64,
            "UINT16" => OpcUaDataType.UInt16,
            "UINT32" => OpcUaDataType.UInt32,
            "UINT64" => OpcUaDataType.UInt64,
            "FLOAT" => OpcUaDataType.Float,
            "DOUBLE" => OpcUaDataType.Double,
            "STRING" => OpcUaDataType.String,
            _ => throw new ArgumentException(
                $"Unknown OPC-UA data_type '{raw}'. Allowed: bool, int16, int32, int64, uint16, uint32, uint64, float, double, string.",
                nameof(raw)),
        };
    }

    // Variant-style coercion of the SDK-returned object? to a double.
    // OPC Foundation Reference Stack returns DataValue.Value as a
    // boxed Variant object — callers strip the Variant wrapper before
    // handing the value to this method (the FakeOpcUaClient path
    // hands raw .NET values; the production OpcUaClient does the
    // strip in Sub-Slice B's later production-binding step). For the
    // numeric data types we coerce via IConvertible so a UInt16-
    // originating value (boxed as ushort) can still feed a double
    // setpoint without a per-type switch.
    //
    // Mismatch (boxed value cannot be converted) returns false; the
    // caller surfaces this as DataQuality.ProtocolError("opcua-type-
    // mismatch"). This is the CA1031-style protocol-boundary capture
    // — we don't want a single weird Variant to crash the read loop.
    public static bool TryToDouble(object? value, out double result)
    {
        if (value is null)
        {
            result = 0;
            return false;
        }
        try
        {
            switch (value)
            {
                case bool b: result = b ? 1.0 : 0.0; return true;
                case sbyte sb: result = sb; return true;
                case byte b8: result = b8; return true;
                case short s16: result = s16; return true;
                case ushort us16: result = us16; return true;
                case int i32: result = i32; return true;
                case uint u32: result = u32; return true;
                case long i64: result = i64; return true;
                case ulong u64: result = u64; return true;
                case float f: result = f; return true;
                case double d: result = d; return true;
                case string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var sd):
                    result = sd; return true;
                default:
                    result = 0;
                    return false;
            }
        }
#pragma warning disable CA1031 // Adapter boundary — protocol-level mismatch surfaced as bool false.
        catch
#pragma warning restore CA1031
        {
            result = 0;
            return false;
        }
    }
}
