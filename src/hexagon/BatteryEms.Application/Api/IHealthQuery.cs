namespace BatteryEms.Application.Api;

// LH-API-001 driving port. M1 surfaces a simple "the worker process is
// up" answer; deeper readiness (database reachable, simulator connected,
// last telemetry within max age) follows when the Worker materialises in
// RM-M1-19 — the port shape is forward-compatible.
public interface IHealthQuery
{
    HealthStatus Probe();
}

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record HealthStatus(string Status, DateTimeOffset At);
