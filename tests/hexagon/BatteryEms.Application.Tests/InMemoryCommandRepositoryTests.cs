using BatteryEms.Application.IO;
using BatteryEms.Application.Persistence;
using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Application.Tests;

public sealed class InMemoryCommandRepositoryTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Append_then_FindByCommandId_returns_the_stored_command()
    {
        var repo = new InMemoryCommandRepository();
        var command = Sample("cmd-1", Now);

        await repo.AppendAsync(command, CommandDispatchResult.Ok(Now, "ok"), CancellationToken.None);

        Assert.Equal(command, await repo.FindByCommandIdAsync("cmd-1", CancellationToken.None));
    }

    [Fact]
    public async Task FindLatest_returns_the_command_with_the_largest_timestamp_per_asset()
    {
        var repo = new InMemoryCommandRepository();
        var earlier = Sample("early", Now);
        var later = Sample("late", Now + TimeSpan.FromSeconds(1));

        // Append in mixed order so the test exercises the
        // "if existing.Timestamp >= new.Timestamp keep existing"
        // branch in both directions.
        await repo.AppendAsync(later, CommandDispatchResult.Ok(later.Timestamp, "ok"), CancellationToken.None);
        await repo.AppendAsync(earlier, CommandDispatchResult.Ok(earlier.Timestamp, "ok"), CancellationToken.None);

        var latest = await repo.FindLatestAsync("asset-1", CancellationToken.None);
        Assert.NotNull(latest);
        Assert.Equal("late", latest!.CommandId);
    }

    [Fact]
    public async Task Returns_null_for_unknown_ids()
    {
        var repo = new InMemoryCommandRepository();
        Assert.Null(await repo.FindByCommandIdAsync("never-seen", CancellationToken.None));
        Assert.Null(await repo.FindLatestAsync("never-seen-asset", CancellationToken.None));
    }

    private static BatteryCommand Sample(string id, DateTimeOffset timestamp) => new(
        CommandId: id,
        Timestamp: timestamp,
        AssetId: "asset-1",
        Mode: CommandMode.Idle,
        ActivePowerKw: 0,
        ReactivePowerKvar: 0,
        ValidUntil: timestamp + TimeSpan.FromMinutes(1),
        Reason: "test",
        Source: CommandSource.Optimization);
}
