using BatteryEms.Adapters.NativeInterop;
using BatteryEms.Application.Control;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.Adapters.NativeInterop.Tests;

// M3-D2 unit tests for the DI extension. Each path of the
// load-result classifier (`Disabled` / `LibraryMissing` /
// `LoadFailed` / `AbiMismatch` / `Loaded`) must produce a
// deterministic IControlKernel registration plus the documented
// abort-policy semantics. The tests bypass the production loader
// gateway by overriding the host config — the loader's own
// fake-gateway tests live in NativeControlLoaderTests; here we
// pin the DI surface that calls them.
public sealed class NativeInteropRegistrationTests
{
    [Fact]
    public void Default_section_missing_registers_ManagedControlKernel()
    {
        // No NativeControl section in the configuration → options
        // default-construct (Enabled=false), the loader returns
        // Disabled, the extension registers the managed kernel.
        var services = BuildServicesWithConfig(new Dictionary<string, string?>());

        var resolved = services.GetRequiredService<IControlKernel>();

        Assert.IsType<ManagedControlKernel>(resolved);
    }

    [Fact]
    public void Section_with_Enabled_false_registers_ManagedControlKernel()
    {
        // Explicit disabled — same result as the missing-section path,
        // pinned separately so a future config-binder change can't
        // silently flip the default.
        var services = BuildServicesWithConfig(new Dictionary<string, string?>
        {
            ["NativeControl:Enabled"] = "false",
        });

        var resolved = services.GetRequiredService<IControlKernel>();

        Assert.IsType<ManagedControlKernel>(resolved);
    }

    [Fact]
    public void Enabled_with_missing_library_falls_back_to_ManagedControlKernel()
    {
        // The default policy from `quality.md` §5.2: a Native-opt-in
        // host with a wrong/missing library path falls back to managed
        // (no startup abort). The loader emits a LibraryMissing log
        // event so operators can diagnose; the host stays up.
        var services = BuildServicesWithConfig(new Dictionary<string, string?>
        {
            ["NativeControl:Enabled"] = "true",
            ["NativeControl:LibraryPath"] = "/nonexistent/path/libfake.so",
        });

        var resolved = services.GetRequiredService<IControlKernel>();

        Assert.IsType<ManagedControlKernel>(resolved);
    }

    [Fact]
    public void Enabled_with_missing_library_and_AbortOnAbiMismatch_still_falls_back()
    {
        // The abort policy is specifically about ABI mismatch, NOT
        // about a missing library. This pin keeps the policy narrow:
        // a missing library is not an ABI lie, so the abort flag
        // does not promote it to a hard startup error.
        var services = BuildServicesWithConfig(new Dictionary<string, string?>
        {
            ["NativeControl:Enabled"] = "true",
            ["NativeControl:LibraryPath"] = "/nonexistent/path/libfake.so",
            ["NativeControl:AbortOnAbiMismatch"] = "true",
        });

        var resolved = services.GetRequiredService<IControlKernel>();

        Assert.IsType<ManagedControlKernel>(resolved);
    }

    [Fact]
    public void Singleton_lifetime_returns_same_instance_across_resolves()
    {
        // The Native-Loader path is expensive (dlopen + ABI export
        // resolution); the DI factory must run exactly once per
        // host instance regardless of how many consumers resolve
        // IControlKernel.
        var services = BuildServicesWithConfig(new Dictionary<string, string?>());

        var first = services.GetRequiredService<IControlKernel>();
        var second = services.GetRequiredService<IControlKernel>();

        Assert.Same(first, second);
    }

    private static IServiceProvider BuildServicesWithConfig(
        IDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddBessNativeControl(configuration);
        return services.BuildServiceProvider();
    }
}
