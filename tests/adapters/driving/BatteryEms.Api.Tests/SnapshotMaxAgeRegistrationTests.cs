using System.Collections.Generic;
using BatteryEms.Api.Composition;
using BatteryEms.Application.Realtime;
using Microsoft.Extensions.Configuration;
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

    // The call-sites (Program.cs / BessHostBuilder.cs) resolve the window via
    // IConfiguration.GetValue<TimeSpan>("Bess:SnapshotMaxAge", …). Exercise that
    // string->TimeSpan path end-to-end — the mechanism a `Bess__SnapshotMaxAge`
    // env var flows through. The key is documented as hh:mm:ss in appsettings.json
    // because a bare "15" would parse to 15 DAYS, silently defeating snapshot ageing.
    [Fact]
    public void Bess_snapshot_max_age_key_binds_from_configuration_string()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bess:SnapshotMaxAge"] = "00:00:15",
            })
            .Build();

        var resolved = config.GetValue("Bess:SnapshotMaxAge", ApplicationServiceRegistration.DefaultSnapshotMaxAge);
        Assert.Equal(TimeSpan.FromSeconds(15), resolved);

        var services = new ServiceCollection();
        services.AddBessApplicationInMemoryStores(resolved);
        using var provider = services.BuildServiceProvider();

        var store = Assert.IsType<InMemorySnapshotStore>(provider.GetRequiredService<ISnapshotStore>());
        Assert.Equal(TimeSpan.FromSeconds(15), store.MaxAge);
    }

    [Fact]
    public void Missing_configuration_key_falls_back_to_the_shared_default()
    {
        var config = new ConfigurationBuilder().Build();

        var resolved = config.GetValue("Bess:SnapshotMaxAge", ApplicationServiceRegistration.DefaultSnapshotMaxAge);

        Assert.Equal(ApplicationServiceRegistration.DefaultSnapshotMaxAge, resolved);
    }
}
