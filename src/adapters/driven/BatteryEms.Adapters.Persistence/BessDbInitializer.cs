using Npgsql;

namespace BatteryEms.Adapters.Persistence;

// Runs BessDbSchema.CreateScript against the configured database. The
// script is idempotent (CREATE TABLE IF NOT EXISTS), so calling
// InitializeAsync at every boot is safe; production systems can pin
// the call to a one-off worker step if start-up time matters.
//
// RM-M2-MIG-02 superseded this class with BessDbMigrator (versioned
// migrations via DbUp). The cut-over lands with RM-M2-MIG-05; until
// then the initializer stays available for tests that pre-date the
// migrator. New callers must wire BessDbMigrator instead.
[Obsolete(
    "Use BessDbMigrator (RM-M2-MIG-02) for new wiring. The idempotent "
    + "CREATE TABLE IF NOT EXISTS path will be removed with RM-M2-MIG-05 "
    + "once all hosts cut over to the versioned-migration runner.",
    error: false)]
public sealed class BessDbInitializer
{
    private readonly NpgsqlDataSource _dataSource;

    public BessDbInitializer(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new NpgsqlCommand(BessDbSchema.CreateScript, connection);
            await using (command.ConfigureAwait(false))
            {
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
