namespace BatteryEms.Adapters.NativeInterop;

// RM-M3-03 result states for the startup loader. The set is the
// minimum the plan's M3-Zielbild table demands — every produced
// status is observable in health, logs and metrics so an operator
// can tell apart "deliberately off" from "tried, fell back".
public enum NativeControlStatus
{
    // NativeControl:Enabled is false. The loader did not touch the
    // file system. Health/log surface this as `disabled`.
    Disabled,

    // Library file does not exist at the configured path. Health/
    // log surface this as `library-missing`.
    LibraryMissing,

    // File exists but the OS loader rejected it (corrupt ELF,
    // missing dependencies, wrong architecture, ...) or the export
    // could not be resolved. Health/log surface as `load-failed`.
    LoadFailed,

    // Library loaded and exposes battery_control_core_abi_version,
    // but the major/minor version is incompatible with what this
    // host expects. Per M3 default policy this triggers managed
    // fallback; only when AbortOnAbiMismatch is set the caller
    // turns it into a hard start failure.
    AbiMismatch,

    // Library loaded successfully and the ABI is compatible.
    Loaded,
}
