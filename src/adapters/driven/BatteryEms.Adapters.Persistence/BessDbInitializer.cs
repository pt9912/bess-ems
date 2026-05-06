using Npgsql;

namespace BatteryEms.Adapters.Persistence;

// Runs BessDbSchema.CreateScript against the configured database. The
// script is idempotent (CREATE TABLE IF NOT EXISTS), so calling
// InitializeAsync at every boot is safe; production systems can pin
// the call to a one-off worker step if start-up time matters.
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
