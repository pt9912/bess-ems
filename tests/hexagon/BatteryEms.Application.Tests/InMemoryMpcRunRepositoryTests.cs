using BatteryEms.Application.Mpc;
using BatteryEms.Application.Persistence;
using Xunit;

namespace BatteryEms.Application.Tests;

public sealed class InMemoryMpcRunRepositoryTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 5, 12, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Append_then_find_replays_same_run()
    {
        var repo = new InMemoryMpcRunRepository();
        var run = BuildRun("run-1", createdAt: T0);

        await repo.AppendAsync(run, CancellationToken.None);

        Assert.Same(run, await repo.FindByRequestIdAsync("run-1", CancellationToken.None));
    }

    [Fact]
    public async Task Re_appending_same_request_id_throws()
    {
        var repo = new InMemoryMpcRunRepository();
        var run = BuildRun("run-1", createdAt: T0);
        await repo.AppendAsync(run, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repo.AppendAsync(run, CancellationToken.None));
    }

    [Fact]
    public async Task Query_filters_asset_and_half_open_control_cycle_tick_window()
    {
        var repo = new InMemoryMpcRunRepository();
        var older = BuildRun("run-1", controlCycleTick: T0.AddMinutes(1), createdAt: T0.AddMinutes(20));
        var newer = BuildRun("run-2", controlCycleTick: T0.AddMinutes(2), createdAt: T0);
        var otherAsset = BuildRun("run-3", assetId: "asset-2", controlCycleTick: T0.AddMinutes(1), createdAt: T0);
        await repo.AppendAsync(newer, CancellationToken.None);
        await repo.AppendAsync(otherAsset, CancellationToken.None);
        await repo.AppendAsync(older, CancellationToken.None);

        var rows = await repo.QueryAsync("asset-1", T0, T0.AddMinutes(2), CancellationToken.None);

        Assert.Equal(new[] { older }, rows);
    }

    [Fact]
    public async Task Compact_keeps_top_n_per_asset_and_drops_older_than_max_age()
    {
        var repo = new InMemoryMpcRunRepository();
        await repo.AppendAsync(BuildRun("old-1", createdAt: T0), CancellationToken.None);
        await repo.AppendAsync(BuildRun("old-2", createdAt: T0.AddMinutes(1)), CancellationToken.None);
        await repo.AppendAsync(BuildRun("latest", createdAt: T0.AddMinutes(10)), CancellationToken.None);

        var removed = await repo.CompactAsync(
            new MpcRunRetentionPolicy(keepLatestPerAsset: 1, maxAge: TimeSpan.FromMinutes(5)),
            T0.AddMinutes(11),
            CancellationToken.None);

        Assert.Equal(2, removed);
        Assert.NotNull(await repo.FindByRequestIdAsync("latest", CancellationToken.None));
        Assert.Null(await repo.FindByRequestIdAsync("old-1", CancellationToken.None));
        Assert.Null(await repo.FindByRequestIdAsync("old-2", CancellationToken.None));
    }

    private static MpcRun BuildRun(
        string id,
        string assetId = "asset-1",
        DateTimeOffset? controlCycleTick = null,
        DateTimeOffset? createdAt = null) =>
        new(
            id,
            assetId,
            controlCycleTick ?? T0,
            TimeSpan.FromMilliseconds(250),
            "lti-soc-v1",
            "kalman-v1",
            "solver-hash",
            "estimator-hash",
            42,
            "{\"schema\":\"mpc-numerik-stamp-v1\"}",
            1.0,
            DeterministicMode.Strict,
            isUsable: true,
            terminalReason: "committed",
            trajectoryJson: "{\"points\":[]}",
            terminalStateJson: "{\"mean\":[50.0]}",
            createdAt ?? T0);
}
