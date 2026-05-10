using BatteryEms.Application.Markets;
using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Application.Tests;

public sealed class ProductionPreconditionProviderTests
{
    private static (
        DefaultProductionPreconditionProvider Provider,
        InMemoryTimebaseHealthSource Timebase,
        InMemoryActivationDedupeStore Dedupe) BuildProvider()
    {
        var clock = new FakeClock();
        var options = new RegelleistungOptions();
        var timebase = new InMemoryTimebaseHealthSource();
        var dedupe = new InMemoryActivationDedupeStore(options, clock);
        return (new DefaultProductionPreconditionProvider(timebase, dedupe), timebase, dedupe);
    }

    [Fact]
    public void Default_provider_fails_on_product_trust_when_not_established()
    {
        var (provider, _, _) = BuildProvider();
        var options = new RegelleistungOptions { ProductTrustEstablished = false };

        var status = provider.Evaluate(options);

        Assert.False(status.IsGreen);
        Assert.False(status.ProductTrust);
        Assert.Equal(ActivationValidationReasons.NotDispatchRelevant, status.ReasonCode);
    }

    [Fact]
    public void Default_provider_fails_on_timebase_when_degraded()
    {
        var (provider, timebase, _) = BuildProvider();
        timebase.Observe(true);
        timebase.Observe(true);
        timebase.Observe(true);
        Assert.Equal(TimebaseHealth.Degraded, timebase.Current.Health);

        var status = provider.Evaluate(new RegelleistungOptions { ProductTrustEstablished = true });

        Assert.False(status.IsGreen);
        Assert.False(status.TimeSync);
        Assert.Equal(ActivationValidationReasons.TimebaseDegraded, status.ReasonCode);
    }

    [Fact]
    public void Default_provider_fails_on_dedupe_when_invalid()
    {
        var (provider, _, dedupe) = BuildProvider();
        dedupe.MarkInvalid();

        var status = provider.Evaluate(new RegelleistungOptions { ProductTrustEstablished = true });

        Assert.False(status.IsGreen);
        Assert.False(status.DedupeStoreHealth);
        Assert.Equal(ActivationValidationReasons.DedupeStoreInvalid, status.ReasonCode);
    }

    // Plan §147 / D-03: even with the first three checks green, the
    // production-code provider keeps the gate closed via security-
    // profile-enforcement-not-wired until F-12 wires a real signal.
    [Fact]
    public void Default_provider_fail_closed_on_security_profile_when_first_three_green()
    {
        var (provider, _, _) = BuildProvider();

        var status = provider.Evaluate(new RegelleistungOptions { ProductTrustEstablished = true });

        Assert.False(status.IsGreen);
        Assert.True(status.ProductTrust);
        Assert.True(status.TimeSync);
        Assert.True(status.DedupeStoreHealth);
        Assert.False(status.SecurityProfile);
        Assert.Equal(
            ActivationValidationReasons.SecurityProfileEnforcementNotWired,
            status.ReasonCode);
    }

    [Fact]
    public void Healthy_provider_returns_all_green()
    {
        var provider = new HealthyProductionPreconditionProvider();

        var status = provider.Evaluate(new RegelleistungOptions { ProductTrustEstablished = true });

        Assert.True(status.IsGreen);
        Assert.Equal(ActivationValidationReasons.Accepted, status.ReasonCode);
    }

    [Fact]
    public void Healthy_provider_null_options_throws()
    {
        var provider = new HealthyProductionPreconditionProvider();
        Assert.Throws<ArgumentNullException>(() => provider.Evaluate(null!));
    }

    [Fact]
    public void Default_provider_null_options_throws()
    {
        var (provider, _, _) = BuildProvider();
        Assert.Throws<ArgumentNullException>(() => provider.Evaluate(null!));
    }

    [Fact]
    public void Default_provider_null_timebase_throws()
    {
        var clock = new FakeClock();
        var dedupe = new InMemoryActivationDedupeStore(new RegelleistungOptions(), clock);
        Assert.Throws<ArgumentNullException>(() =>
            new DefaultProductionPreconditionProvider(null!, dedupe));
    }

    [Fact]
    public void Default_provider_null_dedupe_throws()
    {
        var timebase = new InMemoryTimebaseHealthSource();
        Assert.Throws<ArgumentNullException>(() =>
            new DefaultProductionPreconditionProvider(timebase, null!));
    }
}
