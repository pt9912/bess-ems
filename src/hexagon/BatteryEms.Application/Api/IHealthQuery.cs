namespace BatteryEms.Application.Api;

// LH-API-001 driving port. The simple variant (RM-M1-15a) returned a
// flat "ok" + timestamp; RM-M1-19c adds an optional Components map so
// the Persistence-aware implementation can surface the database probe
// alongside the overall status. Status values:
//   "ok"        — every component is reachable
//   "unhealthy" — at least one critical component failed
public interface IHealthQuery
{
    HealthStatus Probe();
}

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record HealthStatus(
    string Status,
    DateTimeOffset At,
    IReadOnlyDictionary<string, string>? Components = null);
