using System.Buffers;
using System.Collections.Concurrent;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTnet;
using MQTTnet.Protocol;

namespace BatteryEms.Adapters.Mqtt;

// MQTTnet 4.x adapter for IMqttClient. Subscriptions are multiplexed in
// process: each topic registers once at the broker and any number of
// adapter-side handlers fan-out from ApplicationMessageReceivedAsync.
//
// RM-M4-06-FUP-F04: Production MQTT is fail-closed unless TLS plus
// broker authentication is configured. Development/HIL simulators may
// opt into plaintext explicitly via MqttAdapterOptions.AllowPlaintext.
public sealed class MqttNetClient : IMqttClient
{
    private readonly MQTTnet.IMqttClient _inner;
    private readonly MqttClientOptions _connectOptions;
    private readonly TimeSpan _connectTimeout;
    private readonly ConcurrentDictionary<string, List<Func<MqttMessage, Task>>> _handlers = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _connectGate = new(1, 1);

    public MqttNetClient(MqttAdapterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.EnsureValid(NullLogger.Instance);

        // MQTTnet 5.0 split MqttFactory into client / server factories;
        // we only need the client side.
        var factory = new MqttClientFactory();
        _inner = factory.CreateMqttClient();
        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(options.BrokerHost, options.BrokerPort)
            .WithClientId(options.ClientId)
            .WithCleanSession(true);

        var password = ReadSecret(options.CredentialsOrDefault.Password, options.CredentialsOrDefault.PasswordPath);
        if (!string.IsNullOrWhiteSpace(options.CredentialsOrDefault.Username))
        {
            builder.WithCredentials(options.CredentialsOrDefault.Username, password ?? string.Empty);
        }

        var tls = options.TlsOrDefault;
        if (tls.Enabled)
        {
            var roots = LoadTrustedCaCertificates(tls.TrustedCaCertificatePath!);
            var clientCertificate = LoadClientCertificate(tls);
            builder.WithTlsOptions(tlsBuilder =>
            {
                tlsBuilder.UseTls(true);
                tlsBuilder.WithCertificateValidationHandler(args => ValidateBrokerCertificate(args, roots));
                if (clientCertificate is not null)
                {
                    tlsBuilder.WithClientCertificates([clientCertificate]);
                }
            });
        }

        _connectOptions = builder.Build();
        _connectTimeout = options.ConnectTimeout;

        _inner.ApplicationMessageReceivedAsync += OnApplicationMessageReceivedAsync;
    }

    public bool IsConnected => _inner.IsConnected;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_inner.IsConnected)
        {
            return;
        }

        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_inner.IsConnected)
            {
                return;
            }
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_connectTimeout);
            await _inner.ConnectAsync(_connectOptions, cts.Token).ConfigureAwait(false);
        }
        finally
        {
            _connectGate.Release();
        }
    }

    public async Task SubscribeAsync(
        string topicFilter,
        MqttQualityOfService qos,
        Func<MqttMessage, Task> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(topicFilter);
        ArgumentNullException.ThrowIfNull(handler);

        var alreadySubscribed = false;
        _handlers.AddOrUpdate(
            topicFilter,
            _ => new List<Func<MqttMessage, Task>> { handler },
            (_, existing) =>
            {
                lock (existing)
                {
                    existing.Add(handler);
                }
                alreadySubscribed = true;
                return existing;
            });

        if (alreadySubscribed)
        {
            return;
        }

        var options = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(topicFilter, ToMqttNet(qos))
            .Build();
        await _inner.SubscribeAsync(options, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishAsync(
        string topic,
        byte[] payload,
        MqttQualityOfService qos,
        bool retained,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(payload);

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithRetainFlag(retained)
            .WithQualityOfServiceLevel(ToMqttNet(qos))
            .Build();
        await _inner.PublishAsync(message, cancellationToken).ConfigureAwait(false);
    }

    private static MqttQualityOfServiceLevel ToMqttNet(MqttQualityOfService qos) => qos switch
    {
        MqttQualityOfService.AtMostOnce => MqttQualityOfServiceLevel.AtMostOnce,
        MqttQualityOfService.AtLeastOnce => MqttQualityOfServiceLevel.AtLeastOnce,
        MqttQualityOfService.ExactlyOnce => MqttQualityOfServiceLevel.ExactlyOnce,
        _ => throw new ArgumentOutOfRangeException(nameof(qos), qos, "Unknown MqttQualityOfService."),
    };

    private static string? ReadSecret(string? inlineSecret, string? secretPath)
    {
        if (!string.IsNullOrWhiteSpace(secretPath))
        {
            return File.ReadAllText(secretPath).TrimEnd('\r', '\n');
        }

        return inlineSecret;
    }

    private static X509Certificate2Collection LoadTrustedCaCertificates(string path)
    {
        var roots = new X509Certificate2Collection();
        roots.ImportFromPemFile(path);
        if (roots.Count == 0)
        {
            throw new InvalidOperationException(
                $"mqtt-trusted-ca-empty: '{path}' did not contain a PEM certificate.");
        }

        return roots;
    }

    private static X509Certificate2? LoadClientCertificate(MqttTlsOptions tls)
    {
        if (string.IsNullOrWhiteSpace(tls.ClientCertificatePath))
        {
            return null;
        }

        var password = ReadSecret(tls.ClientCertificatePassword, tls.ClientCertificatePasswordPath);
        return X509CertificateLoader.LoadPkcs12FromFile(
            tls.ClientCertificatePath,
            password,
            X509KeyStorageFlags.EphemeralKeySet);
    }

    private static bool ValidateBrokerCertificate(
        MqttClientCertificateValidationEventArgs args,
        X509Certificate2Collection trustedRoots)
    {
        if (args.Certificate is null)
        {
            return false;
        }
        if ((args.SslPolicyErrors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0
            || (args.SslPolicyErrors & SslPolicyErrors.RemoteCertificateNotAvailable) != 0)
        {
            return false;
        }

        using var serverCertificate = args.Certificate is X509Certificate2 certificate
            ? X509CertificateLoader.LoadCertificate(certificate.RawData)
            : X509CertificateLoader.LoadCertificate(args.Certificate.Export(X509ContentType.Cert));
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        foreach (var root in trustedRoots)
        {
            chain.ChainPolicy.CustomTrustStore.Add(root);
        }

        return chain.Build(serverCertificate);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Handlers must not propagate exceptions back into MQTTnet's dispatcher loop; per-handler errors are isolated.")]
    private async Task OnApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var topic = e.ApplicationMessage.Topic;
        if (!_handlers.TryGetValue(topic, out var handlers))
        {
            return;
        }

        Func<MqttMessage, Task>[] snapshot;
        lock (handlers)
        {
            snapshot = handlers.ToArray();
        }

        // MQTTnet 5.0: ApplicationMessage.PayloadSegment lost its
        // getter; the read-side is ReadOnlySequence<byte> Payload, and
        // ToArray() materialises it into the byte[] our handlers
        // expect. Single-allocation copy, same shape as before.
        var payload = e.ApplicationMessage.Payload.ToArray();
        var message = new MqttMessage(topic, payload);
        foreach (var handler in snapshot)
        {
            try
            {
                await handler(message).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // One handler's failure must not starve the others.
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort shutdown swallows transport errors so disposal never throws; transport may already be torn down.")]
    public async ValueTask DisposeAsync()
    {
        if (_inner.IsConnected)
        {
            try
            {
                await _inner.DisconnectAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Best-effort shutdown; transport may already be torn down.
            }
        }
        _inner.Dispose();
        _connectGate.Dispose();
    }
}
