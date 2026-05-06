using BatteryEms.Application.Markets;
using BatteryEms.Application.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BatteryEms.Adapters.Persistence;

// DI extension for the Postgres-backed persistence adapter. The host
// (RM-M1-19a composition root) calls AddBessPersistence once; the
// returned IServiceCollection registers the NpgsqlDataSource as a
// singleton (connection pool reuse), the four Application-side
// repositories, and the schema initialiser used at start-up.
public static class PersistenceRegistration
{
    public static IServiceCollection AddBessPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.AddSingleton<BessDbInitializer>();

        services.AddSingleton<ITelemetryRepository, DapperTelemetryRepository>();
        services.AddSingleton<ICommandRepository, DapperCommandRepository>();
        services.AddSingleton<IScheduleRepository, DapperScheduleRepository>();
        services.AddSingleton<IOperatorAuditLog, DapperOperatorAuditLog>();
        services.AddSingleton<IRetentionRepository, DapperRetentionRepository>();
        return services;
    }
}
