using BatteryEms.Domain;

namespace BatteryEms.Application.Optimization;

public sealed record DispatchRequest(
    string AssetId,
    DateTimeOffset RequestTime,
    BatteryAsset Asset,
    BatteryTelemetry CurrentTelemetry,
    IReadOnlyList<MarketCommitment> Commitments);
