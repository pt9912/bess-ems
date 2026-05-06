using BatteryEms.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BatteryEms.Api.Tests;

// Lightweight in-process host for the read endpoints. The (in-memory)
// DI wiring inside Program is reused as-is so the WebApplicationFactory
// exercises the same composition path production runs.
public sealed class BatteryEmsApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // No extra configuration needed for the read endpoints (no auth,
        // no external services). The base behaviour spins up the same
        // pipeline Program.Main builds.
    }
}
