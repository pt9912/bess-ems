using System.Globalization;
using System.Text.RegularExpressions;
using DbUp;
using DbUp.Engine;
using DbUp.Engine.Output;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BatteryEms.Adapters.Persistence;

// RM-M2-MIG-02: versioned-migrations runner. Loads the SQL files
// embedded as Migrations/RunOnce/????_*.sql, applies them in order
// against the configured Postgres database, and tracks applied
// versions in __schema_versions (per RM-M2-MIG-OPEN-03).
//
// Two safety properties beyond DbUp's defaults (RM-M2-MIG-04):
//   1. Numeric-continuity preflight. DbUp sorts scripts alphabetically
//      and silently skips a gap (e.g. 0001 + 0003 with no 0002), which
//      would let a developer ship an unrunnable history. This migrator
//      validates the embedded set BEFORE handing it to DbUp: the first
//      number must be 0001, every successor must be exactly +1, and no
//      two scripts may share a prefix.
//   2. Multi-replica boot-race serialisation via pg_advisory_lock with
//      the ADR 0001 key (`hashtextextended('bess-ems:migrations', 0)`).
//      The lock is taken on a dedicated session, held across DbUp's
//      run, and released in finally — DbUp's own connection is
//      separate so the lock is purely a process-level mutex on the
//      sentinel resource. Cancellation aborts the wait but never
//      leaks an already-held lock.
//
public sealed partial class BessDbMigrator
{
    // ADR 0001 §2 + RM-M2-MIG-OPEN-06: a fixed string-derived key so
    // every replica acquires the same advisory lock; using a hash of
    // a stable label keeps the bigint argument deterministic across
    // restarts. `hashtextextended(text, seed)` returns bigint, which
    // is the form pg_advisory_lock expects.
    private const string AdvisoryLockKeyExpr =
        "hashtextextended('bess-ems:migrations', 0)";

    private readonly NpgsqlDataSource _dataSource;
    // DbUp opens its own NpgsqlConnection from a raw connection string
    // and needs the password in clear text. NpgsqlDataSource.ConnectionString
    // intentionally strips the password (defense-in-depth), so the
    // migrator carries the original string from the caller separately.
    private readonly string _rawConnectionString;
    private readonly ILogger<BessDbMigrator> _logger;
    private readonly IReadOnlyList<string>? _scriptNamesOverride;

    public BessDbMigrator(
        NpgsqlDataSource dataSource,
        string connectionString,
        ILogger<BessDbMigrator> logger)
        : this(dataSource, connectionString, logger, scriptNamesOverride: null)
    {
    }

    // Test seam: lets a unit test feed a synthetic script-name list
    // through the continuity preflight without touching the real
    // embedded resources or a database. Production always passes null.
    internal BessDbMigrator(
        NpgsqlDataSource dataSource,
        string connectionString,
        ILogger<BessDbMigrator> logger,
        IReadOnlyList<string>? scriptNamesOverride)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(logger);
        _dataSource = dataSource;
        _rawConnectionString = connectionString;
        _logger = logger;
        _scriptNamesOverride = scriptNamesOverride;
    }

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Preflight: refuses to call DbUp on a malformed script set.
        // The override path lets unit tests exercise this without an
        // assembly scan; production reads the embedded resources.
        var scriptNames = _scriptNamesOverride
            ?? typeof(BessDbMigrator).Assembly.GetManifestResourceNames()
                .Where(name => name.Contains(".Migrations.RunOnce.", StringComparison.Ordinal))
                .ToArray();
        EnsureRunOnceContinuityValid(scriptNames);

        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            // Acquire the advisory lock on this session before running
            // DbUp. Two parallel migrators against the same database
            // serialise here: the second waits until the first releases.
            await AcquireAdvisoryLockAsync(connection, cancellationToken)
                .ConfigureAwait(false);
            try
            {
                RunDbUp();
            }
            finally
            {
                // Best-effort release. If the connection is already
                // dead the lock will be released by Postgres when the
                // session ends, so swallow transport errors here so
                // disposal never throws.
                await ReleaseAdvisoryLockAsync(connection).ConfigureAwait(false);
            }
        }
    }

    // Preflight: verify the embedded script set starts at 0001, has no
    // gaps, and contains no duplicate numeric prefixes. Static so unit
    // tests can call it directly with synthetic script-name arrays.
    internal static void EnsureRunOnceContinuityValid(IReadOnlyList<string> scriptNames)
    {
        ArgumentNullException.ThrowIfNull(scriptNames);

        if (scriptNames.Count == 0)
        {
            throw new InvalidOperationException(
                "No RunOnce migrations are embedded; the migrator refuses to run against an empty script set. "
                + "If this is a fresh checkout, ensure 0001_initial.sql is committed under "
                + "src/adapters/driven/BatteryEms.Adapters.Persistence/Migrations/RunOnce/.");
        }

        var numbers = new SortedSet<int>();
        var duplicates = new HashSet<int>();
        foreach (var name in scriptNames)
        {
            var match = ScriptNumberRegex().Match(name);
            if (!match.Success)
            {
                throw new InvalidOperationException(
                    $"Migration script '{name}' does not match the required ????_*.sql naming "
                    + "convention (exactly 4 digits then '_' then a non-empty basename); the "
                    + "migrator cannot determine its sequence position. The 4-digit ceiling is "
                    + "a hard limit because DbUp sorts script names alphabetically — a 5-digit "
                    + "prefix would sort before 4-digit ones (lexicographically '1' < '9') and "
                    + "break the apply order.");
            }
            var number = int.Parse(match.Groups["num"].Value, CultureInfo.InvariantCulture);
            if (!numbers.Add(number))
            {
                duplicates.Add(number);
            }
        }
        if (duplicates.Count > 0)
        {
            var dupList = string.Join(", ", duplicates.Select(n => n.ToString("D4", CultureInfo.InvariantCulture)));
            throw new InvalidOperationException(
                $"Duplicate migration number(s) detected: {dupList}. Each ????_-prefix must be unique.");
        }

        // Continuity: must start at 1, every successor exactly +1.
        var expected = 1;
        foreach (var n in numbers)
        {
            if (n != expected)
            {
                throw new InvalidOperationException(
                    $"Migration sequence has a gap or wrong start: expected {expected:D4} but found {n:D4}. "
                    + "DbUp would silently skip the gap; the migrator refuses to run.");
            }
            expected++;
        }
    }

    private void RunDbUp()
    {
        var upgrader = DeployChanges.To
            .PostgresqlDatabase(_rawConnectionString)
            .WithScriptsEmbeddedInAssembly(
                typeof(BessDbMigrator).Assembly,
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
    }

    // Carve-out M2: split the former boolean-flag ExecuteAdvisoryLockAsync
    // into two named operations so the asymmetric cancellation
    // semantics are visible in the call site. Acquire MUST honour the
    // caller's CancellationToken (a startup ctrl-C must abort the wait
    // for the lock); Release MUST NOT take a token (cancelling the
    // unlock would leave the lock dangling until session end, which is
    // strictly worse than running unlock unconditionally).
    private static async Task AcquireAdvisoryLockAsync(
        NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = $"SELECT pg_advisory_lock({AdvisoryLockKeyExpr});";
            await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design", "CA1031",
        Justification = "Best-effort lock release on shutdown; transport may already be torn down.")]
    private static async Task ReleaseAdvisoryLockAsync(NpgsqlConnection connection)
    {
        try
        {
            var cmd = connection.CreateCommand();
            await using (cmd.ConfigureAwait(false))
            {
                cmd.CommandText = $"SELECT pg_advisory_unlock({AdvisoryLockKeyExpr});";
                await cmd.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // Postgres frees session-level locks automatically on
            // session end; if the explicit unlock fails, the lock is
            // released by the time the connection is disposed.
        }
    }

    [GeneratedRegex(@"\.Migrations\.RunOnce\.(?<num>\d{4})_[^.]+\.sql$")]
    private static partial Regex ScriptNumberRegex();

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
