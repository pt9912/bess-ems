using System.Globalization;
using BatteryEms.Adapters.Persistence;
using BatteryEms.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace BatteryEms.Persistence.IntegrationTests;

// RM-M2-MIG-04: integration tests for BessDbMigrator against a real
// Postgres. The unit tests in BatteryEms.Adapters.Persistence.Tests
// already pin the continuity-preflight rules; these tests cover the
// runtime properties that need a database:
//   * The DbUp run actually creates the schema and records the
//     0001-version in the __schema_versions journal.
//   * A second MigrateAsync call is a no-op (DbUp recognises the
//     journaled version and skips it).
//   * Two parallel MigrateAsync calls serialise via pg_advisory_lock
//     instead of racing the journal-INSERT and producing duplicates.
//
// Each test starts by dropping schema public so the migrator runs
// against a clean state regardless of which other test class touched
// the DB before. The shared compose.yml Postgres is serialised with
// the [Collection("Postgres")] marker on both this class and
// PersistenceRoundtripTests.
[Trait("Category", "Integration")]
[Collection("Postgres")]
public sealed class BessDbMigratorIntegrationTests : IAsyncLifetime
{
    private NpgsqlDataSource? _dataSource;
    private string? _connectionString;

    private static string Host => Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "127.0.0.1";
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("POSTGRES_PORT"), out var p) ? p : 5432;
    private static string Database => Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "bessems";
    private static string User => Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "bessems";
    private static string Password => Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? "bessems";

    public Task InitializeAsync()
    {
        var options = PersistenceOptions.FromHostPort(Host, Port, Database, User, Password);
        _connectionString = options.ConnectionString;
        _dataSource = NpgsqlDataSource.Create(_connectionString);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }
    }

    [Fact]
    public async Task MigrateAsync_applies_0001_and_records_in_journal()
    {
        await ResetSchemaAsync(_dataSource!);
        var migrator = new BessDbMigrator(_dataSource!, _connectionString!, NullLogger<BessDbMigrator>.Instance);

        await migrator.MigrateAsync(CancellationToken.None);

        // The 0001 migration creates the bess-ems schema; assert via
        // the telemetry sentinel table that DbUp actually executed.
        Assert.True(await TableExistsAsync(_dataSource!, "telemetry"));

        // DbUp records each applied script in __schema_versions; the
        // resource basename ends with `0001_initial.sql` regardless of
        // assembly-namespace prefix, so the LIKE filter is robust.
        var journalRows = await CountJournalEntriesAsync(_dataSource!, "%0001_initial.sql");
        Assert.Equal(1, journalRows);
    }

    [Fact]
    public async Task MigrateAsync_is_idempotent_on_second_call()
    {
        await ResetSchemaAsync(_dataSource!);
        var migrator = new BessDbMigrator(_dataSource!, _connectionString!, NullLogger<BessDbMigrator>.Instance);

        await migrator.MigrateAsync(CancellationToken.None);
        var firstCount = await CountJournalEntriesAsync(_dataSource!, "%0001_initial.sql");

        // Second call must observe the existing journal entry and skip
        // re-applying 0001. Anything else would trip on CREATE TABLE
        // (the embedded SQL has no IF NOT EXISTS) and throw.
        await migrator.MigrateAsync(CancellationToken.None);
        var secondCount = await CountJournalEntriesAsync(_dataSource!, "%0001_initial.sql");

        Assert.Equal(1, firstCount);
        Assert.Equal(1, secondCount);
    }

    [Fact]
    public async Task Timescale_migration_is_recorded_and_keeps_plain_postgres_schema_usable()
    {
        await ResetSchemaAsync(_dataSource!);
        var migrator = new BessDbMigrator(_dataSource!, _connectionString!, NullLogger<BessDbMigrator>.Instance);

        await migrator.MigrateAsync(CancellationToken.None);

        var journalRows = await CountJournalEntriesAsync(_dataSource!, "%0005_timescale_telemetry_hypertable.sql");
        Assert.Equal(1, journalRows);
        Assert.True(await TableExistsAsync(_dataSource!, "telemetry"));

        var telemetry = new DapperTelemetryRepository(_dataSource!);
        await telemetry.AppendAsync(new BatteryTelemetry(
            Timestamp: new DateTimeOffset(2026, 5, 13, 12, 0, 0, TimeSpan.Zero),
            AssetId: "timescale-plain-postgres",
            SocPercent: 50,
            SohPercent: 99,
            ActivePowerKw: 0,
            ReactivePowerKvar: 0,
            DcVoltage: 800,
            DcCurrent: 0,
            TemperatureCelsius: 22,
            Available: true,
            FaultStatus: "ok",
            DataQuality: DataQuality.Valid),
            CancellationToken.None);

        var latest = await telemetry.FindLatestAsync("timescale-plain-postgres", CancellationToken.None);
        Assert.NotNull(latest);
    }

    [Fact]
    public async Task Two_parallel_MigrateAsync_calls_serialize_via_advisory_lock()
    {
        await ResetSchemaAsync(_dataSource!);

        // Two distinct migrators (independent NpgsqlConnections from the
        // same DataSource) start in parallel. Without the advisory
        // lock, both would race past the journal-empty check and try
        // to CREATE TABLE telemetry, the loser raising "relation
        // already exists". With the lock, the second migrator waits
        // for the first to finish, then sees the journal entry and
        // exits cleanly.
        var migrator = new BessDbMigrator(_dataSource!, _connectionString!, NullLogger<BessDbMigrator>.Instance);

        var taskA = Task.Run(() => migrator.MigrateAsync(CancellationToken.None));
        var taskB = Task.Run(() => migrator.MigrateAsync(CancellationToken.None));

        await Task.WhenAll(taskA, taskB);

        var journalRows = await CountJournalEntriesAsync(_dataSource!, "%0001_initial.sql");
        Assert.Equal(1, journalRows);
        Assert.True(await TableExistsAsync(_dataSource!, "telemetry"));
    }

    private static async Task ResetSchemaAsync(NpgsqlDataSource dataSource)
    {
        var connection = await dataSource.OpenConnectionAsync();
        await using (connection.ConfigureAwait(false))
        {
            var cmd = connection.CreateCommand();
            await using (cmd.ConfigureAwait(false))
            {
                cmd.CommandText = "DROP SCHEMA public CASCADE; CREATE SCHEMA public;";
                await cmd.ExecuteNonQueryAsync();
            }
        }
    }

    private static async Task<bool> TableExistsAsync(NpgsqlDataSource dataSource, string tableName)
    {
        var connection = await dataSource.OpenConnectionAsync();
        await using (connection.ConfigureAwait(false))
        {
            var cmd = connection.CreateCommand();
            await using (cmd.ConfigureAwait(false))
            {
                cmd.CommandText =
                    "SELECT EXISTS (SELECT 1 FROM information_schema.tables "
                    + "WHERE table_schema = 'public' AND table_name = @name);";
                cmd.Parameters.AddWithValue("@name", tableName);
                var result = await cmd.ExecuteScalarAsync();
                return result is bool b && b;
            }
        }
    }

    private static async Task<long> CountJournalEntriesAsync(NpgsqlDataSource dataSource, string scriptNameLike)
    {
        var connection = await dataSource.OpenConnectionAsync();
        await using (connection.ConfigureAwait(false))
        {
            var cmd = connection.CreateCommand();
            await using (cmd.ConfigureAwait(false))
            {
                cmd.CommandText =
                    "SELECT COUNT(*) FROM __schema_versions WHERE scriptname LIKE @pat;";
                cmd.Parameters.AddWithValue("@pat", scriptNameLike);
                var result = await cmd.ExecuteScalarAsync();
                return result is long l ? l : Convert.ToInt64(result, CultureInfo.InvariantCulture);
            }
        }
    }
}
