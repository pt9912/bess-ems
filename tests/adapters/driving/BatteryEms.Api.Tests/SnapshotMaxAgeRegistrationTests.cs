using BatteryEms.Api.Composition;
using BatteryEms.Application.Realtime;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BatteryEms.Api.Tests;

// ADR 0013 §5.1 sub-slice 5: the snapshot freshness window is configurable via
// Bess:SnapshotMaxAge (default 10s), threaded through AddBessApplicationInMemoryStores.
public sealed class SnapshotMaxAgeRegistrationTests
{
    [Fact]
    public void Configured_snapshot_max_age_is_honored()
    {
        var services = new ServiceCollection();
        services.AddBessApplicationInMemoryStores(TimeSpan.FromSeconds(15));
        using var provider = services.BuildServiceProvider();

        var store = Assert.IsType<InMemorySnapshotStore>(provider.GetRequiredService<ISnapshotStore>());
        Assert.Equal(TimeSpan.FromSeconds(15), store.MaxAge);
    }

    [Fact]
    public void Default_snapshot_max_age_is_the_shared_ten_second_default()
    {
        var services = new ServiceCollection();
        services.AddBessApplicationInMemoryStores();
        using var provider = services.BuildServiceProvider();

        var store = Assert.IsType<InMemorySnapshotStore>(provider.GetRequiredService<ISnapshotStore>());
        Assert.Equal(ApplicationServiceRegistration.DefaultSnapshotMaxAge, store.MaxAge);
        Assert.Equal(TimeSpan.FromSeconds(10), store.MaxAge);
    }
}
