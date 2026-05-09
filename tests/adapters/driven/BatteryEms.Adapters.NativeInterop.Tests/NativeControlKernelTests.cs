using BatteryEms.Adapters.NativeInterop;
using Xunit;

namespace BatteryEms.Adapters.NativeInterop.Tests;

// RM-M3-04 unit tests for the kernel facade. The fake gateway
// substitutes the OS-level compute call so the tests run without
// a real .so on disk; the calling-convention and struct-layout
// invariants are pinned by StructLayoutTests separately.
public sealed class NativeControlKernelTests
{
    [Fact]
    public void Constructor_rejects_zero_handle()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new NativeControlKernel((nint)0, new FakeGateway()));
        Assert.Contains("non-zero library handle", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Compute_forwards_call_and_returns_gateway_result()
    {
        var gateway = new FakeGateway
        {
            ComputeReturn = BccStatus.Ok,
            ComputeCommandToReturn = new BccCommand
            {
                ActivePowerKw = 25.0,
                Mode = BccMode.Discharge,
                Status = BccStatus.Ok,
                ReasonCode = BccReason.WithinLimits,
            },
        };
        using var kernel = new NativeControlKernel((nint)0x1234, gateway);

        var snapshot = new BccSnapshot { SocPercent = 50, ActivePowerKw = 0, TemperatureCelsius = 22 };
        var limits = new BccLimits
        {
            MaxChargePowerKw = 50, MaxDischargePowerKw = 50,
            MinSocPercent = 10, MaxSocPercent = 90,
            MaxRampKwPerSecond = 25,
            MinTemperatureCelsius = -20, MaxTemperatureCelsius = 55,
        };
        var request = new BccRequest
        {
            TargetActivePowerKw = 25, PreviousActivePowerKw = 0,
            DtSeconds = 1, HasPrevious = 0,
        };

        var status = kernel.Compute(in snapshot, in limits, in request, out var command);

        Assert.Equal(BccStatus.Ok, status);
        Assert.Equal(25.0, command.ActivePowerKw);
        Assert.Equal(BccMode.Discharge, command.Mode);
        Assert.Equal(BccReason.WithinLimits, command.ReasonCode);
        Assert.Equal((nint)0x1234, gateway.LastComputeHandle);
    }

    [Fact]
    public void Dispose_frees_handle_via_gateway()
    {
        var gateway = new FakeGateway();
        var kernel = new NativeControlKernel((nint)0x1234, gateway);
        kernel.Dispose();
        Assert.Equal(1, gateway.FreeCalls);
        Assert.Equal((nint)0x1234, gateway.LastFreeHandle);
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        // ObjectDisposedException on subsequent Compute is the
        // contract; calling Dispose twice must not double-free.
        var gateway = new FakeGateway();
        var kernel = new NativeControlKernel((nint)0x1234, gateway);
        kernel.Dispose();
        kernel.Dispose();
        Assert.Equal(1, gateway.FreeCalls);
    }

    [Fact]
    public void Compute_after_dispose_throws()
    {
        var gateway = new FakeGateway();
        var kernel = new NativeControlKernel((nint)0x1234, gateway);
        kernel.Dispose();

        var snapshot = default(BccSnapshot);
        var limits = default(BccLimits);
        var request = default(BccRequest);
        Assert.Throws<ObjectDisposedException>(() =>
            kernel.Compute(in snapshot, in limits, in request, out _));
    }

    private sealed class FakeGateway : INativeLibraryGateway
    {
        public int ComputeReturn { get; set; }
        public BccCommand ComputeCommandToReturn { get; set; }
        public nint LastComputeHandle { get; private set; }
        public int PidStepReturn { get; set; }
        public BccPidCommand PidStepCommandToReturn { get; set; }
        public nint LastPidStepHandle { get; private set; }
        public int FreeCalls { get; private set; }
        public nint LastFreeHandle { get; private set; }

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
            LastComputeHandle = handle;
            command = ComputeCommandToReturn;
            return ComputeReturn;
        }

        public int CallPidStep(
            nint handle,
            in BccPidState state,
            in BccPidOptions options,
            in BccPidInput input,
            out BccPidCommand command)
        {
            LastPidStepHandle = handle;
            command = PidStepCommandToReturn;
            return PidStepReturn;
        }

        public void Free(nint handle)
        {
            FreeCalls++;
            LastFreeHandle = handle;
        }
    }
}
