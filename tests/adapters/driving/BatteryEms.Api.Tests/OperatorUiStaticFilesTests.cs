using Xunit;

namespace BatteryEms.Api.Tests;

public sealed class OperatorUiStaticFilesTests : IClassFixture<BatteryEmsApiFactory>
{
    private readonly BatteryEmsApiFactory _factory;

    public OperatorUiStaticFilesTests(BatteryEmsApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Operator_route_serves_web_shell()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/operator/");

        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("BESS EMS", html, StringComparison.Ordinal);
        Assert.Contains("./app.js", html, StringComparison.Ordinal);

        var script = await client.GetStringAsync("/operator/app.js");
        Assert.Contains("/assets", script, StringComparison.Ordinal);
        Assert.Contains("/operator/stops/current", script, StringComparison.Ordinal);
    }
}
