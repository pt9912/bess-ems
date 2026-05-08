using BatteryEms.Application.Realtime;
using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Application.Tests;

public sealed class SnapshotStoreTests
{
    [Fact]
    public void Get_returns_null_when_asset_unknown()
    {
        var store = new InMemorySnapshotStore(TimeSpan.FromSeconds(10));
        Assert.Null(store.GetLatest("unknown", TestFixtures.Now));
    }

    [Fact]
    public void Update_stores_telemetry_with_received_at()
    {
        var store = new InMemorySnapshotStore(TimeSpan.FromSeconds(10));
        var telemetry = TestFixtures.CreateTelemetry();

        store.Update(telemetry, TestFixtures.Now);

        var snapshot = store.GetLatest("asset-1", TestFixtures.Now);
        Assert.NotNull(snapshot);
        Assert.Equal(TestFixtures.Now, snapshot!.ReceivedAt);
        Assert.Equal(DataQualityState.Valid, snapshot.Quality.Flag);
    }

    [Fact]
    public void Snapshot_within_max_age_keeps_quality_valid()
    {
        var store = new InMemorySnapshotStore(TimeSpan.FromSeconds(10));
        store.Update(TestFixtures.CreateTelemetry(), TestFixtures.Now);

        var snapshot = store.GetLatest("asset-1", TestFixtures.Now + TimeSpan.FromSeconds(9));

        Assert.NotNull(snapshot);
        Assert.Equal(DataQualityState.Valid, snapshot!.Quality.Flag);
    }

    [Fact]
    [Trait("Category", "Safety")]
    public void Snapshot_beyond_max_age_becomes_stale()
    {
        var store = new InMemorySnapshotStore(TimeSpan.FromSeconds(10));
        store.Update(TestFixtures.CreateTelemetry(), TestFixtures.Now);

        var snapshot = store.GetLatest("asset-1", TestFixtures.Now + TimeSpan.FromSeconds(15));

        Assert.NotNull(snapshot);
        Assert.Equal(DataQualityState.Stale, snapshot!.Quality.Flag);
        Assert.Contains("snapshot-aged", snapshot.Quality.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    [Trait("Category", "Safety")]
    public void Out_of_range_soc_is_substituted(double socPercent)
    {
        var store = new InMemorySnapshotStore(TimeSpan.FromSeconds(10));
        store.Update(TestFixtures.CreateTelemetry(socPercent: socPercent), TestFixtures.Now);

        var snapshot = store.GetLatest("asset-1", TestFixtures.Now);

        Assert.NotNull(snapshot);
        Assert.Equal(DataQualityState.Substituted, snapshot!.Quality.Flag);
        Assert.Equal("soc-out-of-range", snapshot.Quality.Reason);
    }

    [Fact]
    [Trait("Category", "Safety")]
    public void Non_finite_active_power_is_protocol_error()
    {
        var store = new InMemorySnapshotStore(TimeSpan.FromSeconds(10));
        store.Update(TestFixtures.CreateTelemetry(activePowerKw: double.NaN), TestFixtures.Now);

        var snapshot = store.GetLatest("asset-1", TestFixtures.Now);

        Assert.NotNull(snapshot);
        Assert.Equal(DataQualityState.ProtocolError, snapshot!.Quality.Flag);
        Assert.Equal("active-power-not-finite", snapshot.Quality.Reason);
    }

    [Fact]
    [Trait("Category", "Safety")]
    public void Non_finite_soc_is_protocol_error()
    {
        // RM-M3-05 Managed-Precheck-Gap: NaN slips past `is < 0 or
        // > 100` because every NaN comparison returns false. The
        // store must catch it explicitly so the cycle never feeds
        // a NaN SOC into Constraint or the native kernel.
        var store = new InMemorySnapshotStore(TimeSpan.FromSeconds(10));
        store.Update(TestFixtures.CreateTelemetry(socPercent: double.NaN), TestFixtures.Now);

        var snapshot = store.GetLatest("asset-1", TestFixtures.Now);

        Assert.NotNull(snapshot);
        Assert.Equal(DataQualityState.ProtocolError, snapshot!.Quality.Flag);
        Assert.Equal("soc-not-finite", snapshot.Quality.Reason);
    }

    [Fact]
    [Trait("Category", "Safety")]
    public void Non_finite_soh_is_protocol_error()
    {
        var store = new InMemorySnapshotStore(TimeSpan.FromSeconds(10));
        store.Update(TestFixtures.CreateTelemetry(sohPercent: double.PositiveInfinity), TestFixtures.Now);

        var snapshot = store.GetLatest("asset-1", TestFixtures.Now);

        Assert.NotNull(snapshot);
        Assert.Equal(DataQualityState.ProtocolError, snapshot!.Quality.Flag);
        Assert.Equal("soh-not-finite", snapshot.Quality.Reason);
    }

    [Fact]
    [Trait("Category", "Safety")]
    public void Non_finite_temperature_is_protocol_error()
    {
        // ConstraintLimiter would forward NaN through the kernel
        // and produce non-finite outputs; the precheck rejects it
        // upfront.
        var store = new InMemorySnapshotStore(TimeSpan.FromSeconds(10));
        store.Update(TestFixtures.CreateTelemetry(temperatureCelsius: double.NaN), TestFixtures.Now);

        var snapshot = store.GetLatest("asset-1", TestFixtures.Now);

        Assert.NotNull(snapshot);
        Assert.Equal(DataQualityState.ProtocolError, snapshot!.Quality.Flag);
        Assert.Equal("temperature-not-finite", snapshot.Quality.Reason);
    }

    [Fact]
    public void Update_overwrites_previous_snapshot()
    {
        var store = new InMemorySnapshotStore(TimeSpan.FromSeconds(10));
        store.Update(TestFixtures.CreateTelemetry(socPercent: 30), TestFixtures.Now);
        store.Update(TestFixtures.CreateTelemetry(socPercent: 60), TestFixtures.Now + TimeSpan.FromSeconds(1));

        var snapshot = store.GetLatest("asset-1", TestFixtures.Now + TimeSpan.FromSeconds(2));

        Assert.NotNull(snapshot);
        Assert.Equal(60, snapshot!.Telemetry.SocPercent);
    }

    [Fact]
    public void Constructor_rejects_non_positive_max_age()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemorySnapshotStore(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemorySnapshotStore(TimeSpan.FromSeconds(-1)));
    }
}
