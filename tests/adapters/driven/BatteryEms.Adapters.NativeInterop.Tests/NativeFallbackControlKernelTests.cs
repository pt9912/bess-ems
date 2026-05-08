using BatteryEms.Adapters.NativeInterop;
using BatteryEms.Application.Control;
using BatteryEms.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.Adapters.NativeInterop.Tests;

// RM-M3-05 unit tests for the native+fallback IControlKernel
// implementation. The native side is a fake gateway so the suite
// runs without a real .so; the managed fallback is also faked to
// isolate the routing logic from the real Constraint+Ramp
// computation (those have their own coverage in
// ManagedControlKernelTests).
public sealed class NativeFallbackControlKernelTests
{
    [Theory]
    [InlineData(BccStatus.Ok, KernelResultSource.Native)]
    [InlineData(BccStatus.Limited, KernelResultSource.Native)]
    public void Compute_uses_native_result_when_status_is_ok_or_limited(
        int nativeStatus, KernelResultSource expectedSource)
    {
        var gateway = new RecordingGateway
        {
            ComputeReturn = nativeStatus,
            ComputeCommandToReturn = new BccCommand
            {
                ActivePowerKw = 25,
                Mode = BccMode.Discharge,
                Status = nativeStatus,
                ReasonCode = nativeStatus == BccStatus.Limited
                    ? BccReason.MaxDischargePower
                    : BccReason.WithinLimits,
            },
        };
        var native = new NativeControlKernel((nint)0x1234, gateway);
        var managed = new RecordingManagedKernel();
        using var fallback = new NativeFallbackControlKernel(
            native, managed, NullLogger<NativeFallbackControlKernel>.Instance);

        var result = fallback.Compute(InputForDispatch(25));

        Assert.Equal(25, result.ActivePowerKw);
        Assert.Equal(expectedSource, result.Source);
        Assert.Equal(0, managed.CallCount);
        Assert.Equal(1, gateway.ComputeCalls);
    }

    [Theory]
    [InlineData(BccStatus.InvalidInput)]
    [InlineData(BccStatus.NonFinite)]
    [InlineData(BccStatus.NegativeDt)]
    [InlineData(BccStatus.UnsupportedState)]
    public void Compute_falls_back_to_managed_when_native_returns_error_status(int nativeStatus)
    {
        // RM-M3-05 contract: any non-OK / non-LIMITED native status
        // means the kernel could not produce a usable result. The
        // adapter falls back to the managed kernel for the SAME
        // tick instead of skipping the cycle.
        var gateway = new RecordingGateway
        {
            ComputeReturn = nativeStatus,
            ComputeCommandToReturn = new BccCommand
            {
                ActivePowerKw = 0,
                Status = nativeStatus,
                ReasonCode = BccReason.UnsupportedState,
            },
        };
        var native = new NativeControlKernel((nint)0x1234, gateway);
        var managed = new RecordingManagedKernel
        {
            ResultToReturn = new KernelResult(
                ActivePowerKw: 12.5,
                Reason: "within-limits",
                WasLimited: false,
                Source: KernelResultSource.Managed),
        };
        using var fallback = new NativeFallbackControlKernel(
            native, managed, NullLogger<NativeFallbackControlKernel>.Instance);

        var result = fallback.Compute(InputForDispatch(20));

        Assert.Equal(12.5, result.ActivePowerKw);
        Assert.Equal("within-limits", result.Reason);
        Assert.False(result.WasLimited);
        Assert.Equal(KernelResultSource.NativeFallbackToManaged, result.Source);
        Assert.Equal(1, managed.CallCount);
    }

    [Fact]
    public void Compute_marshals_input_into_BCC_structs_correctly()
    {
        var gateway = new RecordingGateway
        {
            ComputeReturn = BccStatus.Ok,
            ComputeCommandToReturn = new BccCommand
            {
                ActivePowerKw = 0, Mode = BccMode.Idle,
                Status = BccStatus.Ok, ReasonCode = BccReason.WithinLimits,
            },
        };
        var native = new NativeControlKernel((nint)0x1234, gateway);
        var managed = new RecordingManagedKernel();
        using var fallback = new NativeFallbackControlKernel(
            native, managed, NullLogger<NativeFallbackControlKernel>.Instance);

        var input = InputForDispatch(15, previousPower: 5, dt: 0.25);
        fallback.Compute(input);

        // Snapshot fields straight from telemetry.
        Assert.Equal(input.Telemetry.SocPercent,         gateway.LastSnapshot.SocPercent);
        Assert.Equal(input.Telemetry.ActivePowerKw,      gateway.LastSnapshot.ActivePowerKw);
        Assert.Equal(input.Telemetry.TemperatureCelsius, gateway.LastSnapshot.TemperatureCelsius);

        // Limits straight from the asset.
        Assert.Equal(input.Asset.MaxChargePowerKw,    gateway.LastLimits.MaxChargePowerKw);
        Assert.Equal(input.Asset.MaxDischargePowerKw, gateway.LastLimits.MaxDischargePowerKw);
        Assert.Equal(input.Asset.MinSocPercent,       gateway.LastLimits.MinSocPercent);
        Assert.Equal(input.Asset.MaxSocPercent,       gateway.LastLimits.MaxSocPercent);
        Assert.Equal(input.Asset.MaxRampKwPerSecond,  gateway.LastLimits.MaxRampKwPerSecond);

        // Request: HasPrevious is 1 because previousPower is set.
        Assert.Equal(15, gateway.LastRequest.TargetActivePowerKw);
        Assert.Equal(5,  gateway.LastRequest.PreviousActivePowerKw);
        Assert.Equal(0.25, gateway.LastRequest.DtSeconds);
        Assert.Equal(1, gateway.LastRequest.HasPrevious);
    }

    [Fact]
    public void Compute_marshals_no_previous_as_HasPrevious_zero()
    {
        var gateway = new RecordingGateway
        {
            ComputeReturn = BccStatus.Ok,
            ComputeCommandToReturn = default,
        };
        var native = new NativeControlKernel((nint)0x1234, gateway);
        var managed = new RecordingManagedKernel();
        using var fallback = new NativeFallbackControlKernel(
            native, managed, NullLogger<NativeFallbackControlKernel>.Instance);

        fallback.Compute(InputForDispatch(15, previousPower: null));

        Assert.Equal(0, gateway.LastRequest.HasPrevious);
    }

    [Theory]
    [InlineData(BccReason.WithinLimits, "within-limits")]
    [InlineData(BccReason.TemperatureOutOfRange, "temperature-out-of-range")]
    [InlineData(BccReason.MaxDischargePower, "max-discharge-power")]
    [InlineData(BccReason.RampUpClamped, "ramp-up-clamped")]
    [InlineData(BccReason.UnsupportedState, "native-unsupported-state")]
    [InlineData(99, "native-unknown-reason")]
    public void MapReason_round_trips_BCC_codes_to_managed_strings(int code, string expected)
    {
        Assert.Equal(expected, NativeFallbackControlKernel.MapReason(code));
    }

    [Fact]
    public void Dispose_disposes_native_kernel_and_blocks_subsequent_calls()
    {
        var gateway = new RecordingGateway();
        var native = new NativeControlKernel((nint)0x1234, gateway);
        var fallback = new NativeFallbackControlKernel(
            native, new RecordingManagedKernel(), NullLogger<NativeFallbackControlKernel>.Instance);

        fallback.Dispose();

        Assert.Equal(1, gateway.FreeCalls);
        Assert.Throws<ObjectDisposedException>(() =>
            fallback.Compute(InputForDispatch(0)));
    }

    private static KernelInput InputForDispatch(
        double dispatch, double? previousPower = 0, double dt = 1.0)
    {
        var asset = new BatteryAsset(
            assetId: "asset-1",
            capacityKwh: 100,
            maxChargePowerKw: 50, maxDischargePowerKw: 50,
            minSocPercent: 10, maxSocPercent: 90,
            chargeEfficiency: 0.95, dischargeEfficiency: 0.95,
            maxRampKwPerSecond: 25,
            minOperatingTemperatureCelsius: -20,
            maxOperatingTemperatureCelsius: 55);
        var telemetry = new BatteryTelemetry(
            Timestamp: DateTimeOffset.UtcNow,
            AssetId: "asset-1",
            SocPercent: 50,
            SohPercent: 100,
            ActivePowerKw: 0,
            ReactivePowerKvar: 0,
            DcVoltage: 800, DcCurrent: 0,
            TemperatureCelsius: 22,
            Available: true,
            FaultStatus: "ok",
            DataQuality: DataQuality.Valid);
        return new KernelInput(asset, telemetry, dispatch, previousPower,
            TimeSpan.FromSeconds(dt));
    }

    private sealed class RecordingGateway : INativeLibraryGateway
    {
        public int ComputeReturn { get; set; }
        public BccCommand ComputeCommandToReturn { get; set; }
        public int ComputeCalls { get; private set; }
        public BccSnapshot LastSnapshot { get; private set; }
        public BccLimits LastLimits { get; private set; }
        public BccRequest LastRequest { get; private set; }
        public int FreeCalls { get; private set; }

        public bool FileExists(string path) => true;
        public nint Load(string path) => (nint)0x1234;
        public uint CallAbiVersion(nint handle) => 0;

        public int CallCompute(
            nint handle,
            in BccSnapshot snapshot,
            in BccLimits limits,
            in BccRequest request,
            out BccCommand command)
        {
            ComputeCalls++;
            LastSnapshot = snapshot;
            LastLimits = limits;
            LastRequest = request;
            command = ComputeCommandToReturn;
            return ComputeReturn;
        }

        public void Free(nint handle) => FreeCalls++;
    }

    private sealed class RecordingManagedKernel : IControlKernel
    {
        public int CallCount { get; private set; }
        public KernelResult ResultToReturn { get; set; } =
            new(0, "within-limits", false, KernelResultSource.Managed);

        public KernelResult Compute(KernelInput input)
        {
            CallCount++;
            return ResultToReturn;
        }
    }
}
