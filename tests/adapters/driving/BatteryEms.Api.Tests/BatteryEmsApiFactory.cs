using BatteryEms.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace BatteryEms.Api.Tests;

// In-process host for the API tests. The factory pins a small fleet of
// canonical API tokens so AuthN/AuthZ tests can hit accepted, forbidden,
// and unknown-token paths without each test having to reconfigure the
// host.
public sealed class BatteryEmsApiFactory : WebApplicationFactory<Program>
{
    public const string OperatorToken = "test-operator-token";
    public const string OperatorId = "operator-1";
    public const string ViewerToken = "test-viewer-token";
    public const string ViewerId = "viewer-1";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApiTokens:Tokens:0:Token"] = OperatorToken,
                ["ApiTokens:Tokens:0:Operator"] = OperatorId,
                ["ApiTokens:Tokens:0:Role"] = "operator",
                ["ApiTokens:Tokens:1:Token"] = ViewerToken,
                ["ApiTokens:Tokens:1:Operator"] = ViewerId,
                ["ApiTokens:Tokens:1:Role"] = "viewer",
            });
        });
    }
}
