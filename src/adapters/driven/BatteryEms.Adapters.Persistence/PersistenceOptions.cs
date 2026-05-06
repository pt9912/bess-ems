namespace BatteryEms.Adapters.Persistence;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record PersistenceOptions(string ConnectionString)
{
    public static PersistenceOptions FromHostPort(
        string host,
        int port,
        string database,
        string user,
        string password) =>
        new($"Host={host};Port={port};Database={database};Username={user};Password={password};Include Error Detail=true");
}
