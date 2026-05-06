using System.Collections.Concurrent;
using System.Text.Json;
using BatteryEms.Application.Configuration;
using BatteryEms.Application.IO;
using BatteryEms.Application.Time;
using BatteryEms.Domain;

namespace BatteryEms.Adapters.Mqtt;

public sealed class MqttCommandSink : IBatteryCommandSink
{
    private readonly IMqttClient _client;
    private readonly MqttAdapterOptions _options;
    private readonly IClock _clock;
    private readonly string _commandTopic;
    private readonly string _ackTopic;
    private readonly bool _commandRetained;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<CommandAckPayload>> _pending = new(StringComparer.Ordinal);
    private int _ackSubscribed;

    public MqttCommandSink(
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

        var command = TopicResolver.Require(mapping, "command", "publish");
        var ack = TopicResolver.Require(mapping, "command_ack", "subscribe");
        _commandTopic = TopicResolver.SubstituteAssetId(command.Topic, options.AssetId);
        _ackTopic = TopicResolver.SubstituteAssetId(ack.Topic, options.AssetId);
        _commandRetained = command.Retained;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Adapter boundary captures arbitrary protocol errors and reports them via CommandDispatchResult so the control loop can react.")]
    public async Task<CommandDispatchResult> WriteAsync(BatteryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await EnsureAckSubscribedAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return CommandDispatchResult.Failed($"connect-failed: {ex.Message}", _clock.UtcNow);
        }

        var tcs = new TaskCompletionSource<CommandAckPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(command.CommandId, tcs))
        {
            return CommandDispatchResult.Failed("command-id-not-unique", _clock.UtcNow);
        }

        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(ToWire(command), MqttJson.Options);
            await _client.PublishAsync(_commandTopic, payload, _commandRetained, cancellationToken).ConfigureAwait(false);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.CommandAckTimeout);

            using (timeoutCts.Token.Register(static state =>
                       ((TaskCompletionSource<CommandAckPayload>)state!).TrySetCanceled(),
                   tcs))
            {
                CommandAckPayload ack;
                try
                {
                    ack = await tcs.Task.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    return CommandDispatchResult.Failed("ack-timeout", _clock.UtcNow);
                }

                return ack.Accepted
                    ? CommandDispatchResult.Ok(_clock.UtcNow, ack.Reason ?? command.Reason)
                    : CommandDispatchResult.Failed($"ack-rejected: {ack.Reason ?? "unknown"}", _clock.UtcNow);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return CommandDispatchResult.Failed($"publish-failed: {ex.Message}", _clock.UtcNow);
        }
        finally
        {
            _pending.TryRemove(command.CommandId, out _);
        }
    }

    private async Task EnsureAckSubscribedAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _ackSubscribed, 1) == 1)
        {
            return;
        }
        await _client.SubscribeAsync(_ackTopic, OnAckAsync, cancellationToken).ConfigureAwait(false);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "ACK decode errors must not escape the dispatcher; they are surfaced as ack-decode-error timeouts on the affected commands.")]
    private Task OnAckAsync(MqttMessage message)
    {
        CommandAckPayload? ack = null;
        try
        {
            ack = JsonSerializer.Deserialize<CommandAckPayload>(message.Payload.Span, MqttJson.Options);
        }
        catch (Exception)
        {
            // Malformed ACK is dropped; pending command will time out on its own.
        }
        if (ack is null || string.IsNullOrEmpty(ack.CommandId))
        {
            return Task.CompletedTask;
        }
        if (_pending.TryGetValue(ack.CommandId, out var tcs))
        {
            tcs.TrySetResult(ack);
        }
        return Task.CompletedTask;
    }

    private static CommandPayload ToWire(BatteryCommand command) => new(
        CommandId: command.CommandId,
        Timestamp: command.Timestamp,
        AssetId: command.AssetId,
        Mode: command.Mode.ToString(),
        ActivePowerKw: command.ActivePowerKw,
        ReactivePowerKvar: command.ReactivePowerKvar,
        ValidUntil: command.ValidUntil,
        Reason: command.Reason,
        Source: command.Source.ToString());
}
