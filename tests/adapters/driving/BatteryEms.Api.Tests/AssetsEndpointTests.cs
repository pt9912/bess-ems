using System.Net.Http.Json;
using BatteryEms.Application.Assets;
using BatteryEms.Domain;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BatteryEms.Api.Tests;

public sealed class AssetsEndpointTests : IClassFixture<BatteryEmsApiFactory>
{
    private readonly BatteryEmsApiFactory _factory;

    public AssetsEndpointTests(BatteryEmsApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Returns_registered_assets_in_stable_order()
    {
        var registry = (InMemoryBatteryAssetRegistry)_factory.Services
            .GetRequiredService<IBatteryAssetRegistry>();
        registry.Register(NewAsset("ui-asset-b", capacityKwh: 80));
        registry.Register(NewAsset("ui-asset-a", capacityKwh: 100));

        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/assets");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AssetsDto>(TestJson.Options);

        Assert.NotNull(body);
        var assets = body!.Assets.Where(a => a.AssetId.StartsWith("ui-asset-", StringComparison.Ordinal)).ToArray();
        Assert.Collection(
            assets,
            a =>
            {
                Assert.Equal("ui-asset-a", a.AssetId);
                Assert.Equal(100, a.CapacityKwh);
            },
            a =>
            {
                Assert.Equal("ui-asset-b", a.AssetId);
                Assert.Equal(80, a.CapacityKwh);
            });
    }

    private static BatteryAsset NewAsset(string assetId, double capacityKwh) => new(
        assetId,
        capacityKwh: capacityKwh,
        maxChargePowerKw: 50,
        maxDischargePowerKw: 50,
        minSocPercent: 10,
        maxSocPercent: 90,
        chargeEfficiency: 0.95,
        dischargeEfficiency: 0.95,
        maxRampKwPerSecond: 25,
        minOperatingTemperatureCelsius: -20,
        maxOperatingTemperatureCelsius: 55);

    private sealed record AssetsDto(IReadOnlyList<AssetDto> Assets);

    private sealed record AssetDto(
        string AssetId,
        double CapacityKwh,
        double MaxChargePowerKw,
        double MaxDischargePowerKw,
        double MinSocPercent,
        double MaxSocPercent);
}
