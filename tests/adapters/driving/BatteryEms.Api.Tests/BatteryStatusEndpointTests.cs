using System.Net;
using System.Net.Http.Json;
using BatteryEms.Application.Assets;
using BatteryEms.Application.Persistence;
using BatteryEms.Application.Realtime;
using BatteryEms.Domain;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BatteryEms.Api.Tests;

public sealed class BatteryStatusEndpointTests : IClassFixture<BatteryEmsApiFactory>
{
    private readonly BatteryEmsApiFactory _factory;

    public BatteryStatusEndpointTests(BatteryEmsApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Status_returns_404_when_asset_is_not_registered()
    {
        // Fresh factory instance per test would be cleanest but we stay
        // with one IClassFixture and just assert against an asset id the
        // registry has never seen.
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/battery/ghost-asset/status");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Status_returns_telemetry_and_last_command_for_known_asset()
    {
        // Seed the in-memory stores via the running DI container so the
        // test exercises the same singletons the endpoint reads from.
        using var scope = _factory.Services.CreateScope();
        var assets = scope.ServiceProvider.GetRequiredService<IBatteryAssetRegistry>() as InMemoryBatteryAssetRegistry;
        Assert.NotNull(assets);
        var asset = SampleAsset();
        assets!.Register(asset);

        var snapshots = scope.ServiceProvider.GetRequiredService<ISnapshotStore>();
        var telemetry = SampleTelemetry();
        snapshots.Update(telemetry, telemetry.Timestamp);

        var commands = scope.ServiceProvider.GetRequiredService<ICommandRepository>();
        var command = SampleCommand();
        await commands.AppendAsync(
            command,
            BatteryEms.Application.IO.CommandDispatchResult.Ok(command.Timestamp, "ok"),
            CancellationToken.None);

        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/battery/{asset.AssetId}/status");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<BatteryStatusDto>(TestJson.Options);

        Assert.NotNull(body);
        Assert.Equal(asset.AssetId, body!.AssetId);
        Assert.NotNull(body.Telemetry);
        Assert.Equal(telemetry.SocPercent, body.Telemetry!.SocPercent);
        Assert.Equal("ok", body.Telemetry.FaultStatus);
        Assert.NotNull(body.LastCommand);
        Assert.Equal(command.CommandId, body.LastCommand!.CommandId);
        Assert.Equal("Discharge", body.LastCommand.Mode);
    }

    [Fact]
    public async Task Current_command_endpoint_returns_only_the_command_view()
    {
        using var scope = _factory.Services.CreateScope();
        var assets = scope.ServiceProvider.GetRequiredService<IBatteryAssetRegistry>() as InMemoryBatteryAssetRegistry;
        var asset = SampleAsset("asset-cmd");
        assets!.Register(asset);

        var commands = scope.ServiceProvider.GetRequiredService<ICommandRepository>();
        var command = SampleCommand("cmd-current") with { AssetId = asset.AssetId };
        await commands.AppendAsync(
            command,
            BatteryEms.Application.IO.CommandDispatchResult.Ok(command.Timestamp, "ok"),
            CancellationToken.None);

        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/battery/{asset.AssetId}/command/current");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CurrentCommandDto>(TestJson.Options);

        Assert.NotNull(body);
        Assert.Equal(asset.AssetId, body!.AssetId);
        Assert.NotNull(body.Command);
        Assert.Equal(command.CommandId, body.Command!.CommandId);
    }

    private static BatteryAsset SampleAsset(string id = "asset-status") => new(
        assetId: id,
        capacityKwh: 100,
        maxChargePowerKw: 50,
        maxDischargePowerKw: 50,
        minSocPercent: 10,
        maxSocPercent: 90,
        chargeEfficiency: 0.95,
        dischargeEfficiency: 0.95,
        maxRampKwPerSecond: 25,
        minOperatingTemperatureCelsius: -20,
        maxOperatingTemperatureCelsius: 55);

    private static BatteryTelemetry SampleTelemetry(string assetId = "asset-status") => new(
        Timestamp: new DateTimeOffset(2026, 5, 6, 12, 0, 0, TimeSpan.Zero),
        AssetId: assetId,
        SocPercent: 60.5,
        SohPercent: 99,
        ActivePowerKw: 0,
        ReactivePowerKvar: 0,
        DcVoltage: 800,
        DcCurrent: 0,
        TemperatureCelsius: 22,
        Available: true,
        FaultStatus: "ok",
        DataQuality: DataQuality.Valid);

    private static BatteryCommand SampleCommand(string id = "cmd-status") => new(
        CommandId: id,
        Timestamp: new DateTimeOffset(2026, 5, 6, 12, 0, 1, TimeSpan.Zero),
        AssetId: "asset-status",
        Mode: CommandMode.Discharge,
        ActivePowerKw: 25,
        ReactivePowerKvar: 0,
        ValidUntil: new DateTimeOffset(2026, 5, 6, 12, 0, 6, TimeSpan.Zero),
        Reason: "schedule",
        Source: CommandSource.Optimization);

    private sealed record BatteryStatusDto(
        string AssetId,
        TelemetryDto? Telemetry,
        DataQualityDto? Quality,
        DateTimeOffset? ObservedAt,
        CommandDto? LastCommand);

    private sealed record TelemetryDto(
        DateTimeOffset Timestamp,
        double SocPercent,
        double SohPercent,
        double ActivePowerKw,
        double ReactivePowerKvar,
        double DcVoltage,
        double DcCurrent,
        double TemperatureCelsius,
        bool Available,
        string FaultStatus);

    private sealed record DataQualityDto(string Flag, string Reason);

    private sealed record CommandDto(
        string CommandId,
        DateTimeOffset Timestamp,
        string AssetId,
        string Mode,
        double ActivePowerKw,
        double? ReactivePowerKvar,
        DateTimeOffset ValidUntil,
        string Reason,
        string Source);

    private sealed record CurrentCommandDto(string AssetId, CommandDto? Command);
}
