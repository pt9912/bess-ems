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
}
