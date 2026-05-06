using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BatteryEms.Application.Assets;
using BatteryEms.Application.Persistence;
using BatteryEms.Domain;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BatteryEms.Api.Tests;

public sealed class DayAheadOptimizeEndpointTests : IClassFixture<BatteryEmsApiFactory>
{
    private static readonly DateTimeOffset HorizonStart =
        new(2026, 5, 7, 0, 0, 0, TimeSpan.Zero);

    private readonly BatteryEmsApiFactory _factory;

    public DayAheadOptimizeEndpointTests(BatteryEmsApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Returns_401_without_token()
    {
        using var client = _factory.CreateClient();
        var body = ValidBody();
        var response = await client.PostAsJsonAsync("/markets/day-ahead/optimize", body, TestJson.Options);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_403_with_viewer_token()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", BatteryEmsApiFactory.ViewerToken);
        var body = ValidBody();
        var response = await client.PostAsJsonAsync("/markets/day-ahead/optimize", body, TestJson.Options);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_400_when_required_field_is_missing()
    {
        using var client = AuthenticatedClient();
        var body = new
        {
            asset_id = "",
            schedule_type = "day_ahead",
            horizon_start = HorizonStart,
            horizon_end = HorizonStart + TimeSpan.FromHours(1),
            time_step_seconds = 3600,
        };
        var response = await client.PostAsJsonAsync("/markets/day-ahead/optimize", body, TestJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Returns_404_when_asset_is_not_registered()
    {
        using var client = AuthenticatedClient();
        var body = ValidBody(assetId: "no-such-asset");
        var response = await client.PostAsJsonAsync("/markets/day-ahead/optimize", body, TestJson.Options);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Returns_400_for_unknown_schedule_type()
    {
        using var client = AuthenticatedClient();
        SeedAsset();
        var body = new
        {
            asset_id = "asset-opt-1",
            schedule_type = "fortnight_ahead",
            horizon_start = HorizonStart,
            horizon_end = HorizonStart + TimeSpan.FromHours(1),
            time_step_seconds = 3600,
        };
        var response = await client.PostAsJsonAsync("/markets/day-ahead/optimize", body, TestJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Returns_200_and_persisted_failed_run_with_noop_solver()
    {
        using var client = AuthenticatedClient();
        SeedAsset();
        var body = ValidBody(assetId: "asset-opt-1");

        var response = await client.PostAsJsonAsync("/markets/day-ahead/optimize", body, TestJson.Options);

        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<OptimizationDto>(TestJson.Options);
        Assert.NotNull(dto);
        Assert.NotEqual(Guid.Empty, dto!.RunId);
        Assert.Equal("failed", dto.Status);  // snake_case enum converter, NoOp stub
        Assert.Null(dto.ProducedScheduleVersion);
        Assert.Equal("no-solver-configured", dto.TerminationReason);

        var runs = _factory.Services.GetRequiredService<IOptimizationRunRepository>();
        var stored = await runs.FindByIdAsync(dto.RunId, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(OptimizationSolverStatus.Failed, stored!.Status);
    }

    private HttpClient AuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", BatteryEmsApiFactory.OperatorToken);
        return client;
    }

    private void SeedAsset()
    {
        var assets = (InMemoryBatteryAssetRegistry)_factory.Services.GetRequiredService<IBatteryAssetRegistry>();
        if (assets.Find("asset-opt-1") is null)
        {
            assets.Register(new BatteryAsset(
                assetId: "asset-opt-1",
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

    private static object ValidBody(string assetId = "asset-opt-1") => new
    {
        asset_id = assetId,
        schedule_type = "day_ahead",
        horizon_start = HorizonStart,
        horizon_end = HorizonStart + TimeSpan.FromHours(1),
        time_step_seconds = 3600,
    };

    private sealed record OptimizationDto(
        Guid RunId,
        string Status,
        int? ProducedScheduleVersion,
        string TerminationReason);
}
