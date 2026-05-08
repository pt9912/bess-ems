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
}
