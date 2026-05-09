using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BatteryEms.Application.Assets;
using BatteryEms.Application.Markets;
using BatteryEms.Application.Persistence;
using BatteryEms.Domain;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BatteryEms.Api.Tests;

public sealed class IntradayReoptimizeEndpointTests : IClassFixture<BatteryEmsApiFactory>
{
    private static readonly DateTimeOffset HorizonStart =
        new(2026, 5, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly BatteryEmsApiFactory _factory;

    public IntradayReoptimizeEndpointTests(BatteryEmsApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Returns_401_without_token()
    {
        using var client = _factory.CreateClient();
        var body = ValidBody();
        var response = await client.PostAsJsonAsync("/markets/intraday/reoptimize", body, TestJson.Options);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_403_with_viewer_token()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", BatteryEmsApiFactory.ViewerToken);
        var body = ValidBody();
        var response = await client.PostAsJsonAsync("/markets/intraday/reoptimize", body, TestJson.Options);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_400_when_required_field_is_missing()
    {
        using var client = AuthenticatedClient();
        var body = new
        {
            asset_id = "",
            residual_start = HorizonStart,
            horizon_end = HorizonStart + TimeSpan.FromHours(1),
            time_step_seconds = 3600,
        };
        var response = await client.PostAsJsonAsync("/markets/intraday/reoptimize", body, TestJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Returns_404_when_asset_is_not_registered()
    {
        using var client = AuthenticatedClient();
        var body = ValidBody(assetId: "no-such-asset");
        var response = await client.PostAsJsonAsync("/markets/intraday/reoptimize", body, TestJson.Options);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Returns_200_with_intraday_baseline_missing_when_no_existing_schedule()
    {
        // D-01: missing baseline ⇒ Failed run with TerminationCode
        // "intraday-baseline-missing". The endpoint surfaces it as a
        // 200 (the run was persisted, the operator can read it via
        // /optimization/runs/{id}); status reflects the failure.
        // Per-test asset id so the shared singleton schedule
        // repository can't leak baselines between tests.
        const string assetId = "asset-reopt-baseline";
        using var client = AuthenticatedClient();
        RegisterAsset(assetId);
        var body = ValidBody(assetId: assetId);

        var response = await client.PostAsJsonAsync("/markets/intraday/reoptimize", body, TestJson.Options);

        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<OptimizationDto>(TestJson.Options);
        Assert.NotNull(dto);
        Assert.Equal("failed", dto!.Status);
        Assert.Null(dto.ProducedScheduleVersion);
        Assert.StartsWith("intraday-baseline-missing", dto.TerminationReason, StringComparison.Ordinal);

        var runs = _factory.Services.GetRequiredService<IOptimizationRunRepository>();
        var stored = await runs.FindByIdAsync(dto.RunId, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal("intraday-baseline-missing", stored!.TerminationCode);
        Assert.Equal("intraday-reopt-precheck", stored.SolverName);
    }

    [Fact]
    public async Task Returns_200_with_residual_start_not_aligned_when_residual_misaligned()
    {
        // D-02: residualStart inside an existing window's body ⇒
        // Failed run with TerminationCode "residual-start-not-aligned".
        // Existing schedule is left untouched.
        const string assetId = "asset-reopt-misaligned";
        using var client = AuthenticatedClient();
        RegisterAsset(assetId);
        SeedExistingIntradaySchedule(assetId);

        // residualStart=12:30 falls inside the existing schedule's
        // first window [12:00, 13:00); residual horizon ends 30 min
        // into the second window so the LP step grid stays aligned
        // (30 min + 60 min = 90 min would NOT be integer-multiple).
        // Picking residualStart=12:30 + horizonEnd=13:30 yields a
        // 60-min residual horizon that aligns with the 1-h timeStep
        // — the command passes Command-side validation and the
        // alignment check inside the use case is what surfaces the
        // failure.
        var misalignedResidualStart = HorizonStart + TimeSpan.FromMinutes(30);
        var body = new
        {
            asset_id = assetId,
            residual_start = misalignedResidualStart,
            horizon_end = misalignedResidualStart + TimeSpan.FromHours(1),
            time_step_seconds = 3600,
        };

        var response = await client.PostAsJsonAsync("/markets/intraday/reoptimize", body, TestJson.Options);
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<OptimizationDto>(TestJson.Options);
        Assert.NotNull(dto);
        Assert.Equal("failed", dto!.Status);
        Assert.Null(dto.ProducedScheduleVersion);
        Assert.StartsWith("residual-start-not-aligned", dto.TerminationReason, StringComparison.Ordinal);

        // Existing v3 baseline still intact.
        var schedules = _factory.Services.GetRequiredService<IScheduleRepository>();
        var existing = schedules.FindActive(assetId, ScheduleType.Intraday);
        Assert.Equal(3, existing!.Version);
    }

    [Fact]
    public async Task Returns_200_with_solver_failed_when_no_solver_is_configured()
    {
        // The default DI wiring uses NoOpScheduleOptimizer, which returns
        // Failed/no-solver-configured. The use case still hits the
        // alignment check first (passes because residualStart=HorizonStart
        // matches the seeded schedule's first window), then the optimiser
        // returns Failed with no ProducedSchedule, and the use case
        // persists the run without Replace.
        const string assetId = "asset-reopt-noop";
        using var client = AuthenticatedClient();
        RegisterAsset(assetId);
        SeedExistingIntradaySchedule(assetId);

        var body = ValidBody(assetId: assetId);
        var response = await client.PostAsJsonAsync("/markets/intraday/reoptimize", body, TestJson.Options);

        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<OptimizationDto>(TestJson.Options);
        Assert.NotNull(dto);
        Assert.Equal("failed", dto!.Status);
        Assert.Null(dto.ProducedScheduleVersion);
        Assert.Equal("no-solver-configured", dto.TerminationReason);

        // Existing v3 baseline still intact (no Replace happened).
        var schedules = _factory.Services.GetRequiredService<IScheduleRepository>();
        var existing = schedules.FindActive(assetId, ScheduleType.Intraday);
        Assert.Equal(3, existing!.Version);
    }

    private HttpClient AuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", BatteryEmsApiFactory.OperatorToken);
        return client;
    }

    private void RegisterAsset(string assetId)
    {
        var assets = (InMemoryBatteryAssetRegistry)_factory.Services.GetRequiredService<IBatteryAssetRegistry>();
        if (assets.Find(assetId) is null)
        {
            assets.Register(new BatteryAsset(
                assetId: assetId,
                capacityKwh: 100,
                maxChargePowerKw: 50,
                maxDischargePowerKw: 50,
                minSocPercent: 10,
                maxSocPercent: 90,
                chargeEfficiency: 0.95,
                dischargeEfficiency: 0.95,
                maxRampKwPerSecond: 25,
                minOperatingTemperatureCelsius: -20,
                maxOperatingTemperatureCelsius: 55));
        }
    }

    private void SeedExistingIntradaySchedule(string assetId)
    {
        var schedules = _factory.Services.GetRequiredService<IScheduleRepository>();
        if (schedules.FindActive(assetId, ScheduleType.Intraday) is not null)
        {
            return;
        }
        var schedule = new Schedule(
            assetId: assetId,
            type: ScheduleType.Intraday,
            marketBidArea: "DE-LU",
            version: 3,
            windows: new List<ScheduleWindow>
            {
                new(HorizonStart, HorizonStart + TimeSpan.FromHours(1), 10),
                new(HorizonStart + TimeSpan.FromHours(1), HorizonStart + TimeSpan.FromHours(2), 20),
            });
        schedules.Replace(schedule, expectedBaseVersion: 0);
    }

    private static object ValidBody(string assetId = "asset-reopt-baseline") => new
    {
        asset_id = assetId,
        residual_start = HorizonStart,
        horizon_end = HorizonStart + TimeSpan.FromHours(1),
        time_step_seconds = 3600,
    };

    private sealed record OptimizationDto(
        Guid RunId,
        string Status,
        DateTimeOffset HorizonStart,
        DateTimeOffset HorizonEnd,
        int? ProducedScheduleVersion,
        string TerminationReason);
}
