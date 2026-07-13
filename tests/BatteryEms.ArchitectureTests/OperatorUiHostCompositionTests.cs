using BatteryEms.Host;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BatteryEms.ArchitectureTests;

// RM-M6-01-B regression pin: the operator web shell must be wired in the
// production-shaped host composition, not only in the standalone API host.
// The shell went 404 in the compose runtime because BessHostBuilder never
// called UseOperatorUiStaticShell — the /operator -> /operator/ redirect is
// the middleware's observable fingerprint and works without the static-web-
// assets manifest (which only exists in the published image; the runtime
// smoke probes the served files there).
public sealed class OperatorUiHostCompositionTests
{
    [Fact]
    public async Task Host_pipeline_serves_the_operator_shell_redirect()
    {
        await using var app = BessHostBuilder.BuildApp(
        [
            $"--Bess:SchemaDirectory={RepoPath("config", "schema")}",
            $"--Bess:AssetConfigPath={RepoPath("config", "examples", "asset.single-bess.json")}",
        ]);

        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.Single();

            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                CheckCertificateRevocationList = true,
            };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(address) };
            using var response = await client.GetAsync(new Uri("/operator", UriKind.Relative));

            Assert.Equal(System.Net.HttpStatusCode.Found, response.StatusCode);
            Assert.Equal("/operator/", response.Headers.Location?.ToString());
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static string RepoPath(params string[] parts) =>
        Path.Combine([RepoRoot(), .. parts]);

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "BatteryEms.sln")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing BatteryEms.sln.");
    }
}
