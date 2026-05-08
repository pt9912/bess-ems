using BatteryEms.Adapters.Persistence;
using Xunit;

namespace BatteryEms.Adapters.Persistence.Tests;

// RM-M2-MIG-04: unit tests for the BessDbMigrator's numeric-continuity
// preflight. The preflight runs against the embedded RunOnce-resource
// names BEFORE DbUp is invoked; DbUp itself sorts alphabetically and
// would silently apply a 0001+0003 set, skipping a missing 0002 with
// no warning. The migrator refuses to do that — these tests pin the
// rules without needing a database.
public sealed class BessDbMigratorContinuityTests
{
    private const string Prefix = "BatteryEms.Adapters.Persistence.Migrations.RunOnce.";

    private static readonly string[] Sequential = {
        Prefix + "0001_initial.sql",
        Prefix + "0002_schedules_unique.sql",
        Prefix + "0003_lock_table.sql",
    };

    private static readonly string[] SingleFirst = { Prefix + "0001_initial.sql" };

    private static readonly string[] StartsTooHigh = {
        Prefix + "0002_starts_too_high.sql",
        Prefix + "0003_next.sql",
    };

    private static readonly string[] GapBetween = {
        Prefix + "0001_initial.sql",
        Prefix + "0003_skipped_0002.sql",
    };

    private static readonly string[] DuplicateFirst = {
        Prefix + "0001_first.sql",
        Prefix + "0001_clone.sql",
        Prefix + "0002_next.sql",
    };

    private static readonly string[] MissingPrefix = {
        Prefix + "0001_initial.sql",
        Prefix + "NoNumber_typo.sql",
    };

    private static readonly string[] OutOfOrder = {
        Prefix + "0003_three.sql",
        Prefix + "0001_one.sql",
        Prefix + "0002_two.sql",
    };

    [Fact]
    public void Empty_script_set_is_rejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            BessDbMigrator.EnsureRunOnceContinuityValid(Array.Empty<string>()));
        Assert.Contains("No RunOnce migrations are embedded", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Sequential_starting_at_0001_passes()
    {
        BessDbMigrator.EnsureRunOnceContinuityValid(Sequential);
    }

    [Fact]
    public void Single_0001_passes()
    {
        BessDbMigrator.EnsureRunOnceContinuityValid(SingleFirst);
    }

    [Fact]
    public void Set_starting_at_0002_throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            BessDbMigrator.EnsureRunOnceContinuityValid(StartsTooHigh));
        Assert.Contains("expected 0001 but found 0002", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Gap_between_0001_and_0003_throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            BessDbMigrator.EnsureRunOnceContinuityValid(GapBetween));
        Assert.Contains("expected 0002 but found 0003", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_numbers_are_listed_in_the_error_message()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            BessDbMigrator.EnsureRunOnceContinuityValid(DuplicateFirst));
        Assert.Contains("Duplicate migration number(s) detected: 0001",
            ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_without_numeric_prefix_is_rejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            BessDbMigrator.EnsureRunOnceContinuityValid(MissingPrefix));
        Assert.Contains("does not match the required ????_*.sql naming convention",
            ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Order_of_input_does_not_matter()
    {
        // The preflight sorts internally; callers can pass scripts in
        // any order (assembly resource order is unspecified).
        BessDbMigrator.EnsureRunOnceContinuityValid(OutOfOrder);
    }

    [Fact]
    public void Null_script_list_throws_argument_null()
    {
        Assert.Throws<ArgumentNullException>(() =>
            BessDbMigrator.EnsureRunOnceContinuityValid(null!));
    }
}
