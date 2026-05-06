using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BatteryEms.Api.Observability;

// LH-MON-001: structured stdout logging is configured here so the
// container/host wiring (RM-M1-19) and the API's BuildApp share one
// definition. Splitting it out also keeps BuildApp's class coupling
// under the CA1506 threshold.
public static class LoggingRegistration
{
    public static IHostBuilder ConfigureBessJsonLogging(this IHostBuilder host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return host.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddJsonConsole(o =>
            {
                o.IncludeScopes = true;
                o.UseUtcTimestamp = true;
                o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
            });
        });
    }
}
