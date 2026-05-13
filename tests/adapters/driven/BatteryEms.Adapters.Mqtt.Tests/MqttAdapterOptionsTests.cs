using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.Adapters.Mqtt.Tests;

public sealed class MqttAdapterOptionsTests
{
    [Fact]
    public void Production_profile_rejects_plaintext_even_with_plaintext_ack()
    {
        var options = BaseOptions() with
        {
            RuntimeProfile = MqttRuntimeProfile.Production,
            AllowPlaintext = true,
            AllowPlaintextReason = "legacy broker",
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            options.EnsureValid(NullLogger.Instance));
        Assert.Contains("mqtt-security-not-hardened-in-production", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Hil_profile_rejects_plaintext_without_explicit_reason()
    {
        var options = BaseOptions() with
        {
            RuntimeProfile = MqttRuntimeProfile.HilSimulator,
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            options.EnsureValid(NullLogger.Instance));
        Assert.Contains("mqtt-plaintext-not-acknowledged", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Hil_profile_accepts_explicit_plaintext_ack()
    {
        var options = BaseOptions() with
        {
            RuntimeProfile = MqttRuntimeProfile.HilSimulator,
            AllowPlaintext = true,
            AllowPlaintextReason = "local simulator",
        };

        Assert.Same(options, options.EnsureValid(NullLogger.Instance));
    }

    [Fact]
    public void Tls_requires_configured_ca_bundle()
    {
        var options = BaseOptions() with
        {
            RuntimeProfile = MqttRuntimeProfile.Development,
            Tls = new MqttTlsOptions(Enabled: true),
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            options.EnsureValid(NullLogger.Instance));
        Assert.Contains("mqtt-trusted-ca-required", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_tls_requires_broker_auth()
    {
        var caPath = WriteTempFile("ca.pem", "not parsed by EnsureValid");
        var options = BaseOptions() with
        {
            RuntimeProfile = MqttRuntimeProfile.Production,
            Tls = new MqttTlsOptions(Enabled: true, TrustedCaCertificatePath: caPath),
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            options.EnsureValid(NullLogger.Instance));
        Assert.Contains("mqtt-broker-auth-required-in-production", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_tls_rejects_inline_password()
    {
        var caPath = WriteTempFile("ca.pem", "not parsed by EnsureValid");
        var options = BaseOptions() with
        {
            RuntimeProfile = MqttRuntimeProfile.Production,
            Tls = new MqttTlsOptions(Enabled: true, TrustedCaCertificatePath: caPath),
            Credentials = new MqttCredentialOptions(Username: "bess", Password: "secret"),
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            options.EnsureValid(NullLogger.Instance));
        Assert.Contains("mqtt-inline-password-not-allowed-in-production", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_tls_accepts_password_file()
    {
        var caPath = WriteTempFile("ca.pem", "not parsed by EnsureValid");
        var passwordPath = WriteTempFile("mqtt-password", "secret");
        var options = BaseOptions() with
        {
            RuntimeProfile = MqttRuntimeProfile.Production,
            Tls = new MqttTlsOptions(Enabled: true, TrustedCaCertificatePath: caPath),
            Credentials = new MqttCredentialOptions(Username: "bess", PasswordPath: passwordPath),
        };

        Assert.Same(options, options.EnsureValid(NullLogger.Instance));
    }

    private static MqttAdapterOptions BaseOptions() => MqttAdapterOptions.Defaults(
        brokerHost: "broker",
        brokerPort: 1883,
        clientId: "client",
        assetId: "asset-1");

    private static string WriteTempFile(string name, string contents)
    {
        var directory = Path.Combine(Path.GetTempPath(), "bess-ems-mqtt-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, contents);
        return path;
    }
}
