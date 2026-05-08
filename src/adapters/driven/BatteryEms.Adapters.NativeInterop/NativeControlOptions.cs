namespace BatteryEms.Adapters.NativeInterop;

// RM-M3-03 configuration for the native control core adapter.
//
// Enabled defaults to false: the M3 default policy (per
// docs/user/quality.md §5.2 + architecture §13.4) is that the host
// must come up on the managed path even when a perfectly valid
// library sits next to it. The opt-in protects production until
// RM-M3-05 wires the actual routing.
//
// LibraryPath defaults to /app/native/libbattery_control_core.so —
// the path RM-M3-06 part 2 will eventually use. The default is
// only consulted when Enabled=true; for tests and unmanaged hosts
// the field is freely overridable.
//
// AbortOnAbiMismatch is the explicit production-policy escape hatch
// from §5.2: a deployment can opt to fail-fast on incompatible ABI
// rather than silently fall back. Default is false to preserve the
// M3 fallback-default contract.
public sealed record NativeControlOptions
{
    public bool Enabled { get; init; }

    public string LibraryPath { get; init; } =
        "/app/native/libbattery_control_core.so";

    public bool AbortOnAbiMismatch { get; init; }
}
