using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Domain.Tests;

public sealed class RegelleistungOptionsTests
{
    // Defaults pin (plan-RM-M4-03 §144). The three time tolerances are
    // master-DoD wording and must not silently drift; MaxEntriesPerSource
    // is intentionally NOT pinned here (operator-tunable per the DoD
    // wording "eine konfigurierte Obergrenze").
    [Fact]
    public void Defaults_pin_master_dod_tolerances()
    {
        var options = new RegelleistungOptions();

        Assert.Equal(TimeSpan.FromSeconds(2), options.MaxAge);
        Assert.Equal(TimeSpan.FromMilliseconds(500), options.FutureSkewTolerance);
        Assert.Equal(TimeSpan.FromSeconds(10), options.DedupeWindow);
    }

    [Fact]
    public void Default_max_entries_per_source_is_positive_but_not_pinned()
    {
        // Intentional smoke check only: a positive default exists.
        // The exact number is operator-tunable and must be free to
        // change without a pin-test edit.
        Assert.True(new RegelleistungOptions().MaxEntriesPerSource > 0);
    }

    [Fact]
    public void Ensure_valid_returns_self_for_default_options()
    {
        var options = new RegelleistungOptions();
        Assert.Same(options, options.EnsureValid());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_max_age_throws(int seconds)
    {
        var options = new RegelleistungOptions { MaxAge = TimeSpan.FromSeconds(seconds) };
        Assert.Throws<ArgumentException>(() => options.EnsureValid());
    }

    [Fact]
    public void Negative_future_skew_tolerance_throws()
    {
        var options = new RegelleistungOptions
        {
            FutureSkewTolerance = TimeSpan.FromMilliseconds(-1),
        };
        Assert.Throws<ArgumentException>(() => options.EnsureValid());
    }

    [Fact]
    public void Zero_future_skew_tolerance_is_accepted()
    {
        // A 0-tolerance configuration is lawful (tightest possible
        // future-skew gate). Operator decision; the validator does the
        // right thing with it.
        var options = new RegelleistungOptions
        {
            FutureSkewTolerance = TimeSpan.Zero,
        };
        Assert.Same(options, options.EnsureValid());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_dedupe_window_throws(int seconds)
    {
        var options = new RegelleistungOptions { DedupeWindow = TimeSpan.FromSeconds(seconds) };
        Assert.Throws<ArgumentException>(() => options.EnsureValid());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_max_entries_per_source_throws(int max)
    {
        var options = new RegelleistungOptions { MaxEntriesPerSource = max };
        Assert.Throws<ArgumentOutOfRangeException>(() => options.EnsureValid());
    }
}
