using System.Runtime.InteropServices;

namespace BatteryEms.Adapters.NativeInterop;

// RM-M3-03 production gateway. Wraps the .NET 10 `NativeLibrary`
// API and the cdecl P/Invoke for battery_control_core_abi_version.
// Internal so the only entry point is via NativeControlLoader; the
// type does not exist for callers that try to bypass it.
internal sealed class SystemNativeLibraryGateway : INativeLibraryGateway
{
    private const string AbiVersionExport = "battery_control_core_abi_version";

    public bool FileExists(string path) => File.Exists(path);

    public nint Load(string path) => NativeLibrary.Load(path);

    public uint CallAbiVersion(nint handle)
    {
        var fn = NativeLibrary.GetExport(handle, AbiVersionExport);
        var del = Marshal.GetDelegateForFunctionPointer<AbiVersionDelegate>(fn);
        return del();
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint AbiVersionDelegate();
}
