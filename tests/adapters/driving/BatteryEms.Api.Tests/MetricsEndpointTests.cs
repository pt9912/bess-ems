using System.Net;
using Xunit;

namespace BatteryEms.Api.Tests;

public sealed class MetricsEndpointTests : IClassFixture<BatteryEmsApiFactory>
{
    private readonly BatteryEmsApiFactory _factory;

    public MetricsEndpointTests(BatteryEmsApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_metrics_returns_prometheus_text_format_with_default_metrics()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/metrics", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var contentType = response.Content.Headers.ContentType?.MediaType;
        // prometheus-net advertises the OpenMetrics-compatible text/plain content
        // type; the exact charset/version varies across versions, so we only
        // assert the family.
        Assert.NotNull(contentType);
        Assert.StartsWith("text/plain", contentType!, StringComparison.Ordinal);

        var body = await response.Content.ReadAsStringAsync();
        // process_start_time_seconds is one of the default prometheus-net
        // metrics that is emitted unconditionally; its presence proves the
        // scrape pipeline is wired and ASP.NET serves the registry.
        Assert.Contains("process_start_time_seconds", body, StringComparison.Ordinal);
    }
}
