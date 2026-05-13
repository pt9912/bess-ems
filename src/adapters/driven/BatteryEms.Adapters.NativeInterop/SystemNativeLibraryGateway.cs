using System.Runtime.InteropServices;

namespace BatteryEms.Adapters.NativeInterop;

// RM-M3-03 production gateway. Wraps the .NET 10 `NativeLibrary`
// API and the cdecl P/Invoke for battery_control_core_abi_version.
// Internal so the only entry point is via NativeControlLoader; the
// type does not exist for callers that try to bypass it.
internal sealed class SystemNativeLibraryGateway : INativeLibraryGateway
{
    private const string AbiVersionExport = "battery_control_core_abi_version";
    private const string ComputeExport    = "battery_control_core_compute";
    private const string PidStepExport    = "battery_control_core_pid_step";
    private const string FilterTelemetryExport = "battery_control_core_filter_telemetry";

    // The cdecl delegates are cached after the first lookup so the
    // hot Compute path doesn't pay a GetExport / GetDelegate cost
    // on every regulation tick.
    private AbiVersionDelegate? _abiVersion;
    private ComputeDelegate?    _compute;
    private PidStepDelegate?    _pidStep;
    private FilterTelemetryDelegate? _filterTelemetry;

    public bool FileExists(string path) => File.Exists(path);

    public nint Load(string path) => NativeLibrary.Load(path);

    public uint CallAbiVersion(nint handle)
    {
        var del = _abiVersion ??= Marshal
            .GetDelegateForFunctionPointer<AbiVersionDelegate>(
                NativeLibrary.GetExport(handle, AbiVersionExport));
        return del();
    }

    public int CallCompute(
        nint handle,
        in BccSnapshot snapshot,
        in BccLimits limits,
        in BccRequest request,
        out BccCommand command)
    {
        var del = _compute ??= Marshal
            .GetDelegateForFunctionPointer<ComputeDelegate>(
                NativeLibrary.GetExport(handle, ComputeExport));
        return del(in snapshot, in limits, in request, out command);
    }

    public int CallPidStep(
        nint handle,
        in BccPidState state,
        in BccPidOptions options,
        in BccPidInput input,
        out BccPidCommand command)
    {
        var del = _pidStep ??= Marshal
            .GetDelegateForFunctionPointer<PidStepDelegate>(
                NativeLibrary.GetExport(handle, PidStepExport));
        return del(in state, in options, in input, out command);
    }

    public int CallFilterTelemetry(
        nint handle,
        in BccTelemetryFilterState state,
        in BccTelemetryFilterOptions options,
        in BccTelemetryFilterInput input,
        out BccTelemetryFilterOutput output)
    {
        var del = _filterTelemetry ??= Marshal
            .GetDelegateForFunctionPointer<FilterTelemetryDelegate>(
                NativeLibrary.GetExport(handle, FilterTelemetryExport));
        return del(in state, in options, in input, out output);
    }

    public void Free(nint handle) => NativeLibrary.Free(handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint AbiVersionDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ComputeDelegate(
        in BccSnapshot snapshot,
        in BccLimits limits,
        in BccRequest request,
        out BccCommand command);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PidStepDelegate(
        in BccPidState state,
        in BccPidOptions options,
        in BccPidInput input,
        out BccPidCommand command);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int FilterTelemetryDelegate(
        in BccTelemetryFilterState state,
        in BccTelemetryFilterOptions options,
        in BccTelemetryFilterInput input,
        out BccTelemetryFilterOutput output);
}
