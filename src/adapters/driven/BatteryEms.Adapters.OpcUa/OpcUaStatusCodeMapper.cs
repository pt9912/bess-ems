using System.Globalization;
using BatteryEms.Domain;

namespace BatteryEms.Adapters.OpcUa;

// Pure static mapper from the OPC-UA wire StatusCode to the domain
// DataQuality (plan-RM-M4-04 D-06, LH-OPCUA-004). Severity sits in
// the top two bits of the 32-bit StatusCode:
//
//   0x00xxxxxx → Good       → DataQuality.Valid
//   0x40xxxxxx → Uncertain  → DataQuality.Stale("opcua-uncertain-{name}")
//   0x80xxxxxx → Bad        → DataQuality.ProtocolError("opcua-bad-{name}")
//
// The {name} suffix uses a small static lookup of the most common
// OPC-UA StatusCode constants from the OPC Foundation spec; unknown
// codes fall back to a hex suffix ("opcua-bad-0x80ab0000") so a
// previously-unseen Bad doesn't silently lose its identity. Pin tests
// in Sub-Slice A cover Good (0x0), a representative Uncertain
// (UncertainLastUsableValue=0x40A40000), a representative Bad
// (BadNotConnected=0x80AB0000), and the unknown-hex fallback path.
public static class OpcUaStatusCodeMapper
{
    private const uint SeverityMask = 0xC0000000u;
    private const uint SeverityGood = 0x00000000u;
    private const uint SeverityUncertain = 0x40000000u;

    // Subset of the OPC-UA spec's named StatusCodes. Extended on
    // demand; unknown codes still surface (see FormatHex). The
    // dictionary is built once at class-init and never mutated — the
    // direct Dictionary<,> type satisfies CA1859 (no virtual-call
    // overhead through IReadOnlyDictionary<,>).
    private static readonly Dictionary<uint, string> KnownCodes =
        new Dictionary<uint, string>
        {
            // Good
            [0x00000000u] = "good",

            // Uncertain
            [0x40000000u] = "uncertain",
            [0x40A40000u] = "uncertain-last-usable-value",
            [0x40A20000u] = "uncertain-sensor-not-accurate",
            [0x40A30000u] = "uncertain-engineering-units-exceeded",
            [0x40A10000u] = "uncertain-sub-normal",
            [0x40A50000u] = "uncertain-substitute-value",
            [0x408F0000u] = "uncertain-no-communication-last-usable-value",
            [0x40900000u] = "uncertain-last-usable-value-stale",

            // Bad
            [0x80000000u] = "bad",
            [0x80AB0000u] = "bad-not-connected",
            [0x80050000u] = "bad-internal-error",
            [0x800A0000u] = "bad-timeout",
            [0x80060000u] = "bad-out-of-memory",
            [0x80070000u] = "bad-resource-unavailable",
            [0x80080000u] = "bad-communication-error",
            [0x80090000u] = "bad-encoding-error",
            [0x80130000u] = "bad-no-communication",
            [0x80140000u] = "bad-waiting-for-initial-data",
            [0x800F0000u] = "bad-server-not-connected",
            [0x80190000u] = "bad-not-readable",
            [0x801A0000u] = "bad-not-writable",
            [0x801E0000u] = "bad-type-mismatch",
            [0x80220000u] = "bad-out-of-range",
            [0x80350000u] = "bad-session-id-invalid",
            [0x80360000u] = "bad-session-closed",
            [0x80370000u] = "bad-session-not-activated",
            [0x806A0000u] = "bad-not-supported",
        };

    public static DataQuality Map(uint statusCode)
    {
        var severity = statusCode & SeverityMask;
        if (severity == SeverityGood)
        {
            return DataQuality.Valid;
        }
        var name = LookupName(statusCode);
        if (severity == SeverityUncertain)
        {
            return DataQuality.Stale($"opcua-uncertain-{name}");
        }
        // Bad (0x80xxxxxx) and the reserved 0xCxxxxxxx family both
        // surface as ProtocolError — no usable data either way.
        return DataQuality.ProtocolError($"opcua-bad-{name}");
    }

    private static string LookupName(uint statusCode)
    {
        if (KnownCodes.TryGetValue(statusCode, out var name))
        {
            // For Good we already returned; for Uncertain/Bad the
            // dictionary entry already encodes the severity prefix in
            // the spec name (e.g. "bad-not-connected"). The Map()
            // caller adds the "opcua-" prefix unconditionally; the
            // resulting code looks like "opcua-bad-bad-not-connected"
            // for the spec-named entries because the dictionary
            // values keep their severity hint. Strip the leading
            // severity word when present so the emitted reason is
            // tidy ("opcua-bad-not-connected", not "opcua-bad-bad-
            // not-connected").
            return StripLeadingSeverityWord(name);
        }
        return FormatHex(statusCode);
    }

    private static string StripLeadingSeverityWord(string name)
    {
        if (name.StartsWith("bad-", StringComparison.Ordinal))
        {
            return name[4..];
        }
        if (name.StartsWith("uncertain-", StringComparison.Ordinal))
        {
            return name[10..];
        }
        return name;
    }

    private static string FormatHex(uint statusCode) =>
        "0x" + statusCode.ToString("x8", CultureInfo.InvariantCulture);
}
