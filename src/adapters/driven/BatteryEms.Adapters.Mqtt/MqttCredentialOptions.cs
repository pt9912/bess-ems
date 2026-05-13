namespace BatteryEms.Adapters.Mqtt;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed record MqttCredentialOptions(
    string? Username = null,
    string? Password = null,
    string? PasswordPath = null);
