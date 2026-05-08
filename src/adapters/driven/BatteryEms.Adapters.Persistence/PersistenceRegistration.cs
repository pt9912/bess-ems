using BatteryEms.Application.Api;
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
        // BessDbInitializer is [Obsolete] (RM-M2-MIG-02 superseded it
        // with BessDbMigrator); the registration stays here until the
        // MIG-05 cut-over actually rewires BessHostBuilder to call the
        // migrator and the integration tests stop new'ing the
        // initializer directly. Suppressed at the one call site that
        // legitimately needs the obsolete API during the cut-over
        // window.
#pragma warning disable CS0618
        services.AddSingleton<BessDbInitializer>();
#pragma warning restore CS0618
        services.AddSingleton<BessDbMigrator>();

        services.AddSingleton<ITelemetryRepository, DapperTelemetryRepository>();
        services.AddSingleton<ICommandRepository, DapperCommandRepository>();
        services.AddSingleton<IScheduleRepository, DapperScheduleRepository>();
        services.AddSingleton<IOperatorAuditLog, DapperOperatorAuditLog>();
        services.AddSingleton<IRetentionRepository, DapperRetentionRepository>();
        services.AddSingleton<IOptimizationRunRepository, DapperOptimizationRunRepository>();
        // RM-M1-19c: replace the default "ok"-only IHealthQuery with the
        // Postgres-aware probe so /health returns 503 when the database
        // is unreachable. Order matters: AddBessApplicationInMemoryStores
        // registers DefaultHealthQuery first; AddBessPersistence runs
        // afterwards in the host and the last registration wins for
        // GetService<IHealthQuery>().
        services.AddSingleton<IHealthQuery, DapperHealthQuery>();
        return services;
    }
}
