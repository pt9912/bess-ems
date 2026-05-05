using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Domain.Tests;

public sealed class BatteryCommandTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SafeStop_produces_zero_power_stop_command()
    {
        var cmd = BatteryCommand.SafeStop(
            assetId: "asset-1",
            now: Now,
            validity: TimeSpan.FromSeconds(10),
            reason: "stale-snapshot",
            source: CommandSource.Safety);

        Assert.Equal("asset-1", cmd.AssetId);
        Assert.Equal(CommandMode.Stop, cmd.Mode);
        Assert.Equal(0, cmd.ActivePowerKw);
        Assert.Equal(0, cmd.ReactivePowerKvar);
        Assert.Equal(Now + TimeSpan.FromSeconds(10), cmd.ValidUntil);
        Assert.Equal("stale-snapshot", cmd.Reason);
        Assert.Equal(CommandSource.Safety, cmd.Source);
    }

    [Fact]
    public void IsExpired_is_true_after_valid_until()
    {
        var cmd = BatteryCommand.SafeStop("asset-1", Now, TimeSpan.FromSeconds(5), "reason", CommandSource.Safety);

        Assert.False(cmd.IsExpired(Now + TimeSpan.FromSeconds(4)));
        Assert.False(cmd.IsExpired(Now + TimeSpan.FromSeconds(5)));
        Assert.True(cmd.IsExpired(Now + TimeSpan.FromSeconds(6)));
    }
}
