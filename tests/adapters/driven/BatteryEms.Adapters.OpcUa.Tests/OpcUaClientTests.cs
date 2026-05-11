using Xunit;

namespace BatteryEms.Adapters.OpcUa.Tests;

// Plan-RM-M4-05-B: strukturelle Pins gegen die nach
// `EnsureApplicationConfiguredAsync` sichtbare ApplicationConfiguration.
// Wire-Roundtrips gegen den Embedded TestServer kommen in Sub-Slice D.
public sealed class OpcUaClientTests
{
    [Fact]
    public async Task Production_profile_with_sign_and_encrypt_disables_auto_accept()
    {
        var options = new OpcUaAdapterOptions
        {
            EndpointUrl = new Uri("opc.tcp://localhost:4840"),
            RuntimeProfile = OpcUaRuntimeProfile.Production,
            SecurityMode = OpcUaSecurityMode.SignAndEncrypt,
            // SecurityPolicy=Basic256Sha256 (Default), AllowUnsecured=false (Default).
        };
        await using var client = new OpcUaClient(options);

        await client.EnsureApplicationConfiguredAsync(default);

        var appConfig = client.ApplicationConfigurationForTest;
        Assert.NotNull(appConfig);
        Assert.False(
            appConfig!.SecurityConfiguration.AutoAcceptUntrustedCertificates,
            "Production-Profile darf untrusted Server-Certs NICHT auto-akzeptieren.");
        Assert.False(
            string.IsNullOrWhiteSpace(
                appConfig.SecurityConfiguration.TrustedPeerCertificates.StorePath),
            "Production-Profile braucht einen TrustedPeerCertificates-Store-Pfad "
            + "für Pre-Deployment-Cert-Provisioning.");
    }

    [Fact]
    public async Task Production_profile_with_sign_mode_disables_auto_accept()
    {
        var options = new OpcUaAdapterOptions
        {
            EndpointUrl = new Uri("opc.tcp://localhost:4840"),
            RuntimeProfile = OpcUaRuntimeProfile.Production,
            SecurityMode = OpcUaSecurityMode.Sign,
        };
        await using var client = new OpcUaClient(options);

        await client.EnsureApplicationConfiguredAsync(default);

        var appConfig = client.ApplicationConfigurationForTest;
        Assert.NotNull(appConfig);
        Assert.False(appConfig!.SecurityConfiguration.AutoAcceptUntrustedCertificates);
    }

    [Fact]
    public async Task Hil_simulator_profile_enables_auto_accept()
    {
        var options = new OpcUaAdapterOptions
        {
            EndpointUrl = new Uri("opc.tcp://localhost:4840"),
            RuntimeProfile = OpcUaRuntimeProfile.HilSimulator,
            SecurityMode = OpcUaSecurityMode.None,
            AllowUnsecured = true,
            AllowUnsecuredReason = "client-test",
        };
        await using var client = new OpcUaClient(options);

        await client.EnsureApplicationConfiguredAsync(default);

        var appConfig = client.ApplicationConfigurationForTest;
        Assert.NotNull(appConfig);
        Assert.True(
            appConfig!.SecurityConfiguration.AutoAcceptUntrustedCertificates,
            "HilSimulator-Profile bleibt Pre-M4-05-AutoAccept (Test-Linie braucht "
            + "keinen Cert-Trust-Setup).");
    }

    [Fact]
    public async Task Development_profile_with_unsecured_enables_auto_accept()
    {
        var options = new OpcUaAdapterOptions
        {
            EndpointUrl = new Uri("opc.tcp://localhost:4840"),
            RuntimeProfile = OpcUaRuntimeProfile.Development,
            SecurityMode = OpcUaSecurityMode.None,
            AllowUnsecured = true,
            AllowUnsecuredReason = "dev-against-legacy-server",
        };
        await using var client = new OpcUaClient(options);

        await client.EnsureApplicationConfiguredAsync(default);

        var appConfig = client.ApplicationConfigurationForTest;
        Assert.NotNull(appConfig);
        Assert.True(appConfig!.SecurityConfiguration.AutoAcceptUntrustedCertificates);
    }

    [Fact]
    public async Task Operator_override_of_trusted_server_certificates_path_takes_effect()
    {
        var overridePath = Path.Combine(
            Path.GetTempPath(), "BatteryEms.Tests", $"trust-{Guid.NewGuid():N}");
        Directory.CreateDirectory(overridePath);
        try
        {
            var options = new OpcUaAdapterOptions
            {
                EndpointUrl = new Uri("opc.tcp://localhost:4840"),
                RuntimeProfile = OpcUaRuntimeProfile.Production,
                SecurityMode = OpcUaSecurityMode.SignAndEncrypt,
                TrustedServerCertificatesPath = overridePath,
            };
            await using var client = new OpcUaClient(options);

            await client.EnsureApplicationConfiguredAsync(default);

            var appConfig = client.ApplicationConfigurationForTest;
            Assert.NotNull(appConfig);
            Assert.Equal(
                overridePath,
                appConfig!.SecurityConfiguration.TrustedPeerCertificates.StorePath);
        }
        finally
        {
            try { Directory.Delete(overridePath, recursive: true); }
#pragma warning disable CA1031 // Best-effort test cleanup.
            catch { }
#pragma warning restore CA1031
        }
    }

    [Fact]
    public void Constructor_null_options_throws()
    {
        Assert.Throws<ArgumentNullException>(() => new OpcUaClient(null!));
    }
}
