using System.Runtime.InteropServices;
using BatteryEms.Adapters.NativeInterop;
using Xunit;

namespace BatteryEms.Adapters.NativeInterop.Tests;

// RM-M3-04 layout-pin tests. The C-ABI in
// native/battery_control_core/include/battery_control_core.h is
// fixed at the byte level: every field has a documented offset
// and the struct sizes follow x86_64 SysV natural alignment. A
// .NET-side struct that drifts from those offsets would silently
// marshal junk across the P/Invoke boundary.
//
// These assertions are statically computable — Marshal does not
// need a real .so on disk — so they catch the regression at unit-
// test time rather than at first compute call.
public sealed class StructLayoutTests
{
    [Fact]
    public void BccSnapshot_size_and_offsets_match_C_layout()
    {
        Assert.Equal(24, Marshal.SizeOf<BccSnapshot>());
        Assert.Equal(0,  (int)Marshal.OffsetOf<BccSnapshot>(nameof(BccSnapshot.SocPercent)));
        Assert.Equal(8,  (int)Marshal.OffsetOf<BccSnapshot>(nameof(BccSnapshot.ActivePowerKw)));
        Assert.Equal(16, (int)Marshal.OffsetOf<BccSnapshot>(nameof(BccSnapshot.TemperatureCelsius)));
    }

    [Fact]
    public void BccLimits_size_and_offsets_match_C_layout()
    {
        Assert.Equal(56, Marshal.SizeOf<BccLimits>());
        Assert.Equal(0,  (int)Marshal.OffsetOf<BccLimits>(nameof(BccLimits.MaxChargePowerKw)));
        Assert.Equal(8,  (int)Marshal.OffsetOf<BccLimits>(nameof(BccLimits.MaxDischargePowerKw)));
        Assert.Equal(16, (int)Marshal.OffsetOf<BccLimits>(nameof(BccLimits.MinSocPercent)));
        Assert.Equal(24, (int)Marshal.OffsetOf<BccLimits>(nameof(BccLimits.MaxSocPercent)));
        Assert.Equal(32, (int)Marshal.OffsetOf<BccLimits>(nameof(BccLimits.MaxRampKwPerSecond)));
        Assert.Equal(40, (int)Marshal.OffsetOf<BccLimits>(nameof(BccLimits.MinTemperatureCelsius)));
        Assert.Equal(48, (int)Marshal.OffsetOf<BccLimits>(nameof(BccLimits.MaxTemperatureCelsius)));
    }

    [Fact]
    public void BccRequest_size_and_offsets_match_C_layout()
    {
        // 3 doubles + int32 with trailing padding to the struct's
        // 8-byte alignment → 32 bytes total.
        Assert.Equal(32, Marshal.SizeOf<BccRequest>());
        Assert.Equal(0,  (int)Marshal.OffsetOf<BccRequest>(nameof(BccRequest.TargetActivePowerKw)));
        Assert.Equal(8,  (int)Marshal.OffsetOf<BccRequest>(nameof(BccRequest.PreviousActivePowerKw)));
        Assert.Equal(16, (int)Marshal.OffsetOf<BccRequest>(nameof(BccRequest.DtSeconds)));
        Assert.Equal(24, (int)Marshal.OffsetOf<BccRequest>(nameof(BccRequest.HasPrevious)));
    }

    [Fact]
    public void BccCommand_size_and_offsets_match_C_layout()
    {
        // double + 3 int32 = 8 + 12 = 20, padded to 24 (multiple
        // of 8 because the leading double sets struct alignment).
        Assert.Equal(24, Marshal.SizeOf<BccCommand>());
        Assert.Equal(0,  (int)Marshal.OffsetOf<BccCommand>(nameof(BccCommand.ActivePowerKw)));
        Assert.Equal(8,  (int)Marshal.OffsetOf<BccCommand>(nameof(BccCommand.Mode)));
        Assert.Equal(12, (int)Marshal.OffsetOf<BccCommand>(nameof(BccCommand.Status)));
        Assert.Equal(16, (int)Marshal.OffsetOf<BccCommand>(nameof(BccCommand.ReasonCode)));
    }

    [Fact]
    public void BccStatus_constants_match_native_header_values()
    {
        // ABI-stable numeric assignments per
        // battery_control_core.h. Renumbering on either side
        // requires a coordinated ABI-major bump.
        Assert.Equal(0, BccStatus.Ok);
        Assert.Equal(1, BccStatus.Limited);
        Assert.Equal(2, BccStatus.InvalidInput);
        Assert.Equal(3, BccStatus.NonFinite);
        Assert.Equal(4, BccStatus.NegativeDt);
        Assert.Equal(5, BccStatus.UnsupportedState);
    }

    [Fact]
    public void BccReason_constants_match_native_header_values()
    {
        Assert.Equal(0,  BccReason.WithinLimits);
        Assert.Equal(1,  BccReason.TemperatureOutOfRange);
        Assert.Equal(2,  BccReason.SocAtMaxChargeBlocked);
        Assert.Equal(3,  BccReason.SocAtMinDischargeBlocked);
        Assert.Equal(4,  BccReason.MaxChargePower);
        Assert.Equal(5,  BccReason.MaxDischargePower);
        Assert.Equal(6,  BccReason.RampNotPermitted);
        Assert.Equal(7,  BccReason.RampDownClamped);
        Assert.Equal(8,  BccReason.RampUpClamped);
        Assert.Equal(9,  BccReason.NonFiniteInput);
        Assert.Equal(10, BccReason.NonFiniteOutput);
        Assert.Equal(11, BccReason.NegativeDt);
        Assert.Equal(12, BccReason.UnsupportedState);
    }

    [Fact]
    public void BccMode_constants_match_native_header_and_managed_CommandMode()
    {
        // Numeric values mirror the managed CommandMode enum so a
        // round-trip through the kernel preserves identity.
        Assert.Equal(0, BccMode.Stop);
        Assert.Equal(1, BccMode.Idle);
        Assert.Equal(2, BccMode.Charge);
        Assert.Equal(3, BccMode.Discharge);
    }

    // RM-M3-13 PID slice layout pins. Sizes computed for x86_64 SysV
    // natural alignment (the M3 deployment target).

    [Fact]
    public void BccPidState_size_and_offsets_match_C_layout()
    {
        // 2 doubles = 16 bytes, no padding.
        Assert.Equal(16, Marshal.SizeOf<BccPidState>());
        Assert.Equal(0, (int)Marshal.OffsetOf<BccPidState>(nameof(BccPidState.Integral)));
        Assert.Equal(8, (int)Marshal.OffsetOf<BccPidState>(nameof(BccPidState.PreviousError)));
    }

    [Fact]
    public void BccPidOptions_size_and_offsets_match_C_layout()
    {
        // 6 doubles + int32 + 4-byte trailing padding = 56 bytes
        // (struct alignment is 8 from the leading doubles).
        Assert.Equal(56, Marshal.SizeOf<BccPidOptions>());
        Assert.Equal(0,  (int)Marshal.OffsetOf<BccPidOptions>(nameof(BccPidOptions.Kp)));
        Assert.Equal(8,  (int)Marshal.OffsetOf<BccPidOptions>(nameof(BccPidOptions.Ki)));
        Assert.Equal(16, (int)Marshal.OffsetOf<BccPidOptions>(nameof(BccPidOptions.Kd)));
        Assert.Equal(24, (int)Marshal.OffsetOf<BccPidOptions>(nameof(BccPidOptions.OutputMin)));
        Assert.Equal(32, (int)Marshal.OffsetOf<BccPidOptions>(nameof(BccPidOptions.OutputMax)));
        Assert.Equal(40, (int)Marshal.OffsetOf<BccPidOptions>(nameof(BccPidOptions.DeadbandAbsolute)));
        Assert.Equal(48, (int)Marshal.OffsetOf<BccPidOptions>(nameof(BccPidOptions.AntiWindupMode)));
    }

    [Fact]
    public void BccPidInput_size_and_offsets_match_C_layout()
    {
        Assert.Equal(24, Marshal.SizeOf<BccPidInput>());
        Assert.Equal(0,  (int)Marshal.OffsetOf<BccPidInput>(nameof(BccPidInput.Setpoint)));
        Assert.Equal(8,  (int)Marshal.OffsetOf<BccPidInput>(nameof(BccPidInput.Measurement)));
        Assert.Equal(16, (int)Marshal.OffsetOf<BccPidInput>(nameof(BccPidInput.DtSeconds)));
    }

    [Fact]
    public void BccPidCommand_size_and_offsets_match_C_layout()
    {
        // 3 doubles + 4 int32 = 24 + 16 = 40 (no trailing padding —
        // 40 is already a multiple of the 8-byte struct alignment).
        Assert.Equal(40, Marshal.SizeOf<BccPidCommand>());
        Assert.Equal(0,  (int)Marshal.OffsetOf<BccPidCommand>(nameof(BccPidCommand.Output)));
        Assert.Equal(8,  (int)Marshal.OffsetOf<BccPidCommand>(nameof(BccPidCommand.NextIntegral)));
        Assert.Equal(16, (int)Marshal.OffsetOf<BccPidCommand>(nameof(BccPidCommand.NextPreviousError)));
        Assert.Equal(24, (int)Marshal.OffsetOf<BccPidCommand>(nameof(BccPidCommand.Status)));
        Assert.Equal(28, (int)Marshal.OffsetOf<BccPidCommand>(nameof(BccPidCommand.ReasonCode)));
        Assert.Equal(32, (int)Marshal.OffsetOf<BccPidCommand>(nameof(BccPidCommand.WasClamped)));
        Assert.Equal(36, (int)Marshal.OffsetOf<BccPidCommand>(nameof(BccPidCommand.WasIntegralFrozen)));
    }

    [Fact]
    public void BccPidReason_constants_match_native_header_values()
    {
        // Append-only on top of the M3-A reason set (0..12).
        Assert.Equal(13, BccPidReason.OutputClampedHigh);
        Assert.Equal(14, BccPidReason.OutputClampedLow);
        Assert.Equal(15, BccPidReason.IntegratorOverflow);
        Assert.Equal(16, BccPidReason.InvalidOptions);
    }

    [Fact]
    public void BccPidAntiWindupMode_constants_match_native_header_values()
    {
        Assert.Equal(0, BccPidAntiWindupMode.ConditionalIntegration);
    }

    [Fact]
    public void NativeControlLoader_expected_abi_minor_matches_pid_slice()
    {
        // RM-M3-13 bumped the ABI to 0.2 (additive: pid_step plus the
        // four new pid_* structs and reason codes 13..16). The host
        // expectation must be in lockstep so the integration test
        // `Real_library_reports_packed_major_minor_patch_matching_host`
        // pins both sides.
        Assert.Equal(0u, NativeControlLoader.ExpectedAbiMajor);
        Assert.Equal(2u, NativeControlLoader.ExpectedAbiMinor);
        Assert.Equal(0u, NativeControlLoader.ExpectedAbiPatch);
    }
}
