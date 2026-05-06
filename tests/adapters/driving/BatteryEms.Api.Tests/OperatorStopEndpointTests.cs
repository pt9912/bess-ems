using System.Net;
using System.Net.Http.Json;
using BatteryEms.Application.Control;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BatteryEms.Api.Tests;

public sealed class OperatorStopEndpointTests : IClassFixture<BatteryEmsApiFactory>
{
    private readonly BatteryEmsApiFactory _factory;

    public OperatorStopEndpointTests(BatteryEmsApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Operator_stop_writes_state_into_the_registry_and_returns_acknowledgment()
    {
        using var client = _factory.CreateClient();

        var body = new { asset_id = "asset-stop-1", @operator = "operator-1", reason = "manual-shutdown" };
        var response = await client.PostAsJsonAsync("/operator/stop", body, TestJson.Options);

        response.EnsureSuccessStatusCode();
        var ack = await response.Content.ReadFromJsonAsync<OperatorStopDto>(TestJson.Options);
        Assert.NotNull(ack);
        Assert.Equal("asset-stop-1", ack!.AssetId);
        Assert.Equal("operator-1", ack.Operator);
        Assert.Equal("manual-shutdown", ack.Reason);
        Assert.True(ack.ActivatedAt > DateTimeOffset.MinValue);

        // The registry singleton the API wires must now report the stop.
        var registry = _factory.Services.GetRequiredService<IOperatorStopRegistry>();
        var state = registry.Find("asset-stop-1");
        Assert.NotNull(state);
        Assert.Equal("operator-1", state!.Operator);
        Assert.Equal("manual-shutdown", state.Reason);
    }

    [Theory]
    [InlineData("", "op", "reason")]
    [InlineData("asset", "", "reason")]
    [InlineData("asset", "op", "")]
    public async Task Operator_stop_returns_400_when_required_field_is_blank(string assetId, string @operator, string reason)
    {
        using var client = _factory.CreateClient();
        var body = new { asset_id = assetId, @operator, reason };

        var response = await client.PostAsJsonAsync("/operator/stop", body, TestJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record OperatorStopDto(string AssetId, string Operator, string Reason, DateTimeOffset ActivatedAt);
}
