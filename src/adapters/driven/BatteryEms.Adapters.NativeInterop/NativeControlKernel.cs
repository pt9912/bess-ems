namespace BatteryEms.Adapters.NativeInterop;

// RM-M3-04 thin wrapper around the native compute call. Stores the
// loaded library handle and forwards battery_control_core_compute
// invocations through the gateway. The kernel is the seam where
// the production routing (RM-M3-05) plugs the native path into the
// control cycle; until then the type compiles, has unit-test
// coverage through the gateway seam, and frees the handle on
// Dispose so a host that opted into NativeControl can shut down
// cleanly.
public sealed class NativeControlKernel : IDisposable
{
    private readonly INativeLibraryGateway _gateway;
    private readonly nint _handle;
    private bool _disposed;

    // Production callers receive the handle from
    // NativeControlLoader once it returns a Loaded result. The
    // public constructor is the entry point for Routing wiring
    // (RM-M3-05); the gateway-aware constructor stays internal so
    // tests can substitute a fake gateway without exposing the
    // OS-level seam to production code.
    public NativeControlKernel(nint handle)
        : this(handle, new SystemNativeLibraryGateway())
    {
    }

    internal NativeControlKernel(nint handle, INativeLibraryGateway gateway)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        if (handle == 0)
        {
            throw new ArgumentException(
                "Native control kernel requires a non-zero library handle "
                + "from a successful NativeControlLoader.TryLoad result.",
                nameof(handle));
        }
        _handle = handle;
        _gateway = gateway;
    }

    // Forwards the native compute call. Returns the BccStatus value
    // straight from the C-ABI and writes the produced command into
    // out_command. The .NET-side mapping into managed
    // CommandMode / Reason strings happens at the routing layer
    // (RM-M3-05) so this surface stays a thin marshal-only wrapper.
    public int Compute(
        in BccSnapshot snapshot,
        in BccLimits limits,
        in BccRequest request,
        out BccCommand command)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _gateway.CallCompute(
            _handle, in snapshot, in limits, in request, out command);
    }

    // RM-M3-13 PID slice. Mirrors Compute one-to-one — the handle is
    // shared across both exports because the .so carries them both,
    // and the routing layer (managed precheck + state-management)
    // lives in the M3-D2 IPidKernel / ManagedPidKernel pair, not
    // here. This thin wrapper keeps the cross-boundary shape
    // marshal-only.
    public int PidStep(
        in BccPidState state,
        in BccPidOptions options,
        in BccPidInput input,
        out BccPidCommand command)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _gateway.CallPidStep(
            _handle, in state, in options, in input, out command);
    }

    // RM-M5-03 telemetry filter. This stays a thin marshal-only
    // wrapper like Compute/PidStep; policy (when to use the native
    // filter, and how to route unhealthy MPC state) remains above
    // the native adapter.
    public int FilterTelemetry(
        in BccTelemetryFilterState state,
        in BccTelemetryFilterOptions options,
        in BccTelemetryFilterInput input,
        out BccTelemetryFilterOutput output)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _gateway.CallFilterTelemetry(
            _handle, in state, in options, in input, out output);
    }

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        _gateway.Free(_handle);
    }
}
