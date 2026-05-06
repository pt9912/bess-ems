using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using BatteryEms.Application.Configuration;
using BatteryEms.Application.IO;
using BatteryEms.Application.Time;
using BatteryEms.Domain;

namespace BatteryEms.Adapters.Mqtt;

public sealed class MqttTelemetrySource : IBatteryTelemetrySource
{
    private readonly IMqttClient _client;
    private readonly MqttAdapterOptions _options;
    private readonly IClock _clock;
    private readonly string _telemetryTopic;
    private readonly Channel<BatteryTelemetry> _channel = Channel.CreateUnbounded<BatteryTelemetry>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private AdapterStatus _status = AdapterStatus.Disconnected;
    private int _started;

    public MqttTelemetrySource(
        IMqttClient client,
        MqttMappingConfiguration mapping,
        MqttAdapterOptions options,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);

        _client = client;
        _options = options;
        _clock = clock;

        // The EMS subscribes to the `telemetry` topic — its payload carries
        // the full snapshot. status/fault are intentionally not consumed
        // here in M1: their content is a strict subset of telemetry, and
        // every simulator tick emits all three together. RM-M1-10 only
        // demands "Telemetrieempfang"; pulling status/fault separately
        // adds a state-merging concern without value for M1.
        var telemetry = TopicResolver.Require(mapping, "telemetry", "subscribe");
        _telemetryTopic = TopicResolver.SubstituteAssetId(telemetry.Topic, options.AssetId);
    }

    public AdapterStatus Status => _status;

    public async IAsyncEnumerable<BatteryTelemetry> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);

        while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (_channel.Reader.TryRead(out var telemetry))
            {
                yield return telemetry;
            }
        }
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        await _client.SubscribeAsync(_telemetryTopic, OnMessageAsync, cancellationToken).ConfigureAwait(false);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Adapter boundary captures arbitrary protocol/decoding errors and surfaces them via AdapterStatus so the worker can degrade gracefully.")]
    private Task OnMessageAsync(MqttMessage message)
    {
        try
        {
            var snapshot = JsonSerializer.Deserialize<TelemetrySnapshotPayload>(message.Payload.Span, MqttJson.Options);
            if (snapshot is null)
            {
                RecordFailure("payload-null");
                return Task.CompletedTask;
            }

            var now = _clock.UtcNow;
            _status = new AdapterStatus(
                Connected: true,
                LastSuccessfulReadAt: now,
                LastError: null,
                ConsecutiveFailures: 0);

            var telemetry = new BatteryTelemetry(
                Timestamp: now,
                AssetId: _options.AssetId,
                SocPercent: snapshot.SocPercent,
                SohPercent: snapshot.SohPercent,
                ActivePowerKw: snapshot.ActivePowerKw,
                ReactivePowerKvar: snapshot.ReactivePowerKvar,
                DcVoltage: snapshot.DcVoltage,
                DcCurrent: snapshot.DcCurrent,
                TemperatureCelsius: snapshot.TemperatureCelsius,
                Available: snapshot.Available,
                FaultStatus: string.IsNullOrEmpty(snapshot.FaultStatus) ? "ok" : snapshot.FaultStatus,
                DataQuality: DataQuality.Valid);

            // Unbounded channel: TryWrite always succeeds unless the channel is completed.
            _channel.Writer.TryWrite(telemetry);
        }
        catch (Exception ex)
        {
            RecordFailure(ex.Message);
        }
        return Task.CompletedTask;
    }

    private void RecordFailure(string reason)
    {
        _status = new AdapterStatus(
            Connected: _client.IsConnected,
            LastSuccessfulReadAt: _status.LastSuccessfulReadAt,
            LastError: reason,
            ConsecutiveFailures: _status.ConsecutiveFailures + 1);
    }
}
