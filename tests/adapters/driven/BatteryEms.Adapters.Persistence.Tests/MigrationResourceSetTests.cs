using BatteryEms.Adapters.Persistence;
using Xunit;

namespace BatteryEms.Adapters.Persistence.Tests;

// RM-M2-MIG-06: pin the embedded-resource layout. Drafts under
// Migrations/Drafts/ are deliberately Build-Action None so DbUp
// never sees them; the only way they can leak into the script set
// is if someone adds a stray <EmbeddedResource Include=
// "Migrations/Drafts/**/*.sql" /> to the csproj. This test catches
// that mistake at unit-test time, before it reaches a database.
public sealed class MigrationResourceSetTests
{
    private const string RunOnceMarker = ".Migrations.RunOnce.";
    private const string DraftsMarker = ".Migrations.Drafts.";

    [Fact]
    public void Only_RunOnce_scripts_are_embedded()
    {
        var resources = typeof(BessDbMigrator).Assembly.GetManifestResourceNames();

        var runOnce = resources
            .Where(r => r.Contains(RunOnceMarker, StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(runOnce);
        Assert.Contains(runOnce, r => r.EndsWith(".0001_initial.sql", StringComparison.Ordinal));
        Assert.Contains(runOnce, r => r.EndsWith(".0005_timescale_telemetry_hypertable.sql", StringComparison.Ordinal));
    }

    [Fact]
    public void Timescale_migration_is_guarded_for_plain_postgres()
    {
        var sql = ReadRunOnceScript("0005_timescale_telemetry_hypertable.sql");

        Assert.Contains("pg_available_extensions", sql, StringComparison.Ordinal);
        Assert.Contains("TimescaleDB extension is not available", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE EXTENSION IF NOT EXISTS timescaledb", sql, StringComparison.Ordinal);
        Assert.Contains("insufficient_privilege", sql, StringComparison.Ordinal);
        Assert.Contains("create_hypertable", sql, StringComparison.Ordinal);
        Assert.Contains("PRIMARY KEY (\"id\", \"recorded_at\")", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("$bess_timescale$", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void No_draft_migration_is_embedded()
    {
        var resources = typeof(BessDbMigrator).Assembly.GetManifestResourceNames();

        var drafts = resources
            .Where(r => r.Contains(DraftsMarker, StringComparison.Ordinal))
            .ToArray();
        Assert.Empty(drafts);
    }

    [Fact]
    public void Embedded_resource_set_contains_only_RunOnce_migration_scripts()
    {
        // Carve-out Mn4: belt-and-suspenders allowlist. The two
        // tests above check positive (RunOnce contains the
        // expected file) and one negative (no Drafts/* leak), but
        // neither catches a wholly different leak — e.g. someone
        // adds <EmbeddedResource Include="**/*.json" /> and ships
        // an unintended payload through DbUp's filter (it would
        // skip the json, but the resource set would still grow
        // silently). This test fences the manifest: every assembly
        // resource must either match the RunOnce/????_*.sql
        // pattern or live on the explicit allowlist.
        var assembly = typeof(BessDbMigrator).Assembly;
        var resources = assembly.GetManifestResourceNames();

        var allowedNonScript = new[]
        {
            // .NET adds these automatically for resx-style strings;
            // they would never appear here today but are reserved
            // by the framework if a future class adds a .resx file.
            ".g.resources",
        };

        var unexpected = resources
            .Where(r => !r.Contains(RunOnceMarker, StringComparison.Ordinal))
            .Where(r => !allowedNonScript.Any(a => r.EndsWith(a, StringComparison.Ordinal)))
            .ToArray();

        Assert.True(
            unexpected.Length == 0,
            "Unexpected manifest resources detected (every embedded resource must "
            + "be a RunOnce/????_*.sql script or appear on the allowlist): "
            + string.Join(", ", unexpected));
    }

    private static string ReadRunOnceScript(string fileName)
    {
        var assembly = typeof(BessDbMigrator).Assembly;
        var resource = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("." + fileName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded migration not found: {fileName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
