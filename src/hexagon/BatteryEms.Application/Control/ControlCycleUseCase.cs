using System.Collections.Concurrent;
using BatteryEms.Application.Assets;
using BatteryEms.Application.Optimization;
using BatteryEms.Application.Realtime;
using BatteryEms.Application.Time;
using BatteryEms.Domain;

namespace BatteryEms.Application.Control;

public sealed class ControlCycleUseCase : IControlCycleUseCase
{
    private readonly IBatteryAssetRegistry _assets;
    private readonly ISnapshotStore _snapshots;
    private readonly IDispatchOptimizer _optimizer;
    private readonly IClock _clock;
    private readonly ControlCycleOptions _options;

    private readonly ConcurrentDictionary<string, (double Power, DateTimeOffset At)> _previous =
        new(StringComparer.Ordinal);

    public ControlCycleUseCase(
        IBatteryAssetRegistry assets,
        ISnapshotStore snapshots,
        IDispatchOptimizer optimizer,
        IClock clock,
        ControlCycleOptions options)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(optimizer);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);

        _assets = assets;
        _snapshots = snapshots;
        _optimizer = optimizer;
        _clock = clock;
        _options = options;
    }

    public async Task<BatteryCommand> ExecuteAsync(string assetId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);

        var now = _clock.UtcNow;

        var asset = _assets.Find(assetId);
        if (asset is null)
        {
            return BatteryCommand.SafeStop(assetId, now, _options.SafeFallbackValidity, "asset-not-registered", CommandSource.Fallback);
        }

        var snapshot = _snapshots.GetLatest(assetId, now);
        if (snapshot is null)
        {
            return BatteryCommand.SafeStop(assetId, now, _options.SafeFallbackValidity, "no-snapshot", CommandSource.Fallback);
        }

        if (!snapshot.Quality.IsUsableForControl)
        {
            return BatteryCommand.SafeStop(assetId, now, _options.SafeFallbackValidity, snapshot.Quality.Reason, CommandSource.Fallback);
        }

        if (!snapshot.Telemetry.Available)
        {
            return BatteryCommand.SafeStop(assetId, now, _options.SafeFallbackValidity, "asset-unavailable", CommandSource.Fallback);
        }

        var dispatch = await _optimizer.OptimizeAsync(
            new DispatchRequest(assetId, now, asset, snapshot.Telemetry, Array.Empty<MarketCommitment>()),
            cancellationToken).ConfigureAwait(false);

        if (!dispatch.IsValid)
        {
            return BatteryCommand.SafeStop(assetId, now, _options.SafeFallbackValidity, dispatch.Reason, CommandSource.Fallback);
        }

        var constrained = ConstraintLimiter.Apply(asset, snapshot.Telemetry, dispatch.TargetActivePowerKw);

        LimitResult ramped;
        if (_previous.TryGetValue(assetId, out var prev))
        {
            var elapsed = now - prev.At;
            ramped = RampLimiter.Apply(asset, prev.Power, constrained.LimitedActivePowerKw, elapsed);
        }
        else
        {
            ramped = LimitResult.Unchanged(constrained.LimitedActivePowerKw);
        }

        _previous[assetId] = (ramped.LimitedActivePowerKw, now);

        var mode = ramped.LimitedActivePowerKw switch
        {
            > 0 => CommandMode.Discharge,
            < 0 => CommandMode.Charge,
            _ => CommandMode.Idle,
        };

        var reason = constrained.WasLimited
            ? constrained.LimitReason
            : ramped.WasLimited
                ? ramped.LimitReason
                : dispatch.Reason;

        return new BatteryCommand(
            CommandId: $"ctrl-{now.ToUnixTimeMilliseconds()}-{assetId}",
            Timestamp: now,
            AssetId: assetId,
            Mode: mode,
            ActivePowerKw: ramped.LimitedActivePowerKw,
            ReactivePowerKvar: 0,
            ValidUntil: now + _options.SafeFallbackValidity,
            Reason: reason,
            Source: CommandSource.Optimization);
    }
}
