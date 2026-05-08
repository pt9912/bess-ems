using Xunit;

namespace BatteryEms.Persistence.IntegrationTests;

// All integration tests in this assembly share the single Postgres
// instance from tests/integration/compose.yml. Test classes that
// reset the schema (BessDbMigratorIntegrationTests via DROP SCHEMA)
// would race with classes that assume the migrator-built state
// (PersistenceRoundtripTests). Joining them in one collection
// disables xUnit's default cross-class parallelism so each class
// owns the database in turn.
[CollectionDefinition("Postgres")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming", "CA1711",
    Justification = "xUnit collection fixtures conventionally end with 'Collection'.")]
public sealed class PostgresCollection : ICollectionFixture<object>
{
}
