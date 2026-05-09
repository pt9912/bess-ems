using System.Net;
using System.Net.Http.Json;
using BatteryEms.Application.Markets;
using BatteryEms.Domain;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BatteryEms.Api.Tests;

public sealed class SchedulesEndpointTests : IClassFixture<BatteryEmsApiFactory>
{
    private readonly BatteryEmsApiFactory _factory;

    public SchedulesEndpointTests(BatteryEmsApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Returns_400_when_assetId_query_string_is_missing()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/markets/schedules/current");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Returns_active_schedule_for_asset()
    {
        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IScheduleRepository>();

        var start = new DateTimeOffset(2026, 5, 6, 0, 0, 0, TimeSpan.Zero);
        var schedule = new Schedule("asset-sched", ScheduleType.DayAhead, "DE-LU", 1, new List<ScheduleWindow>
        {
            new(start, start + TimeSpan.FromHours(1), 25),
            new(start + TimeSpan.FromHours(1), start + TimeSpan.FromHours(2), -10),
        });
        repo.Replace(schedule, expectedBaseVersion: 0);

        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/markets/schedules/current?assetId=asset-sched");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<SchedulesDto>(TestJson.Options);

        Assert.NotNull(body);
        Assert.Equal("asset-sched", body!.AssetId);
        var single = Assert.Single(body.Schedules);
        Assert.Equal("DayAhead", single.Type);
        Assert.Equal(1, single.Version);
        Assert.Equal(2, single.Windows.Count);
        Assert.Equal(25, single.Windows[0].TargetPowerKw);
    }

    [Fact]
    public async Task Returns_empty_schedule_list_for_unknown_asset()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/markets/schedules/current?assetId=ghost-sched");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<SchedulesDto>(TestJson.Options);

        Assert.NotNull(body);
        Assert.Equal("ghost-sched", body!.AssetId);
        Assert.Empty(body.Schedules);
    }

    private sealed record SchedulesDto(string AssetId, IReadOnlyList<ScheduleDto> Schedules);

    private sealed record ScheduleDto(
        string Type,
        string MarketBidArea,
        int Version,
        DateTimeOffset HorizonStart,
        DateTimeOffset HorizonEnd,
        IReadOnlyList<WindowDto> Windows);

    private sealed record WindowDto(DateTimeOffset Start, DateTimeOffset End, double TargetPowerKw);
}
