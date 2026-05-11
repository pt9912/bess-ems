using System.Security.Cryptography.X509Certificates;
using BatteryEms.Adapters.OpcUa;
using Opc.Ua;

namespace BatteryEms.OpcUa.IntegrationTests;

// Plan-RM-M4-05-C: bidirektionale Trust-Bridge zwischen Embedded
// TestServer und OpcUaClient. Kopiert die jeweilige Application-Cert
// in den Trusted-Peer-Store der anderen Seite — damit testen die
// Security-Pins **echten** Cert-Trust statt AutoAccept.
//
// Voraussetzung: beide Seiten haben ihre `EnsureApplicationConfigured-
// Async`-Equivalente schon durchgelaufen (Server via `StartAsync`,
// Client via `EnsureApplicationConfiguredAsync`). Der Helper liest
// die App-Certs aus den Application-Configurations und schreibt sie
// per `ICertificateStore.AddAsync` in die Trusted-Peer-Stores.
//
// Nach dem Trust-Setup ruft der Helper `UpdateAsync` auf beiden
// Validators, damit der frisch geschriebene Trust beim nächsten
// Connect/Session-Handshake greift.
internal static class CertificateTrustBridge
{
    public static async Task EstablishMutualTrustAsync(
        EmbeddedTestServerHost server,
        OpcUaClient client,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(client);

        // Client-side App-Config liegt erst nach EnsureApplicationConfigured-
        // Async vor. Der Defaults.ForProductionSecure-Pfad triggert das in
        // ConnectAsync — die Trust-Bridge muss VOR ConnectAsync laufen,
        // daher rufen wir es hier idempotent.
        await client.EnsureApplicationConfiguredAsync(cancellationToken)
            .ConfigureAwait(false);

        var serverConfig = server.ApplicationConfiguration;
        var clientConfig = client.ApplicationConfigurationForTest
            ?? throw new InvalidOperationException(
                "Client ApplicationConfiguration is null after "
                + "EnsureApplicationConfiguredAsync — Trust-Bridge cannot run.");

        var serverCert = await LoadOwnCertAsync(serverConfig, cancellationToken)
            .ConfigureAwait(false);
        var clientCert = await LoadOwnCertAsync(clientConfig, cancellationToken)
            .ConfigureAwait(false);

        // Server-Cert in den Client-Trust-Store schreiben.
        await AddToTrustStoreAsync(
            clientConfig.SecurityConfiguration.TrustedPeerCertificates,
            serverCert,
            cancellationToken).ConfigureAwait(false);

        // Client-Cert in den Server-Trust-Store schreiben.
        await AddToTrustStoreAsync(
            serverConfig.SecurityConfiguration.TrustedPeerCertificates,
            clientCert,
            cancellationToken).ConfigureAwait(false);

        // Validators neu laden, sodass die frisch geschriebenen Certs
        // beim nächsten Handshake greifen.
        await clientConfig.CertificateValidator
            .UpdateAsync(clientConfig, ct: cancellationToken)
            .ConfigureAwait(false);
        await serverConfig.CertificateValidator
            .UpdateAsync(serverConfig, ct: cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<X509Certificate2> LoadOwnCertAsync(
        ApplicationConfiguration appConfig, CancellationToken ct)
    {
        var identifier = appConfig.SecurityConfiguration.ApplicationCertificate
            ?? throw new InvalidOperationException(
                "ApplicationConfiguration has no ApplicationCertificate.");
        var cert = await identifier
            .FindAsync(needPrivateKey: false, ct: ct)
            .ConfigureAwait(false);
        if (cert is null)
        {
            throw new InvalidOperationException(
                $"ApplicationCertificate (subject={identifier.SubjectName}) "
                + "could not be loaded from the configured store.");
        }
        // Return only the public-key view; the trusted-store accepts the
        // public cert without the private key.
        return X509CertificateLoader.LoadCertificate(cert.RawData);
    }

    private static async Task AddToTrustStoreAsync(
        CertificateTrustList trustList,
        X509Certificate2 certificate,
        CancellationToken cancellationToken)
    {
        var identifier = new CertificateStoreIdentifier
        {
            StoreType = trustList.StoreType,
            StorePath = trustList.StorePath,
        };
        using ICertificateStore store = identifier.OpenStore(telemetry: null);
        await store.AddAsync(certificate, password: null, cancellationToken)
            .ConfigureAwait(false);
    }
}
