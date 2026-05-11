using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.Adapters.OpcUa.Tests;

public sealed class OpcUaAdapterOptionsTests
{
    private static readonly NullLogger<OpcUaAdapterOptions> Logger =
        NullLogger<OpcUaAdapterOptions>.Instance;

    // M4-05-A: Test-Helper für den HilSimulator-None-Pfad (Pre-M4-05-
    // Verhalten bleibt erhalten für Test-Profile). Im Production-Default
    // würde `SecurityMode=None` werfen — D-02.
    private static OpcUaAdapterOptions ValidUnsecuredOptions() => new()
    {
        EndpointUrl = new Uri("opc.tcp://localhost:4840"),
        RuntimeProfile = OpcUaRuntimeProfile.HilSimulator,
        SecurityMode = OpcUaSecurityMode.None,
        AllowUnsecured = true,
        AllowUnsecuredReason = "hil-simulator-pre-m4-05",
    };

    // M4-05-A: Default-Pin. Mit dem D-03-Schwenk sind die Defaults jetzt
    // Production + SignAndEncrypt + Basic256Sha256. Eine Operator-
    // Konfiguration ohne explizite Profile/Mode/Policy-Wahl bekommt den
    // sicheren Default.
    [Fact]
    public void Defaults_pin_master_dod_values()
    {
        var options = new OpcUaAdapterOptions
        {
            EndpointUrl = new Uri("opc.tcp://localhost:4840"),
        };

        Assert.Equal("bess-ems", options.SessionName);
        Assert.Equal(TimeSpan.FromSeconds(15), options.ConnectTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), options.ReadTimeout);
        Assert.Equal(TimeSpan.FromSeconds(10), options.KeepAliveInterval);
        Assert.Equal(TimeSpan.FromSeconds(1), options.ReconnectBackoffStart);
        Assert.Equal(TimeSpan.FromSeconds(30), options.ReconnectBackoffMax);
        Assert.Equal(1000, options.DefaultMonitoringIntervalMs);
        Assert.Equal(256, options.SubscriptionChannelCapacity);
        Assert.Equal(OpcUaRuntimeProfile.Production, options.RuntimeProfile);
        Assert.Equal(OpcUaSecurityMode.SignAndEncrypt, options.SecurityMode);
        Assert.Equal(OpcUaSecurityPolicies.Basic256Sha256, options.SecurityPolicy);
        Assert.False(options.AllowUnsecured);
        Assert.Null(options.AllowUnsecuredReason);
        Assert.Null(options.ApplicationCertificateSubject);
        Assert.Null(options.TrustedServerCertificatesPath);
    }

    // M4-05-A: ein Operator, der nichts überschreibt, bekommt den
    // Production-Secure-Pfad — EnsureValid lässt durch und logt
    // EventId 4221 (Profile-Established) plus 4222 (Allowlisted).
    [Fact]
    public void Default_options_pass_ensure_valid_and_log_secure_profile()
    {
        var spy = new SpyLogger();
        var options = new OpcUaAdapterOptions
        {
            EndpointUrl = new Uri("opc.tcp://localhost:4840"),
        };

        var result = options.EnsureValid(spy);

        Assert.Same(options, result);
        Assert.Equal(2, spy.Records.Count);
        Assert.Equal(4221, spy.Records[0].EventId);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Information, spy.Records[0].Level);
        Assert.Equal(4222, spy.Records[1].EventId);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Information, spy.Records[1].Level);
    }

    [Fact]
    public void Null_endpoint_url_throws()
    {
        var options = new OpcUaAdapterOptions
        {
            EndpointUrl = null!,
            RuntimeProfile = OpcUaRuntimeProfile.HilSimulator,
            SecurityMode = OpcUaSecurityMode.None,
            AllowUnsecured = true,
            AllowUnsecuredReason = "test",
        };

        Assert.Throws<ArgumentNullException>(() => options.EnsureValid(Logger));
    }

    // M4-05-A D-02 Pin (i): Production+None+AllowUnsecured=false throws
    // opcua-security-not-hardened-in-production (the Production-Profile
    // doesn't allow None at all).
    [Fact]
    public void Production_profile_with_unsecured_mode_throws_without_allowunsecured()
    {
        var options = new OpcUaAdapterOptions
        {
            EndpointUrl = new Uri("opc.tcp://localhost:4840"),
            RuntimeProfile = OpcUaRuntimeProfile.Production,
            SecurityMode = OpcUaSecurityMode.None,
            // AllowUnsecured=false (default)
        };

        var ex = Assert.Throws<InvalidOperationException>(() => options.EnsureValid(Logger));
        Assert.Contains(
            "opcua-security-not-hardened-in-production",
            ex.Message,
            StringComparison.Ordinal);
    }

    // M4-05-A D-02 Pin (ii): Production+None+AllowUnsecured=true STILL
    // throws — the bool-axis override is bewusst nicht ausreichend im
    // Production-Profile (Master-DoD-Konsequenz).
    [Fact]
    public void Production_profile_with_unsecured_mode_throws_even_with_allowunsecured()
    {
        var options = new OpcUaAdapterOptions
        {
            EndpointUrl = new Uri("opc.tcp://localhost:4840"),
            RuntimeProfile = OpcUaRuntimeProfile.Production,
            SecurityMode = OpcUaSecurityMode.None,
            AllowUnsecured = true,
            AllowUnsecuredReason = "operator-tried-to-bypass",
        };

        var ex = Assert.Throws<InvalidOperationException>(() => options.EnsureValid(Logger));
        Assert.Contains(
            "opcua-security-not-hardened-in-production",
            ex.Message,
            StringComparison.Ordinal);
    }

    // M4-05-A Pin: HilSimulator+None+AllowUnsecured+Reason → passes
    // (Pre-M4-05-Verhalten bleibt für Test-Profile gültig).
    [Fact]
    public void Hil_simulator_profile_with_unsecured_mode_passes()
    {
        var spy = new SpyLogger();
        var options = new OpcUaAdapterOptions
        {
            EndpointUrl = new Uri("opc.tcp://localhost:4840"),
            RuntimeProfile = OpcUaRuntimeProfile.HilSimulator,
            SecurityMode = OpcUaSecurityMode.None,
            AllowUnsecured = true,
            AllowUnsecuredReason = "hil-simulator-pre-m4-05",
        };

        var result = options.EnsureValid(spy);

        Assert.Same(options, result);
        Assert.Single(spy.Records);
        Assert.Equal(4200, spy.Records[0].EventId);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, spy.Records[0].Level);
    }

    // M4-05-A Pin: Production+SignAndEncrypt+Basic128Rsa15 throws
    // opcua-security-policy-not-allowlisted. Die Allowlist enthält
    // heute nur Basic256Sha256.
    [Theory]
    [InlineData("Basic128Rsa15")]
    [InlineData("http://opcfoundation.org/UA/SecurityPolicy#Basic128Rsa15")]
    [InlineData("http://opcfoundation.org/UA/SecurityPolicy#Aes128Sha256RsaOaep")]
    [InlineData("http://opcfoundation.org/UA/SecurityPolicy#Basic256")]
    public void Non_allowlisted_security_policy_throws(string policy)
    {
        var options = new OpcUaAdapterOptions
        {
            EndpointUrl = new Uri("opc.tcp://localhost:4840"),
            RuntimeProfile = OpcUaRuntimeProfile.Production,
            SecurityMode = OpcUaSecurityMode.SignAndEncrypt,
            SecurityPolicy = policy,
        };

        var ex = Assert.Throws<InvalidOperationException>(() => options.EnsureValid(Logger));
        Assert.Contains(
            "opcua-security-policy-not-allowlisted",
            ex.Message,
            StringComparison.Ordinal);
    }

    // M4-05-A Pin: SignAndEncrypt+AllowUnsecured=true → throws
    // opcua-allow-unsecured-with-secure-mode-inconsistent. Eine
    // Konfigurations-Inkonsistenz, kein operativ valider Pfad.
    [Fact]
    public void Secure_mode_with_allowunsecured_throws_inconsistent()
    {
        var options = new OpcUaAdapterOptions
        {
            EndpointUrl = new Uri("opc.tcp://localhost:4840"),
            RuntimeProfile = OpcUaRuntimeProfile.Production,
            SecurityMode = OpcUaSecurityMode.SignAndEncrypt,
            AllowUnsecured = true,
            AllowUnsecuredReason = "should-not-combine",
        };

        var ex = Assert.Throws<InvalidOperationException>(() => options.EnsureValid(Logger));
        Assert.Contains(
            "opcua-allow-unsecured-with-secure-mode-inconsistent",
            ex.Message,
            StringComparison.Ordinal);
    }

    // M4-05-A Pin (a) bestehender None-Pfad: AllowUnsecured=true + leerer
    // Reason → throws (Bool-Guard, M4-04 D-04). Bleibt für HilSimulator-
    // Profile gültig.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Allow_unsecured_without_reason_throws_opcua_security_not_hardened(string? reason)
    {
        var options = new OpcUaAdapterOptions
        {
            EndpointUrl = new Uri("opc.tcp://localhost:4840"),
            RuntimeProfile = OpcUaRuntimeProfile.HilSimulator,
            SecurityMode = OpcUaSecurityMode.None,
            AllowUnsecured = true,
            AllowUnsecuredReason = reason,
        };

        var ex = Assert.Throws<InvalidOperationException>(() => options.EnsureValid(Logger));
        Assert.Contains("opcua-security-not-hardened", ex.Message, StringComparison.Ordinal);
    }

    // M4-05-A Pin: Sign-Mode + Basic256Sha256 (Allowlist) passes.
    [Fact]
    public void Sign_mode_with_allowlisted_policy_passes()
    {
        var spy = new SpyLogger();
        var options = new OpcUaAdapterOptions
        {
            EndpointUrl = new Uri("opc.tcp://localhost:4840"),
            RuntimeProfile = OpcUaRuntimeProfile.Production,
            SecurityMode = OpcUaSecurityMode.Sign,
        };

        var result = options.EnsureValid(spy);

        Assert.Same(options, result);
        Assert.Equal(2, spy.Records.Count);
        Assert.Equal(4221, spy.Records[0].EventId);
        Assert.Equal(4222, spy.Records[1].EventId);
    }

    // M4-05-A Pin: OpcUaSecurityPolicies.IsAllowed
    [Fact]
    public void OpcUaSecurityPolicies_allowlist_pin()
    {
        Assert.True(OpcUaSecurityPolicies.IsAllowed(OpcUaSecurityPolicies.Basic256Sha256));
        Assert.False(OpcUaSecurityPolicies.IsAllowed("Basic128Rsa15"));
        Assert.False(OpcUaSecurityPolicies.IsAllowed(
            "http://opcfoundation.org/UA/SecurityPolicy#Basic128Rsa15"));
        Assert.False(OpcUaSecurityPolicies.IsAllowed(
            "http://opcfoundation.org/UA/SecurityPolicy#Aes256Sha256RsaPss"));
        Assert.False(OpcUaSecurityPolicies.IsAllowed(""));
        Assert.Throws<ArgumentNullException>(() => OpcUaSecurityPolicies.IsAllowed(null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_default_monitoring_interval_throws(int interval)
    {
        var options = ValidUnsecuredOptions() with { DefaultMonitoringIntervalMs = interval };
        Assert.Throws<InvalidOperationException>(() => options.EnsureValid(Logger));
    }

    [Fact]
    public void Reconnect_backoff_max_smaller_than_start_throws()
    {
        var options = ValidUnsecuredOptions() with
        {
            ReconnectBackoffStart = TimeSpan.FromSeconds(10),
            ReconnectBackoffMax = TimeSpan.FromSeconds(1),
        };
        Assert.Throws<InvalidOperationException>(() => options.EnsureValid(Logger));
    }

    [Fact]
    public void Null_logger_throws()
    {
        var options = ValidUnsecuredOptions();
        Assert.Throws<ArgumentNullException>(() => options.EnsureValid(null!));
    }

    private sealed class SpyLogger : Microsoft.Extensions.Logging.ILogger
    {
        public List<Record> Records { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
            => NullLogger.Instance.BeginScope(state)!;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Records.Add(new Record(logLevel, eventId.Id, formatter(state, exception)));
        }

        public sealed record Record(
            Microsoft.Extensions.Logging.LogLevel Level,
            int EventId,
            string Message);
    }
}
