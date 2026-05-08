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
}
