using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Domain.Tests;

public sealed class SignConventionTests
{
    private static readonly BatteryAsset Asset = new(
        "asset-1", 100, 50, 50, 10, 90, 0.95, 0.95, 100, -20, 55);

    private static BatteryTelemetry Telemetry(double soc = 50)
        => new(DateTimeOffset.UnixEpoch, "asset-1", soc, 100, 0, 0, 800, 0, 22, true, "ok", DataQuality.Valid);

    [Fact]
    public void Discharge_command_is_positive()
    {
        var cmd = new BatteryCommand(
            CommandId: "c-1",
            Timestamp: DateTimeOffset.UnixEpoch,
            AssetId: "asset-1",
            Mode: CommandMode.Discharge,
            ActivePowerKw: 25,
            ReactivePowerKvar: 0,
            ValidUntil: DateTimeOffset.UnixEpoch + TimeSpan.FromSeconds(10),
            Reason: "schedule",
            Source: CommandSource.Schedule);

        Assert.True(cmd.ActivePowerKw > 0, "Discharge power must be positive per architecture §7.");
    }

    [Fact]
    public void Charge_command_is_negative()
    {
        var cmd = new BatteryCommand(
            CommandId: "c-2",
            Timestamp: DateTimeOffset.UnixEpoch,
            AssetId: "asset-1",
            Mode: CommandMode.Charge,
            ActivePowerKw: -25,
            ReactivePowerKvar: 0,
            ValidUntil: DateTimeOffset.UnixEpoch + TimeSpan.FromSeconds(10),
            Reason: "schedule",
            Source: CommandSource.Schedule);

        Assert.True(cmd.ActivePowerKw < 0, "Charge power must be negative per architecture §7.");
    }

    [Fact]
    public void Constraint_limiter_uses_negative_for_max_charge_clamp()
    {
        var result = ConstraintLimiter.Apply(Asset, Telemetry(), requestedActivePowerKw: -100);

        Assert.True(result.WasLimited);
        Assert.Equal(-50, result.LimitedActivePowerKw);
    }

    [Fact]
    public void Constraint_limiter_uses_positive_for_max_discharge_clamp()
    {
        var result = ConstraintLimiter.Apply(Asset, Telemetry(), requestedActivePowerKw: 100);

        Assert.True(result.WasLimited);
        Assert.Equal(50, result.LimitedActivePowerKw);
    }

    [Fact]
    public void Charging_blocked_at_max_soc_uses_negative_request()
    {
        var result = ConstraintLimiter.Apply(Asset, Telemetry(soc: 90), requestedActivePowerKw: -10);

        Assert.True(result.WasLimited);
        Assert.Equal(0, result.LimitedActivePowerKw);
        Assert.Equal("soc-at-max-charge-blocked", result.LimitReason);
    }

    [Fact]
    public void Discharging_blocked_at_min_soc_uses_positive_request()
    {
        var result = ConstraintLimiter.Apply(Asset, Telemetry(soc: 10), requestedActivePowerKw: 10);

        Assert.True(result.WasLimited);
        Assert.Equal(0, result.LimitedActivePowerKw);
        Assert.Equal("soc-at-min-discharge-blocked", result.LimitReason);
    }

    [Fact]
    public void SafeStop_command_is_zero_power()
    {
        var cmd = BatteryCommand.SafeStop("asset-1", DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(5), "reason", CommandSource.Safety);
        Assert.Equal(0, cmd.ActivePowerKw);
    }
}
