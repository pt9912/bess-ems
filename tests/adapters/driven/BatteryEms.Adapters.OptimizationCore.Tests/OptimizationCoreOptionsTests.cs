using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.Adapters.OptimizationCore.Tests;

// Plan-RM-M5-01-A Test-Pins für EnsureValid + Defaults.
public sealed class OptimizationCoreOptionsTests
{
    private static readonly NullLogger<OptimizationCoreOptions> Logger =
        NullLogger<OptimizationCoreOptions>.Instance;

    [Fact]
    public void Defaults_pin_master_dod_values()
    {
        var options = new OptimizationCoreOptions
        {
            SidecarEndpoint = new Uri("unix:///var/run/test.sock"),
        };

        Assert.Equal(OptimizationCoreRuntimeProfile.Production, options.RuntimeProfile);
        Assert.Equal(TimeSpan.FromSeconds(60), options.RequestDeadline);
        Assert.Equal(TimeSpan.FromSeconds(10), options.ConnectTimeout);
        Assert.Equal(TimeSpan.Zero, options.MaxFallbackScheduleAge);
        Assert.Equal("1.0.0", options.ExpectedContractVersion);
        Assert.Contains("has-usable-solution", options.RequiredFeatures);
        Assert.Null(options.ClientCertificatePath);
        Assert.Null(options.TrustedServerCertificatesPath);
        Assert.Null(options.BearerTokenPath);
    }

    // D-02: Production-Profile macht plaintext-TCP unwirksam.
    [Fact]
    public void Production_profile_with_plaintext_http_throws()
    {
        var options = new OptimizationCoreOptions
        {
            SidecarEndpoint = new Uri("http://localhost:5001"),
            RuntimeProfile = OptimizationCoreRuntimeProfile.Production,
        };

        var ex = Assert.Throws<InvalidOperationException>(() => options.EnsureValid(Logger));
        Assert.Contains(
            "optimization-core-not-hardened-in-production",
            ex.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Production_profile_with_uds_endpoint_passes()
    {
        var options = new OptimizationCoreOptions
        {
            SidecarEndpoint = new Uri("unix:///var/run/bess-ems/optimization-core.sock"),
            RuntimeProfile = OptimizationCoreRuntimeProfile.Production,
        };

        var result = options.EnsureValid(Logger);

        Assert.Same(options, result);
    }

    [Fact]
    public void Production_profile_with_https_endpoint_passes()
    {
        var options = new OptimizationCoreOptions
        {
            SidecarEndpoint = new Uri("https://optimization-core.internal:8443"),
            RuntimeProfile = OptimizationCoreRuntimeProfile.Production,
        };

        var result = options.EnsureValid(Logger);

        Assert.Same(options, result);
    }

    [Theory]
    [InlineData(OptimizationCoreRuntimeProfile.HilSimulator)]
    [InlineData(OptimizationCoreRuntimeProfile.Development)]
    public void Test_profile_with_plaintext_http_passes(OptimizationCoreRuntimeProfile profile)
    {
        var options = new OptimizationCoreOptions
        {
            SidecarEndpoint = new Uri("http://localhost:5001"),
            RuntimeProfile = profile,
        };

        var result = options.EnsureValid(Logger);

        Assert.Same(options, result);
    }

    [Fact]
    public void Null_endpoint_throws()
    {
        var options = new OptimizationCoreOptions
        {
            SidecarEndpoint = null!,
            RuntimeProfile = OptimizationCoreRuntimeProfile.HilSimulator,
        };

        Assert.Throws<ArgumentNullException>(() => options.EnsureValid(Logger));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_connect_timeout_throws(int seconds)
    {
        var options = new OptimizationCoreOptions
        {
            SidecarEndpoint = new Uri("http://localhost:5001"),
            RuntimeProfile = OptimizationCoreRuntimeProfile.HilSimulator,
            ConnectTimeout = TimeSpan.FromSeconds(seconds),
        };

        Assert.Throws<InvalidOperationException>(() => options.EnsureValid(Logger));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_request_deadline_throws(int seconds)
    {
        var options = new OptimizationCoreOptions
        {
            SidecarEndpoint = new Uri("http://localhost:5001"),
            RuntimeProfile = OptimizationCoreRuntimeProfile.HilSimulator,
            RequestDeadline = TimeSpan.FromSeconds(seconds),
        };

        Assert.Throws<InvalidOperationException>(() => options.EnsureValid(Logger));
    }

    [Fact]
    public void Negative_max_fallback_age_throws()
    {
        var options = new OptimizationCoreOptions
        {
            SidecarEndpoint = new Uri("http://localhost:5001"),
            RuntimeProfile = OptimizationCoreRuntimeProfile.HilSimulator,
            MaxFallbackScheduleAge = TimeSpan.FromSeconds(-1),
        };

        Assert.Throws<InvalidOperationException>(() => options.EnsureValid(Logger));
    }

    [Fact]
    public void Empty_expected_contract_version_throws_with_contract_incompatible_reason()
    {
        var options = new OptimizationCoreOptions
        {
            SidecarEndpoint = new Uri("http://localhost:5001"),
            RuntimeProfile = OptimizationCoreRuntimeProfile.HilSimulator,
            ExpectedContractVersion = "",
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => options.EnsureValid(Logger));
        Assert.Contains(
            "optimization-core-contract-incompatible",
            ex.Message,
            StringComparison.Ordinal);
    }

    // Plan-RM-M5-01 §6 Akzeptanzkriterium: EnsureValid wirft den
    // `optimization-core-contract-incompatible`-Reason auch wenn der
    // Operator einen nicht-parsbaren Versions-String setzt (z. B.
    // Tippfehler `"v1"` statt `"1.0.0"`).
    [Theory]
    [InlineData("abc")]
    [InlineData("1")]
    [InlineData("v1.0.0")]
    [InlineData("1.0.0.0.0")]
    public void Non_semver_expected_contract_version_throws_with_contract_incompatible_reason(
        string version)
    {
        var options = new OptimizationCoreOptions
        {
            SidecarEndpoint = new Uri("http://localhost:5001"),
            RuntimeProfile = OptimizationCoreRuntimeProfile.HilSimulator,
            ExpectedContractVersion = version,
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => options.EnsureValid(Logger));
        Assert.Contains(
            "optimization-core-contract-incompatible",
            ex.Message,
            StringComparison.Ordinal);
    }

    // SemVer-Pre-Release-Suffixe sind erlaubt (analog zur Wire-Side-
    // Versions-Range-Check-Logik im OptimizationCoreScheduleOptimizer).
    [Theory]
    [InlineData("1.0.0-rc.1")]
    [InlineData("1.2.3-alpha")]
    [InlineData("2.0.0-beta.5")]
    public void Semver_prerelease_expected_contract_version_passes(string version)
    {
        var options = new OptimizationCoreOptions
        {
            SidecarEndpoint = new Uri("http://localhost:5001"),
            RuntimeProfile = OptimizationCoreRuntimeProfile.HilSimulator,
            ExpectedContractVersion = version,
        };

        var validated = options.EnsureValid(Logger);
        Assert.Equal(version, validated.ExpectedContractVersion);
    }

    [Fact]
    public void Null_logger_throws()
    {
        var options = new OptimizationCoreOptions
        {
            SidecarEndpoint = new Uri("http://localhost:5001"),
            RuntimeProfile = OptimizationCoreRuntimeProfile.HilSimulator,
        };

        Assert.Throws<ArgumentNullException>(() => options.EnsureValid(null!));
    }
}
