using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BatteryEms.Application.Assets;
using BatteryEms.Application.Optimization;
using BatteryEms.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace BatteryEms.Api.Tests;

public sealed class PriceSeriesEndpointTests : IClassFixture<BatteryEmsApiFactory>
{
    private static readonly DateTimeOffset HorizonStart =
        new(2026, 5, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly double[] OneImportedPrice = [42.5];

    private readonly BatteryEmsApiFactory _factory;

    public PriceSeriesEndpointTests(BatteryEmsApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Import_requires_operator_role()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            BatteryEmsApiFactory.ViewerToken);

        var response = await client.PostAsJsonAsync(
            "/markets/price-series/import",
            ValidImportBody(),
            TestJson.Options);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Imported_price_series_can_feed_day_ahead_optimization()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IScheduleOptimizer>();
                services.AddSingleton<CapturingScheduleOptimizer>();
                services.AddSingleton<IScheduleOptimizer>(
                    sp => sp.GetRequiredService<CapturingScheduleOptimizer>());
            });
        });
        using var client = AuthenticatedClient(factory);
        SeedAsset(factory, "asset-price-1");

        var import = await client.PostAsJsonAsync(
            "/markets/price-series/import",
            ValidImportBody(),
            TestJson.Options);
        import.EnsureSuccessStatusCode();
        var importDto = await import.Content.ReadFromJsonAsync<PriceSeriesImportDto>(TestJson.Options);
        Assert.NotNull(importDto);
        Assert.Equal(1, importDto!.Count);
        Assert.Equal("synthetic-test", importDto.Source);

        var optimize = await client.PostAsJsonAsync(
            "/markets/day-ahead/optimize",
            new
            {
                asset_id = "asset-price-1",
                schedule_type = "day_ahead",
                horizon_start = HorizonStart,
                horizon_end = HorizonStart + TimeSpan.FromHours(1),
                time_step_seconds = 3600,
                price_series = new
                {
                    market_bid_area = "DE-LU",
                    product = "day_ahead",
                    price_kind = "energy",
                    source = "synthetic-test",
                },
            },
            TestJson.Options);

        optimize.EnsureSuccessStatusCode();
        var optimizer = factory.Services.GetRequiredService<CapturingScheduleOptimizer>();
        Assert.NotNull(optimizer.LastRequest);
        Assert.Equal("EUR/MWh", optimizer.LastRequest!.PriceUnit);
        Assert.Equal(42.5, optimizer.LastRequest.PricesPerStep!.Single());
    }

    [Fact]
    public async Task Optimization_returns_404_for_unknown_price_series_reference()
    {
        using var client = AuthenticatedClient(_factory);
        SeedAsset(_factory, "asset-price-2");

        var response = await client.PostAsJsonAsync(
            "/markets/day-ahead/optimize",
            new
            {
                asset_id = "asset-price-2",
                schedule_type = "day_ahead",
                horizon_start = HorizonStart,
                horizon_end = HorizonStart + TimeSpan.FromHours(1),
                time_step_seconds = 3600,
                price_series = new
                {
                    market_bid_area = "DE-LU",
                    product = "day_ahead",
                    price_kind = "energy",
                    source = "missing-source",
                },
            },
            TestJson.Options);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Import_returns_400_for_time_step_outside_timespan_range()
    {
        using var client = AuthenticatedClient(_factory);

        var response = await client.PostAsJsonAsync(
            "/markets/price-series/import",
            ValidImportBody(timeStepSeconds: double.MaxValue),
            TestJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Day_ahead_returns_400_for_time_step_outside_timespan_range()
    {
        using var client = AuthenticatedClient(_factory);

        var response = await client.PostAsJsonAsync(
            "/markets/day-ahead/optimize",
            new
            {
                asset_id = "asset-price-overflow",
                schedule_type = "day_ahead",
                horizon_start = HorizonStart,
                horizon_end = HorizonStart + TimeSpan.FromHours(1),
                time_step_seconds = double.MaxValue,
            },
            TestJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Intraday_returns_400_for_time_step_outside_timespan_range()
    {
        using var client = AuthenticatedClient(_factory);

        var response = await client.PostAsJsonAsync(
            "/markets/intraday/reoptimize",
            new
            {
                asset_id = "asset-price-overflow",
                residual_start = HorizonStart,
                horizon_end = HorizonStart + TimeSpan.FromHours(1),
                time_step_seconds = double.MaxValue,
            },
            TestJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static HttpClient AuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            BatteryEmsApiFactory.OperatorToken);
        return client;
    }

    private static void SeedAsset(WebApplicationFactory<Program> factory, string assetId)
    {
        var assets = (InMemoryBatteryAssetRegistry)factory.Services.GetRequiredService<IBatteryAssetRegistry>();
        if (assets.Find(assetId) is not null)
        {
            return;
        }

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

    private static object ValidImportBody(double timeStepSeconds = 3600) => new
    {
        market_bid_area = "DE-LU",
        product = "day_ahead",
        price_kind = "energy",
        unit = "EUR/MWh",
        source = "synthetic-test",
        horizon_start = HorizonStart,
        horizon_end = HorizonStart + TimeSpan.FromHours(1),
        time_step_seconds = timeStepSeconds,
        values = OneImportedPrice,
    };

    private sealed record PriceSeriesImportDto(
        string Source,
        int Count);

    private sealed class CapturingScheduleOptimizer : IScheduleOptimizer
    {
        public ScheduleOptimizationRequest? LastRequest { get; private set; }

        public Task<ScheduleOptimizationResult> OptimizeAsync(
            ScheduleOptimizationRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            LastRequest = request;
            var run = new OptimizationRun(
                runId: Guid.NewGuid(),
                assetId: request.AssetId,
                solverName: "capturing-schedule-optimizer",
                status: OptimizationSolverStatus.Failed,
                horizonStart: request.HorizonStart,
                horizonEnd: request.HorizonEnd,
                timeStep: request.TimeStep,
                objectiveValue: 0,
                objectiveBreakdown: OptimizationObjectiveBreakdown.Empty,
                constraintViolations: Array.Empty<string>(),
                warnings: Array.Empty<string>(),
                solverRuntime: TimeSpan.Zero,
                terminationCode: "captured",
                terminationDetail: null,
                createdAt: DateTimeOffset.UtcNow,
                inputs: request.Inputs,
                producedSchedule: null);
            return Task.FromResult(new ScheduleOptimizationResult(run, producedSchedule: null));
        }
    }
}
