namespace BatteryEms.Adapters.OpcUa;

// Adapter-side wire-result records returned by IOpcUaClient (plan-RM-
// M4-04 §4 Sub-Slice A). The values here are intentionally raw —
// Variant-unboxed but not yet ScaleFactor-applied; the Telemetry
// Source / Command Sink layer (Sub-Slice B/C) does the Domain
// translation. StatusCode is the OPC-UA wire repräsentation (uint32
// with severity in the top two bits — see OpcUaStatusCodeMapper /
// D-06).

public sealed record OpcUaReadResult(
    string NodeId,
    object? Value,
    uint StatusCode,
    DateTimeOffset SourceTimestamp);

public sealed record OpcUaWriteResult(
    string NodeId,
    uint StatusCode);

public sealed record OpcUaNotification(
    string NodeId,
    object? Value,
    uint StatusCode,
    DateTimeOffset SourceTimestamp);
