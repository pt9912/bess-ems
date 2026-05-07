using BatteryEms.Application.Persistence;
using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Application.Tests;

public sealed class InMemoryOptimizationRunRepositoryTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 5, 7, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FindById_returns_null_before_any_append()
    {
        var repo = new InMemoryOptimizationRunRepository();
        Assert.Null(await repo.FindByIdAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Append_then_FindById_returns_the_run()
    {
        var repo = new InMemoryOptimizationRunRepository();
        var run = BuildRun(createdAt: T0);
        await repo.AppendAsync(run, CancellationToken.None);

        var loaded = await repo.FindByIdAsync(run.RunId, CancellationToken.None);
        Assert.Same(run, loaded);
    }

    [Fact]
    public async Task Re_appending_the_same_run_id_throws()
    {
        var repo = new InMemoryOptimizationRunRepository();
        var run = BuildRun(createdAt: T0);
        await repo.AppendAsync(run, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repo.AppendAsync(run, CancellationToken.None));
    }

    [Fact]
    public async Task Query_returns_runs_inside_half_open_window_ordered_by_createdAt()
    {
        var repo = new InMemoryOptimizationRunRepository();
        var older = BuildRun(createdAt: T0 + TimeSpan.FromHours(1));
        var newer = BuildRun(createdAt: T0 + TimeSpan.FromHours(3));
        await repo.AppendAsync(newer, CancellationToken.None);
        await repo.AppendAsync(older, CancellationToken.None);

        var inside = await repo.QueryAsync("asset-1", T0, T0 + TimeSpan.FromHours(4), CancellationToken.None);
        Assert.Equal(new[] { older, newer }, inside);

        var outside = await repo.QueryAsync("asset-1", T0 + TimeSpan.FromHours(4), T0 + TimeSpan.FromHours(5), CancellationToken.None);
        Assert.Empty(outside);
    }

    [Fact]
    public async Task Query_window_is_half_open_until_excluded()
    {
        var repo = new InMemoryOptimizationRunRepository();
        var run = BuildRun(createdAt: T0 + TimeSpan.FromHours(1));
        await repo.AppendAsync(run, CancellationToken.None);

        var miss = await repo.QueryAsync("asset-1", T0, T0 + TimeSpan.FromHours(1), CancellationToken.None);
        Assert.Empty(miss);

        var hit = await repo.QueryAsync("asset-1",
            T0 + TimeSpan.FromHours(1),
            T0 + TimeSpan.FromHours(2),
            CancellationToken.None);
        Assert.Single(hit);
    }

    [Fact]
    public async Task Query_filters_by_asset_id()
    {
        var repo = new InMemoryOptimizationRunRepository();
        await repo.AppendAsync(BuildRun(assetId: "asset-1", createdAt: T0), CancellationToken.None);
        await repo.AppendAsync(BuildRun(assetId: "asset-2", createdAt: T0), CancellationToken.None);

        var asset1 = await repo.QueryAsync("asset-1", T0, T0 + TimeSpan.FromHours(1), CancellationToken.None);
        Assert.Single(asset1);
        Assert.Equal("asset-1", asset1[0].AssetId);
    }

    [Fact]
    public async Task Query_throws_for_blank_asset_id()
    {
        var repo = new InMemoryOptimizationRunRepository();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            repo.QueryAsync("", T0, T0 + TimeSpan.FromHours(1), CancellationToken.None));
    }

    [Fact]
    public async Task Query_throws_when_until_precedes_from()
    {
        var repo = new InMemoryOptimizationRunRepository();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            repo.QueryAsync("asset-1", T0 + TimeSpan.FromHours(1), T0, CancellationToken.None));
    }

    [Fact]
    public async Task Append_throws_for_null_run()
    {
        var repo = new InMemoryOptimizationRunRepository();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            repo.AppendAsync(null!, CancellationToken.None));
    }

    private static OptimizationRun BuildRun(
        string assetId = "asset-1",
        DateTimeOffset? createdAt = null) => new(
            runId: Guid.NewGuid(),
            assetId: assetId,
            solverName: "noop-solver",
            status: OptimizationSolverStatus.Optimal,
            horizonStart: T0,
            horizonEnd: T0 + TimeSpan.FromHours(24),
            timeStep: TimeSpan.FromHours(1),
            objectiveValue: 0,
            objectiveBreakdown: OptimizationObjectiveBreakdown.Empty,
            constraintViolations: Array.Empty<string>(),
            warnings: Array.Empty<string>(),
            solverRuntime: TimeSpan.FromMilliseconds(1),
            terminationCode: "ok",
            terminationDetail: null,
            createdAt: createdAt ?? T0,
            inputs: Array.Empty<ScheduleReference>(),
            producedSchedule: new ScheduleReference(assetId, ScheduleType.DayAhead, 1));
}
