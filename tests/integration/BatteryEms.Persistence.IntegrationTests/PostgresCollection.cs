using Xunit;

// Belt to the [Collection("Postgres")] suspenders below: forbids
// cross-class parallelism across the entire assembly. Without it
// a future test class that forgets the [Collection] attribute
// would silently parallelise against the schema-resetting suite
// — a class of CI flake that's hard to bisect. Both mechanisms
// stay in place: the collection is the precise serialisation,
// the assembly attribute is the safety net.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace BatteryEms.Persistence.IntegrationTests;

// Defines the named collection used by every test class in this
// assembly that touches Postgres. The collection itself carries no
// state — xUnit's serialisation guarantee comes from collection
// membership alone — so this is a marker, not a fixture.
[CollectionDefinition("Postgres")]
public sealed class PostgresCollectionMarker
{
}
