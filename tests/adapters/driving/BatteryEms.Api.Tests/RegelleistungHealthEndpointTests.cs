using System.Net.Http.Json;
using Xunit;

namespace BatteryEms.Api.Tests;

public sealed class RegelleistungHealthEndpointTests : IClassFixture<BatteryEmsApiFactory>
{
    private readonly BatteryEmsApiFactory _factory;

    public RegelleistungHealthEndpointTests(BatteryEmsApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_returns_default_snapshot_with_disabled_gate_and_no_last_activation()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/regelleistung");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<RegelleistungHealthDto>(TestJson.Options);
        Assert.NotNull(body);
        Assert.Equal("healthy", body!.Timebase);
        Assert.Equal("healthy", body.DedupeStore);
        Assert.Equal("disabled", body.ProductionGate);
        Assert.NotNull(body.Preconditions);
        Assert.Null(body.LastActivation);
    }

    private sealed record RegelleistungHealthDto(
        DateTimeOffset At,
        string Timebase,
        string DedupeStore,
        string ProductionGate,
        PreconditionsDto Preconditions,
        LastActivationDto? LastActivation);

    private sealed record PreconditionsDto(
        bool ProductTrust,
        bool TimeSync,
        bool DedupeStoreHealth,
        bool SecurityProfile,
        string ReasonCode);

    private sealed record LastActivationDto(
        string SourceId,
        string ActivationId,
        DateTimeOffset ReceivedAt,
        string ReasonCode,
        bool DispatchRelevant,
        string Details);
}
