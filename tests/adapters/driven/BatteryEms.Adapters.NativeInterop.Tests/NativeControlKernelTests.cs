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
    public void FilterTelemetry_forwards_call_and_returns_gateway_result()
    {
        var gateway = new FakeGateway
        {
            FilterTelemetryReturn = BccStatus.Ok,
            FilterTelemetryOutputToReturn = new BccTelemetryFilterOutput
            {
                FilteredSocPercent = 51.0,
                FilteredActivePowerKw = 15.0,
                FilteredTemperatureCelsius = 21.0,
                Status = BccStatus.Ok,
                ReasonCode = BccReason.WithinLimits,
                Initialized = 1,
            },
        };
        using var kernel = new NativeControlKernel((nint)0x1234, gateway);

        var state = new BccTelemetryFilterState
        {
            FilteredSocPercent = 50,
            FilteredActivePowerKw = 10,
            FilteredTemperatureCelsius = 20,
            Initialized = 1,
        };
        var options = new BccTelemetryFilterOptions
        {
            Alpha = 0.25,
            MaxSocDeltaPercent = 20,
            MaxPowerDeltaKw = 50,
            MaxTemperatureDeltaCelsius = 10,
            MinSamplePeriodSeconds = 0.001,
            MaxSamplePeriodSeconds = 1,
        };
        var input = new BccTelemetryFilterInput
        {
            SocPercent = 54,
            ActivePowerKw = 30,
            TemperatureCelsius = 24,
            DtSeconds = 0.01,
        };

        var status = kernel.FilterTelemetry(in state, in options, in input, out var output);

        Assert.Equal(BccStatus.Ok, status);
        Assert.Equal(51.0, output.FilteredSocPercent);
        Assert.Equal(15.0, output.FilteredActivePowerKw);
        Assert.Equal((nint)0x1234, gateway.LastFilterTelemetryHandle);
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
        public int FilterTelemetryReturn { get; set; }
        public BccTelemetryFilterOutput FilterTelemetryOutputToReturn { get; set; }
        public nint LastFilterTelemetryHandle { get; private set; }
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

        public int CallFilterTelemetry(
            nint handle,
            in BccTelemetryFilterState state,
            in BccTelemetryFilterOptions options,
            in BccTelemetryFilterInput input,
            out BccTelemetryFilterOutput output)
        {
            LastFilterTelemetryHandle = handle;
            output = FilterTelemetryOutputToReturn;
            return FilterTelemetryReturn;
        }

        public void Free(nint handle)
        {
            FreeCalls++;
            LastFreeHandle = handle;
        }
    }
}
