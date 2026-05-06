using System.Net.Http.Json;
using Xunit;

namespace BatteryEms.Api.Tests;

public sealed class HealthEndpointTests : IClassFixture<BatteryEmsApiFactory>
{
    private readonly BatteryEmsApiFactory _factory;

    public HealthEndpointTests(BatteryEmsApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_returns_ok_status_and_timestamp()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<HealthDto>(TestJson.Options);
        Assert.NotNull(body);
        Assert.Equal("ok", body!.Status);
        Assert.True(body.At > DateTimeOffset.MinValue);
    }

    private sealed record HealthDto(string Status, DateTimeOffset At);
}
