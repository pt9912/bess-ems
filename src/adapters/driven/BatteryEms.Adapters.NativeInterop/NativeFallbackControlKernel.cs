using BatteryEms.Application.Control;
using Microsoft.Extensions.Logging;

namespace BatteryEms.Adapters.NativeInterop;

// RM-M3-05 IControlKernel implementation that prefers the native
// kernel and falls back to the managed reference on any native
// error from a validated input. The control cycle is responsible
// for validating Snapshot/Limits/Request fields BEFORE constructing
// KernelInput (RM-M3-05 prereq); this kernel therefore trusts the
// inputs are finite and treats every non-OK / non-LIMITED native
// status as a "fall back to managed for the same tick" event.
//
// Mapping rules from the BCC structs to the Application port:
//   * Source = Native              when native returns OK / LIMITED
//   * Source = NativeFallbackToManaged when native returns
//     INVALID_INPUT / NON_FINITE / NEGATIVE_DT / UNSUPPORTED_STATE
//   * Reason on the Native path is the canonical managed reason
//     string for the BCC reason code (constants below); a code
//     not in the table maps to "native-unknown-reason" and is
//     surfaced via a warning log so an ABI bump can be diagnosed.
public sealed partial class NativeFallbackControlKernel : IControlKernel, IDisposable
{
    private readonly NativeControlKernel _native;
    private readonly IControlKernel _managed;
    private readonly ILogger<NativeFallbackControlKernel> _logger;
    private bool _disposed;

    public NativeFallbackControlKernel(
        NativeControlKernel native,
        IControlKernel managed,
        ILogger<NativeFallbackControlKernel> logger)
    {
        ArgumentNullException.ThrowIfNull(native);
        ArgumentNullException.ThrowIfNull(managed);
        ArgumentNullException.ThrowIfNull(logger);
        _native = native;
        _managed = managed;
        _logger = logger;
    }

    public KernelResult Compute(KernelInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var snapshot = new BccSnapshot
        {
            SocPercent          = input.Telemetry.SocPercent,
            ActivePowerKw       = input.Telemetry.ActivePowerKw,
            TemperatureCelsius  = input.Telemetry.TemperatureCelsius,
        };
        var limits = new BccLimits
        {
            MaxChargePowerKw       = input.Asset.MaxChargePowerKw,
            MaxDischargePowerKw    = input.Asset.MaxDischargePowerKw,
            MinSocPercent          = input.Asset.MinSocPercent,
            MaxSocPercent          = input.Asset.MaxSocPercent,
            MaxRampKwPerSecond     = input.Asset.MaxRampKwPerSecond,
            MinTemperatureCelsius  = input.Asset.MinOperatingTemperatureCelsius,
            MaxTemperatureCelsius  = input.Asset.MaxOperatingTemperatureCelsius,
        };
        var request = new BccRequest
        {
            TargetActivePowerKw    = input.DispatchTargetActivePowerKw,
            PreviousActivePowerKw  = input.PreviousActivePowerKw ?? 0.0,
            DtSeconds              = input.TimeSinceLastCommand.TotalSeconds,
            HasPrevious            = input.PreviousActivePowerKw.HasValue ? 1 : 0,
        };

        var status = _native.Compute(in snapshot, in limits, in request, out var command);

        if (status == BccStatus.Ok || status == BccStatus.Limited)
        {
            return new KernelResult(
                ActivePowerKw: command.ActivePowerKw,
                Reason:        MapReason(command.ReasonCode),
                WasLimited:    status == BccStatus.Limited,
                Source:        KernelResultSource.Native);
        }

        // Plan: any other status from a validated input is a native
        // bug or a slice-not-implemented case; fall back to the
        // managed kernel for the same tick so the regulation cycle
        // still produces a command.
        LogNativeFallback(status, command.ReasonCode);
        var fallback = _managed.Compute(input);
        return fallback with { Source = KernelResultSource.NativeFallbackToManaged };
    }

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        _native.Dispose();
    }

    // BCC-reason → managed reason string. Pinned by the kernel-
    // tests and the Domain reason strings — the lookup MUST round-
    // trip through the M1 ConstraintLimiter / RampLimiter reasons
    // so the audit trail is identical to the .NET reference path.
    internal static string MapReason(int reasonCode) => reasonCode switch
    {
        BccReason.WithinLimits              => "within-limits",
        BccReason.TemperatureOutOfRange     => "temperature-out-of-range",
        BccReason.SocAtMaxChargeBlocked     => "soc-at-max-charge-blocked",
        BccReason.SocAtMinDischargeBlocked  => "soc-at-min-discharge-blocked",
        BccReason.MaxChargePower            => "max-charge-power",
        BccReason.MaxDischargePower         => "max-discharge-power",
        BccReason.RampNotPermitted          => "ramp-not-permitted",
        BccReason.RampDownClamped           => "ramp-down-clamped",
        BccReason.RampUpClamped             => "ramp-up-clamped",
        BccReason.NonFiniteInput            => "native-non-finite-input",
        BccReason.NonFiniteOutput           => "native-non-finite-output",
        BccReason.NegativeDt                => "native-negative-dt",
        BccReason.UnsupportedState          => "native-unsupported-state",
        _                                   => "native-unknown-reason",
    };

    [LoggerMessage(EventId = 3500, Level = LogLevel.Warning,
        Message = "Native control kernel returned non-OK status; falling back to managed kernel for this tick. native_status={Status} native_reason_code={ReasonCode}")]
    private partial void LogNativeFallback(int status, int reasonCode);
}
