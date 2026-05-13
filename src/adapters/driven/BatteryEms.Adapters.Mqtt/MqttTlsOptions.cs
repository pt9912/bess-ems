namespace BatteryEms.Adapters.Mqtt;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record MqttTlsOptions(
    bool Enabled = false,
    string? TrustedCaCertificatePath = null,
    string? ClientCertificatePath = null,
    string? ClientCertificatePassword = null,
    string? ClientCertificatePasswordPath = null);
