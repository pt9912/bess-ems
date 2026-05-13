using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BatteryEms.Application.Control;
using BatteryEms.Application.Persistence;
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
    public async Task Operator_stop_writes_state_and_audit_when_called_with_operator_token()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", BatteryEmsApiFactory.OperatorToken);

        var body = new { asset_id = "asset-stop-1", reason = "manual-shutdown" };
        var response = await client.PostAsJsonAsync("/operator/stop", body, TestJson.Options);

        response.EnsureSuccessStatusCode();
        var ack = await response.Content.ReadFromJsonAsync<OperatorStopDto>(TestJson.Options);
        Assert.NotNull(ack);
        Assert.Equal("asset-stop-1", ack!.AssetId);
        Assert.Equal(BatteryEmsApiFactory.OperatorId, ack.Operator);
        Assert.Equal("manual-shutdown", ack.Reason);
        Assert.True(ack.ActivatedAt > DateTimeOffset.MinValue);

        // Registry sees the activation with the token-derived operator id.
        var registry = _factory.Services.GetRequiredService<IOperatorStopRegistry>();
        var state = registry.Find("asset-stop-1");
        Assert.NotNull(state);
        Assert.Equal(BatteryEmsApiFactory.OperatorId, state!.Operator);
        Assert.Equal("manual-shutdown", state.Reason);

        // Audit log records the accepted attempt.
        var audit = await ReadAuditAsync();
        Assert.Contains(audit, e =>
            e.Operator == BatteryEmsApiFactory.OperatorId
            && e.Action == "operator-stop"
            && e.TargetAssetId == "asset-stop-1"
            && e.Outcome == "accepted");
    }

    [Fact]
    public async Task Operator_stop_status_returns_active_stop_for_asset()
    {
        var registry = _factory.Services.GetRequiredService<IOperatorStopRegistry>();
        registry.Activate(new OperatorStopState(
            AssetId: "asset-stop-status",
            Operator: "operator-status",
            Reason: "maintenance",
            ActivatedAt: new DateTimeOffset(2026, 5, 13, 8, 0, 0, TimeSpan.Zero)));

        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/operator/stops/current?assetId=asset-stop-status");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<OperatorStopStatusDto>(TestJson.Options);

        Assert.NotNull(body);
        Assert.Equal("asset-stop-status", body!.AssetId);
        Assert.NotNull(body.Stop);
        Assert.Equal("operator-status", body.Stop!.Operator);
        Assert.Equal("maintenance", body.Stop.Reason);
    }

    [Fact]
    public async Task Operator_stop_status_returns_null_stop_when_asset_is_not_stopped()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/operator/stops/current?assetId=asset-not-stopped");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<OperatorStopStatusDto>(TestJson.Options);

        Assert.NotNull(body);
        Assert.Equal("asset-not-stopped", body!.AssetId);
        Assert.Null(body.Stop);
    }

    [Fact]
    public async Task Operator_stop_status_returns_400_when_assetId_is_missing()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/operator/stops/current");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorDto>(TestJson.Options);
        Assert.NotNull(body);
        Assert.Equal("missing-asset-id", body!.Error);
    }

    [Theory]
    [InlineData("", "reason")]
    [InlineData("asset", "")]
    public async Task Operator_stop_returns_400_and_invalid_audit_for_blank_field(string assetId, string reason)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", BatteryEmsApiFactory.OperatorToken);
        var body = new { asset_id = assetId, reason };

        var response = await client.PostAsJsonAsync("/operator/stop", body, TestJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var audit = await ReadAuditAsync();
        Assert.Contains(audit, e =>
            e.Operator == BatteryEmsApiFactory.OperatorId
            && e.Action == "operator-stop"
            && e.Outcome == "invalid"
            && e.Reason == "missing-required-field");
    }

    [Fact]
    public async Task Operator_stop_returns_401_and_unauthorized_audit_when_no_token_is_present()
    {
        using var client = _factory.CreateClient();
        var body = new { asset_id = "asset-x", reason = "manual" };

        var response = await client.PostAsJsonAsync("/operator/stop", body, TestJson.Options);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var audit = await ReadAuditAsync();
        Assert.Contains(audit, e =>
            e.Operator == "anonymous"
            && e.Action == "operator-stop"
            && e.Outcome == "unauthorized");
    }

    [Fact]
    public async Task Operator_stop_returns_401_and_unauthorized_audit_when_token_is_unknown()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "no-such-token");
        var body = new { asset_id = "asset-x", reason = "manual" };

        var response = await client.PostAsJsonAsync("/operator/stop", body, TestJson.Options);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var audit = await ReadAuditAsync();
        Assert.Contains(audit, e =>
            e.Operator == "anonymous"
            && e.Action == "operator-stop"
            && e.Outcome == "unauthorized");
    }

    [Fact]
    public async Task Operator_stop_returns_403_and_forbidden_audit_when_token_role_is_not_operator()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", BatteryEmsApiFactory.ViewerToken);
        var body = new { asset_id = "asset-x", reason = "manual" };

        var response = await client.PostAsJsonAsync("/operator/stop", body, TestJson.Options);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var audit = await ReadAuditAsync();
        Assert.Contains(audit, e =>
            e.Operator == BatteryEmsApiFactory.ViewerId
            && e.Action == "operator-stop"
            && e.Outcome == "forbidden");
    }

    private async Task<IReadOnlyList<BatteryEms.Domain.AuditEvent>> ReadAuditAsync()
    {
        var auditLog = _factory.Services.GetRequiredService<IOperatorAuditLog>();
        return await auditLog.QueryAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, CancellationToken.None);
    }

    private sealed record OperatorStopDto(string AssetId, string Operator, string Reason, DateTimeOffset ActivatedAt);

    private sealed record OperatorStopStatusDto(string AssetId, OperatorStopViewDto? Stop);

    private sealed record OperatorStopViewDto(string Operator, string Reason, DateTimeOffset ActivatedAt);

    private sealed record ErrorDto(string Error);
}
