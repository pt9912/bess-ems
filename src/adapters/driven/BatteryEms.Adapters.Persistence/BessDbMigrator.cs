using System.Reflection;
using DbUp;
using DbUp.Engine;
using DbUp.Engine.Output;
using DbUp.Helpers;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BatteryEms.Adapters.Persistence;

// RM-M2-MIG-02: versioned-migrations runner. Replaces BessDbInitializer.
// Loads the SQL files embedded as Migrations/RunOnce/????_*.sql, applies
// them in order against the configured Postgres database, and tracks
// applied versions in __schema_versions (per RM-M2-MIG-OPEN-03).
//
// Two safety properties beyond DbUp's defaults:
//   1. Preflight numeric continuity check (per RM-M2-MIG-OPEN-04 in the
//      plan). The runner refuses to apply a script set that doesn't
//      start at 0001, has gaps, or contains duplicate numbers — DbUp
//      itself sorts alphabetically and would silently skip a missing
//      0001 if the next number is 0002. The actual check + tests land
//      with RM-M2-MIG-04; this class exposes the validation seam now.
//   2. Multi-replica boot-race serialisation via pg_advisory_lock with
//      the same ADR 0001 key (`hashtextextended('bess-ems:migrations',
//      0)`). The lock is taken before DbUp runs, released in finally,
//      and the cancellation-token only aborts the wait — never an
//      already-acquired lock. The actual lock-around-DbUp wiring lands
//      with RM-M2-MIG-04; this class exposes the API now.
//
// API matches the original BessDbInitializer for a drop-in cut-over in
// MIG-05: same constructor signature, async MigrateAsync(CancellationToken)
// shape, no return value. Test hosts that already wire BessDbInitializer
// flip to BessDbMigrator without further surface change.
public sealed class BessDbMigrator
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<BessDbMigrator> _logger;

    public BessDbMigrator(NpgsqlDataSource dataSource, ILogger<BessDbMigrator> logger)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(logger);
        _dataSource = dataSource;
        _logger = logger;
    }

    public Task MigrateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connectionString = _dataSource.ConnectionString;
        var assembly = typeof(BessDbMigrator).Assembly;

        // RM-M2-MIG-04 follow-up wraps this DeployChanges call in a
        // pg_advisory_lock and runs the numeric-continuity preflight
        // before invoking DbUp. The skeleton here keeps the surface
        // stable so MIG-04 only adds, never reshapes.
        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(
                assembly,
                static name => name.Contains(".Migrations.RunOnce.", StringComparison.Ordinal))
            .JournalToPostgresqlTable(schema: null, table: "__schema_versions")
            .LogTo(new DbUpLoggerAdapter(_logger))
            .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
        {
            throw new InvalidOperationException(
                $"Schema migration failed at script '{result.ErrorScript?.Name}': {result.Error?.Message}",
                result.Error);
        }
        return Task.CompletedTask;
    }

    // DbUp's IUpgradeLog routes the migrator's own log lines through
    // ILogger so they show up in the host's structured-log stream
    // alongside everything else (LH-MON-001 fields stay consistent).
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private sealed class DbUpLoggerAdapter : IUpgradeLog
    {
        private readonly ILogger<BessDbMigrator> _logger;

        public DbUpLoggerAdapter(ILogger<BessDbMigrator> logger) => _logger = logger;

        // CA2254: DbUp callers use composite-format strings; the
        // adapter contract REQUIRES forwarding them through.
        // CA1848: LoggerMessage source-generators expect a fixed
        // template per call site, which doesn't apply when the
        // template comes from DbUp at run time.
#pragma warning disable CA2254, CA1848
        public void LogTrace(string format, params object[] args) =>
            _logger.LogTrace(format, args);
        public void LogDebug(string format, params object[] args) =>
            _logger.LogDebug(format, args);
        public void LogInformation(string format, params object[] args) =>
            _logger.LogInformation(format, args);
        public void LogWarning(string format, params object[] args) =>
            _logger.LogWarning(format, args);
        public void LogError(string format, params object[] args) =>
            _logger.LogError(format, args);
        public void LogError(Exception ex, string format, params object[] args) =>
            _logger.LogError(ex, format, args);
#pragma warning restore CA2254, CA1848
    }
}
