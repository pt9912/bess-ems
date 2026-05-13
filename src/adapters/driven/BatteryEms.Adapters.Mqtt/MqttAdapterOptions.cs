using Microsoft.Extensions.Logging;

namespace BatteryEms.Adapters.Mqtt;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record MqttAdapterOptions(
    string BrokerHost,
    int BrokerPort,
    string ClientId,
    string AssetId,
    TimeSpan ConnectTimeout,
    TimeSpan CommandAckTimeout,
    MqttQosOptions? QoS = null,
    MqttRuntimeProfile RuntimeProfile = MqttRuntimeProfile.Production,
    MqttTlsOptions? Tls = null,
    MqttCredentialOptions? Credentials = null,
    bool AllowPlaintext = false,
    string? AllowPlaintextReason = null)
{
    public MqttQosOptions QoSOrDefault => QoS ?? MqttQosOptions.Defaults;

    public MqttTlsOptions TlsOrDefault => Tls ?? new MqttTlsOptions();

    public MqttCredentialOptions CredentialsOrDefault => Credentials ?? new MqttCredentialOptions();

    public static MqttAdapterOptions Defaults(string brokerHost, int brokerPort, string clientId, string assetId) => new(
        BrokerHost: brokerHost,
        BrokerPort: brokerPort,
        ClientId: clientId,
        AssetId: assetId,
        ConnectTimeout: TimeSpan.FromSeconds(5),
        CommandAckTimeout: TimeSpan.FromSeconds(2),
        QoS: MqttQosOptions.Defaults);

    public MqttAdapterOptions EnsureValid(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ValidateBasicFields();

        if (!TlsOrDefault.Enabled)
        {
            ValidatePlaintextProfile();
            MqttAdapterOptionsLog.LogPlaintextMqttConnection(logger, RuntimeProfile, BrokerHost, BrokerPort, AllowPlaintextReason!);
            return this;
        }

        ValidateTlsProfile(TlsOrDefault, CredentialsOrDefault);
        MqttAdapterOptionsLog.LogSecureMqttConnection(logger, RuntimeProfile, BrokerHost, BrokerPort);
        return this;
    }

    private void ValidateBasicFields()
    {
        if (string.IsNullOrWhiteSpace(BrokerHost))
        {
            throw new InvalidOperationException("Mqtt BrokerHost must be set.");
        }
        if (BrokerPort <= 0)
        {
            throw new InvalidOperationException($"Mqtt BrokerPort must be positive (got {BrokerPort}).");
        }
        if (string.IsNullOrWhiteSpace(ClientId))
        {
            throw new InvalidOperationException("Mqtt ClientId must be set.");
        }
        if (string.IsNullOrWhiteSpace(AssetId))
        {
            throw new InvalidOperationException("Mqtt AssetId must be set.");
        }
        if (ConnectTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"Mqtt ConnectTimeout must be positive (got {ConnectTimeout}).");
        }
        if (CommandAckTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"Mqtt CommandAckTimeout must be positive (got {CommandAckTimeout}).");
        }
    }

    private void ValidatePlaintextProfile()
    {
        if (RuntimeProfile == MqttRuntimeProfile.Production)
        {
            throw new InvalidOperationException(
                "mqtt-security-not-hardened-in-production: "
                + "RuntimeProfile=Production requires Mqtt Tls.Enabled=true. "
                + "Plaintext MQTT is allowed only in Development or HilSimulator "
                + "with AllowPlaintext=true plus AllowPlaintextReason.");
        }
        if (!AllowPlaintext || string.IsNullOrWhiteSpace(AllowPlaintextReason))
        {
            throw new InvalidOperationException(
                "mqtt-plaintext-not-acknowledged: plaintext MQTT requires "
                + "AllowPlaintext=true plus a non-empty AllowPlaintextReason "
                + "in Development/HilSimulator profiles.");
        }
    }

    private void ValidateTlsProfile(
        MqttTlsOptions tls,
        MqttCredentialOptions credentials)
    {
        if (AllowPlaintext)
        {
            throw new InvalidOperationException(
                "mqtt-allow-plaintext-with-tls-inconsistent: "
                + "AllowPlaintext=true cannot be combined with Tls.Enabled=true.");
        }
        if (string.IsNullOrWhiteSpace(tls.TrustedCaCertificatePath))
        {
            throw new InvalidOperationException(
                "mqtt-trusted-ca-required: Tls.Enabled=true requires "
                + "Tls.TrustedCaCertificatePath so broker trust does not fall "
                + "back to the system-default root set.");
        }
        if (!File.Exists(tls.TrustedCaCertificatePath))
        {
            throw new InvalidOperationException(
                $"mqtt-trusted-ca-not-found: '{tls.TrustedCaCertificatePath}' does not exist.");
        }

        if (!string.IsNullOrWhiteSpace(tls.ClientCertificatePath)
            && !File.Exists(tls.ClientCertificatePath))
        {
            throw new InvalidOperationException(
                $"mqtt-client-certificate-not-found: '{tls.ClientCertificatePath}' does not exist.");
        }

        ValidateProductionAuth(tls, credentials);
        ValidateSecretFiles(tls, credentials);
    }

    private void ValidateProductionAuth(
        MqttTlsOptions tls,
        MqttCredentialOptions credentials)
    {
        if (RuntimeProfile != MqttRuntimeProfile.Production)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(credentials.Password))
        {
            throw new InvalidOperationException(
                "mqtt-inline-password-not-allowed-in-production: "
                + "use MqttPasswordPath or a secret-mounted file instead.");
        }
        if (!string.IsNullOrWhiteSpace(tls.ClientCertificatePassword))
        {
            throw new InvalidOperationException(
                "mqtt-inline-client-certificate-password-not-allowed-in-production: "
                + "use MqttTlsClientCertificatePasswordPath instead.");
        }

        var hasUsernamePassword = !string.IsNullOrWhiteSpace(credentials.Username)
            && !string.IsNullOrWhiteSpace(credentials.PasswordPath);
        var hasClientCertificate = !string.IsNullOrWhiteSpace(tls.ClientCertificatePath);
        if (!hasUsernamePassword && !hasClientCertificate)
        {
            throw new InvalidOperationException(
                "mqtt-broker-auth-required-in-production: "
                + "RuntimeProfile=Production requires Username+PasswordPath "
                + "or a client certificate.");
        }
    }

    private static void ValidateSecretFiles(
        MqttTlsOptions tls,
        MqttCredentialOptions credentials)
    {
        if (!string.IsNullOrWhiteSpace(credentials.Username)
            && string.IsNullOrWhiteSpace(credentials.Password)
            && string.IsNullOrWhiteSpace(credentials.PasswordPath))
        {
            throw new InvalidOperationException(
                "mqtt-password-required: Username requires Password or PasswordPath.");
        }
        if (!string.IsNullOrWhiteSpace(credentials.PasswordPath)
            && !File.Exists(credentials.PasswordPath))
        {
            throw new InvalidOperationException(
                $"mqtt-password-file-not-found: '{credentials.PasswordPath}' does not exist.");
        }
        if (!string.IsNullOrWhiteSpace(tls.ClientCertificatePasswordPath)
            && !File.Exists(tls.ClientCertificatePasswordPath))
        {
            throw new InvalidOperationException(
                $"mqtt-client-certificate-password-file-not-found: '{tls.ClientCertificatePasswordPath}' does not exist.");
        }
    }
}

internal static partial class MqttAdapterOptionsLog
{
    [LoggerMessage(EventId = 4320, Level = LogLevel.Warning,
        Message = "mqtt adapter starting plaintext in {RuntimeProfile} against {BrokerHost}:{BrokerPort}: {Reason}")]
    public static partial void LogPlaintextMqttConnection(
        ILogger logger,
        MqttRuntimeProfile runtimeProfile,
        string brokerHost,
        int brokerPort,
        string reason);

    [LoggerMessage(EventId = 4321, Level = LogLevel.Information,
        Message = "mqtt adapter starting with TLS profile {RuntimeProfile} against {BrokerHost}:{BrokerPort}")]
    public static partial void LogSecureMqttConnection(
        ILogger logger,
        MqttRuntimeProfile runtimeProfile,
        string brokerHost,
        int brokerPort);
}
