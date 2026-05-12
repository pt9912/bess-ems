using BatteryEms.Application.Api;
using BatteryEms.Application.Markets;
using BatteryEms.Application.Optimization;
using BatteryEms.Application.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        // BessDbMigrator needs the raw connection string for DbUp
        // (NpgsqlDataSource.ConnectionString masks the password); a
        // factory captures the caller's string in the closure rather
        // than registering it as a separate ambient service.
        services.AddSingleton(sp => new BessDbMigrator(
            sp.GetRequiredService<NpgsqlDataSource>(),
            connectionString,
            sp.GetRequiredService<ILogger<BessDbMigrator>>()));

        services.AddSingleton<ITelemetryRepository, DapperTelemetryRepository>();
        services.AddSingleton<ICommandRepository, DapperCommandRepository>();
        services.AddSingleton<IScheduleRepository, DapperScheduleRepository>();
        services.AddSingleton<IActivationDedupeStore, DapperActivationDedupeStore>();
        services.AddSingleton<IOperatorAuditLog, DapperOperatorAuditLog>();
        services.AddSingleton<IRetentionRepository, DapperRetentionRepository>();
        services.AddSingleton<IOptimizationRunRepository, DapperOptimizationRunRepository>();
        services.AddSingleton<IMpcRunRepository, DapperMpcRunRepository>();
        // RM-M5-01-C step 4: ersetzt das InMemoryOptimizationIdempotency-
        // Store-Binding aus AddBessApplicationInMemoryStores durch die
        // Postgres-backed Variante (CAS via INSERT...ON CONFLICT +
        // UPDATE WHERE Pending). Restart-Replay-fest pro request_id.
        services.AddSingleton<IOptimizationIdempotencyStore, DapperOptimizationIdempotencyStore>();
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
