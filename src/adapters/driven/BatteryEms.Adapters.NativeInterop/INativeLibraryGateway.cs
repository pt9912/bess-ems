namespace BatteryEms.Adapters.NativeInterop;

// RM-M3-03 testability seam for the OS-level dlopen/dlsym calls.
//
// Production wires SystemNativeLibraryGateway, which delegates
// directly to System.Runtime.InteropServices.NativeLibrary; tests
// inject a fake that exercises every loader outcome without an
// actual .so on disk. The interface is internal so production
// callers cannot bypass the loader's invariant ordering, while
// the tests project still has visibility through
// InternalsVisibleTo.
internal interface INativeLibraryGateway
{
    bool FileExists(string path);

    // Throws on failure (caller turns the exception into a
    // LoadFailed result). Returns the OS handle on success.
    nint Load(string path);

    // Resolves and invokes battery_control_core_abi_version on the
    // loaded handle. Throws on missing export. Kept on the gateway
    // (rather than exposing GetExport + delegate marshalling) so
    // the loader stays free of P/Invoke plumbing and tests can
    // inject any uint without the cdecl-delegate dance.
    uint CallAbiVersion(nint handle);

    // RM-M3-04: invokes battery_control_core_compute on the loaded
    // handle. Same shape contract as CallAbiVersion — production
    // resolves and caches the export internally; tests substitute
    // a deterministic command without touching a .so.
    int CallCompute(
        nint handle,
        in BccSnapshot snapshot,
        in BccLimits limits,
        in BccRequest request,
        out BccCommand command);

    // RM-M3-13: invokes battery_control_core_pid_step on the loaded
    // handle. Mirrors CallCompute's shape — production caches the
    // delegate, tests inject a deterministic command. The export
    // is only available on libraries reporting ABI minor >= 2.
    int CallPidStep(
        nint handle,
        in BccPidState state,
        in BccPidOptions options,
        in BccPidInput input,
        out BccPidCommand command);

    // RM-M5-03: invokes battery_control_core_filter_telemetry on the
    // loaded handle. The export is additive and available on ABI
    // minor >= 3.
    int CallFilterTelemetry(
        nint handle,
        in BccTelemetryFilterState state,
        in BccTelemetryFilterOptions options,
        in BccTelemetryFilterInput input,
        out BccTelemetryFilterOutput output);

    // RM-M3-04: releases the handle. NativeLibrary.Free is the
    // production wiring; tests can no-op.
    void Free(nint handle);
}
