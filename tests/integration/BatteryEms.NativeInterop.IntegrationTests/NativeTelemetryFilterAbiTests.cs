using System.Runtime.InteropServices;
using BatteryEms.Adapters.NativeInterop;
using Xunit;

namespace BatteryEms.NativeInterop.IntegrationTests;

// RM-M5-03 telemetry-filter integration tests against the real
// libbattery_control_core.so. Native doctests cover the branch
// matrix; this file proves the new export and structs survive the
// P/Invoke boundary.
[Collection("native-library")]
public sealed class NativeTelemetryFilterAbiTests
{
    private static NativeControlKernel LoadKernel()
    {
        var path = NativeLibraryLocator.Locate();
        var handle = NativeLibrary.Load(path);
        return new NativeControlKernel(handle);
    }

    private static BccTelemetryFilterOptions ValidOptions() => new()
    {
        Alpha = 0.25,
        MaxSocDeltaPercent = 20.0,
        MaxPowerDeltaKw = 50.0,
        MaxTemperatureDeltaCelsius = 10.0,
        MinSamplePeriodSeconds = 0.001,
        MaxSamplePeriodSeconds = 1.0,
    };

    private static BccTelemetryFilterState InitializedState() => new()
    {
        FilteredSocPercent = 50.0,
        FilteredActivePowerKw = 10.0,
        FilteredTemperatureCelsius = 20.0,
        Initialized = 1,
    };

    private static BccTelemetryFilterInput ValidInput() => new()
    {
        SocPercent = 54.0,
        ActivePowerKw = 30.0,
        TemperatureCelsius = 24.0,
        DtSeconds = 0.01,
    };

    [Fact]
    public void FilterTelemetry_happy_path_returns_IIR_update()
    {
        using var kernel = LoadKernel();
        var state = InitializedState();
        var options = ValidOptions();
        var input = ValidInput();

        var status = kernel.FilterTelemetry(in state, in options, in input, out var output);

        Assert.Equal(BccStatus.Ok, status);
        Assert.Equal(BccReason.WithinLimits, output.ReasonCode);
        Assert.Equal(51.0, output.FilteredSocPercent, precision: 12);
        Assert.Equal(15.0, output.FilteredActivePowerKw, precision: 12);
        Assert.Equal(21.0, output.FilteredTemperatureCelsius, precision: 12);
        Assert.Equal(1, output.Initialized);
    }

    [Fact]
    public void FilterTelemetry_drift_returns_invalid_input_and_preserves_state()
    {
        using var kernel = LoadKernel();
        var state = InitializedState();
        var options = ValidOptions();
        options.MaxPowerDeltaKw = 5.0;
        var input = ValidInput();

        var status = kernel.FilterTelemetry(in state, in options, in input, out var output);

        Assert.Equal(BccStatus.InvalidInput, status);
        Assert.Equal(BccTelemetryFilterReason.TelemetryDrift, output.ReasonCode);
        Assert.Equal(1, output.DriftDetected);
        Assert.Equal(10.0, output.FilteredActivePowerKw, precision: 12);
        Assert.Equal(1, output.Initialized);
    }

    [Fact]
    public void FilterTelemetry_sample_period_outside_window_returns_invalid_input()
    {
        using var kernel = LoadKernel();
        var state = InitializedState();
        var options = ValidOptions();
        var input = ValidInput();
        input.DtSeconds = 2.0;

        var status = kernel.FilterTelemetry(in state, in options, in input, out var output);

        Assert.Equal(BccStatus.InvalidInput, status);
        Assert.Equal(BccTelemetryFilterReason.SamplePeriod, output.ReasonCode);
    }

    [Fact]
    public void FilterTelemetry_non_finite_input_returns_non_finite()
    {
        using var kernel = LoadKernel();
        var state = InitializedState();
        var options = ValidOptions();
        var input = ValidInput();
        input.SocPercent = double.NaN;

        var status = kernel.FilterTelemetry(in state, in options, in input, out var output);

        Assert.Equal(BccStatus.NonFinite, status);
        Assert.Equal(BccReason.NonFiniteInput, output.ReasonCode);
        Assert.Equal(0, output.Initialized);
    }
}
