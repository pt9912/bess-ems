using BatteryEms.Application.Api;
using BatteryEms.Application.Time;
using Dapper;
using Npgsql;

namespace BatteryEms.Adapters.Persistence;

// Health probe that wraps the default "ok" answer with a synchronous
// SELECT 1 against the configured Postgres data source. The driving
// adapter (RM-M1-15) already exposes /health as a readiness probe;
// surfacing the database state here turns a missing or unreachable
// Postgres into HTTP 503 instead of a successful boot followed by
// silent persistence failures (LH-OPS-001 + LH-PERSIST-006).
//
// The probe lives in the persistence adapter — not in Application —
// because resolving NpgsqlDataSource is an infrastructure concern.
public sealed class DapperHealthQuery : IHealthQuery
{
    private const string ProbeSql = "SELECT 1";

    private readonly NpgsqlDataSource _dataSource;
    private readonly IClock _clock;

    public DapperHealthQuery(NpgsqlDataSource dataSource, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(clock);
        DapperConfig.EnsureConfigured();
        _dataSource = dataSource;
        _clock = clock;
    }

    public HealthStatus Probe()
    {
        var components = new Dictionary<string, string>(StringComparer.Ordinal);
        string status;
        try
        {
            using var connection = _dataSource.OpenConnection();
            connection.ExecuteScalar<int>(new CommandDefinition(ProbeSql, commandTimeout: 2));
            components["database"] = "ok";
            status = "ok";
        }
#pragma warning disable CA1031 // Health probe must surface every failure as "unhealthy" rather than crash.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            components["database"] = $"error:{ex.GetType().Name}";
            status = "unhealthy";
        }
        return new HealthStatus(status, _clock.UtcNow, components);
    }
}
