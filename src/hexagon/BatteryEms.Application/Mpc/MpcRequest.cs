using System;
using BatteryEms.Domain;

namespace BatteryEms.Application.Mpc;

// Per-control-cycle input. `Asset` carries the operational bounds the
// validator cross-checks against the model's constraint hull;
// `LatestMeasurement` is the sensor read the estimator predicts and
// updates against; `CommandTick` is the truncated UTC instant that
// feeds the Sub-Slice-D `mpc_request_id` identity tuple (D-09 field
// `control_cycle_tick_utc_ms_truncated`). The truncation rule itself
// lives with the orchestrator; Sub-Slice A only requires that the
// caller supplies a tick the orchestrator can hash deterministically.
public sealed record MpcRequest
{
    public string AssetId { get; }
    public DateTimeOffset CommandTick { get; }
    public BatteryAsset Asset { get; }
    public BatteryTelemetry? LatestMeasurement { get; }
    public MpcModel Model { get; }
    public MpcOptions Options { get; }
    public MpcState? PriorState { get; }

    public MpcRequest(
        string assetId,
        DateTimeOffset commandTick,
        BatteryAsset asset,
        BatteryTelemetry? latestMeasurement,
        MpcModel model,
        MpcOptions options,
        MpcState? priorState)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(options);

        if (!string.Equals(assetId, asset.AssetId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"AssetId '{assetId}' does not match Asset.AssetId '{asset.AssetId}'.",
                nameof(assetId));
        }
        if (latestMeasurement is not null
            && !string.Equals(latestMeasurement.AssetId, assetId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"LatestMeasurement.AssetId '{latestMeasurement.AssetId}' does not match request AssetId '{assetId}'.",
                nameof(latestMeasurement));
        }

        AssetId = assetId;
        CommandTick = commandTick;
        Asset = asset;
        LatestMeasurement = latestMeasurement;
        Model = model;
        Options = options;
        PriorState = priorState;
    }
}
