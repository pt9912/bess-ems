using System.Runtime.InteropServices;

namespace BatteryEms.Adapters.NativeInterop;

// RM-M3-04 P/Invoke surface for the native control core.
//
// Layout rules (verified by the layout-pin tests in the test
// project): every struct uses Sequential layout with no explicit
// pack so the .NET runtime picks the same natural alignment as
// the C compiler does on Linux x86_64 (the M3 deployment target).
// All boolean-ish fields are int32_t per the ABI header — `bool`
// would have caused a 1-byte field with surprising padding.
//
// Numeric status / reason / mode values are kept as int (int32_t)
// rather than C# enums to keep the marshalled layout exactly the
// same as the C struct; the .NET-side helpers in
// BccStatusCodes / BccReasonCodes / BccMode classify the values.
//
// Sign convention follows architecture §4.1: discharge is positive,
// charge is negative. Power values are kW; ramp limit is kW/s.

// CA1815 (override Equals + ==) does not pay its weight on P/Invoke
// marshaling structs: callers either compare individual fields or
// pass the whole struct to the native side. Adding generated
// equality only enlarges the public surface and adds methods that
// are never called.
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance", "CA1815",
    Justification = "P/Invoke marshaling struct; value-equality is not part of the contract.")]
[StructLayout(LayoutKind.Sequential)]
public struct BccSnapshot
{
    public double SocPercent;          // 0
    public double ActivePowerKw;       // 8
    public double TemperatureCelsius;  // 16
}

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance", "CA1815",
    Justification = "P/Invoke marshaling struct; value-equality is not part of the contract.")]
[StructLayout(LayoutKind.Sequential)]
public struct BccLimits
{
    public double MaxChargePowerKw;       // 0
    public double MaxDischargePowerKw;    // 8
    public double MinSocPercent;          // 16
    public double MaxSocPercent;          // 24
    public double MaxRampKwPerSecond;     // 32
    public double MinTemperatureCelsius;  // 40
    public double MaxTemperatureCelsius;  // 48
}

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance", "CA1815",
    Justification = "P/Invoke marshaling struct; value-equality is not part of the contract.")]
[StructLayout(LayoutKind.Sequential)]
public struct BccRequest
{
    public double TargetActivePowerKw;    // 0
    public double PreviousActivePowerKw;  // 8
    public double DtSeconds;              // 16
    public int    HasPrevious;            // 24 (4 bytes + 4 trailing padding → 32 total)
}

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance", "CA1815",
    Justification = "P/Invoke marshaling struct; value-equality is not part of the contract.")]
[StructLayout(LayoutKind.Sequential)]
public struct BccCommand
{
    public double ActivePowerKw;  // 0
    public int    Mode;           // 8
    public int    Status;         // 12
    public int    ReasonCode;     // 16 (4 bytes + 4 trailing padding → 24 total)
}

// Numeric constants that mirror the bcc_status_t / bcc_reason_t /
// bcc_mode_t enums in battery_control_core.h. Renumbering on the
// native side requires a coordinated bump on both the C header
// and these definitions; the unit tests in
// BccConstants_match_native_header_values lock both sides to the
// same values.
public static class BccStatus
{
    public const int Ok                = 0;
    public const int Limited           = 1;
    public const int InvalidInput      = 2;
    public const int NonFinite         = 3;
    public const int NegativeDt        = 4;
    public const int UnsupportedState  = 5;
}

public static class BccReason
{
    public const int WithinLimits              = 0;
    public const int TemperatureOutOfRange     = 1;
    public const int SocAtMaxChargeBlocked     = 2;
    public const int SocAtMinDischargeBlocked  = 3;
    public const int MaxChargePower            = 4;
    public const int MaxDischargePower         = 5;
    public const int RampNotPermitted          = 6;
    public const int RampDownClamped           = 7;
    public const int RampUpClamped             = 8;
    public const int NonFiniteInput            = 9;
    public const int NonFiniteOutput           = 10;
    public const int NegativeDt                = 11;
    public const int UnsupportedState          = 12;
}

public static class BccMode
{
    public const int Stop      = 0;
    public const int Idle      = 1;
    public const int Charge    = 2;
    public const int Discharge = 3;
}

// RM-M3-13 PID slice (ABI minor 0.2). The structs below mirror the
// `bcc_pid_*_t` types in battery_control_core.h one-to-one; layout
// pins live in StructLayoutTests.PidLayout. Anti-windup mode values
// match the bcc_pid_anti_windup_t enum.

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance", "CA1815",
    Justification = "P/Invoke marshaling struct; value-equality is not part of the contract.")]
[StructLayout(LayoutKind.Sequential)]
public struct BccPidState
{
    public double Integral;       // 0
    public double PreviousError;  // 8
}

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance", "CA1815",
    Justification = "P/Invoke marshaling struct; value-equality is not part of the contract.")]
[StructLayout(LayoutKind.Sequential)]
public struct BccPidOptions
{
    public double Kp;                  // 0
    public double Ki;                  // 8
    public double Kd;                  // 16
    public double OutputMin;           // 24
    public double OutputMax;           // 32
    public double DeadbandAbsolute;    // 40
    public int    AntiWindupMode;      // 48 (4 bytes + 4 trailing padding → 56 total)
}

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance", "CA1815",
    Justification = "P/Invoke marshaling struct; value-equality is not part of the contract.")]
[StructLayout(LayoutKind.Sequential)]
public struct BccPidInput
{
    public double Setpoint;     // 0
    public double Measurement;  // 8
    public double DtSeconds;    // 16
}

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance", "CA1815",
    Justification = "P/Invoke marshaling struct; value-equality is not part of the contract.")]
[StructLayout(LayoutKind.Sequential)]
public struct BccPidCommand
{
    public double Output;             // 0
    public double NextIntegral;       // 8
    public double NextPreviousError;  // 16
    public int    Status;             // 24
    public int    ReasonCode;         // 28
    public int    WasClamped;         // 32
    public int    WasIntegralFrozen;  // 36 (40 total)
}

public static class BccPidAntiWindupMode
{
    public const int ConditionalIntegration = 0;
}

// RM-M3-13 reason codes for the PID kernel. Numeric values are
// append-only on top of the M3-A reason set; ABI minor bump 0.1
// → 0.2 on the native side.
public static class BccPidReason
{
    public const int OutputClampedHigh = 13;
    public const int OutputClampedLow  = 14;
    public const int IntegratorOverflow = 15;
    public const int InvalidOptions     = 16;
}
