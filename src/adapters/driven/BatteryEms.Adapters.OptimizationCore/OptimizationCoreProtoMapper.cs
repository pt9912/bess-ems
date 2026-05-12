using Google.Protobuf.WellKnownTypes;
using BatteryEms.Application.Optimization;
using BatteryEms.Domain;

namespace BatteryEms.Adapters.OptimizationCore;

internal static class OptimizationCoreProtoMapper
{
    public static Grpc.V1.OptimizeRequest BuildRequest(ScheduleOptimizationRequest request)
    {
        var horizonStart = request.HorizonStart.ToUniversalTime();
        var horizonEnd = request.HorizonEnd.ToUniversalTime();
        var proto = new Grpc.V1.OptimizeRequest
        {
            RequestId = Guid.NewGuid().ToString("D"),
            AssetId = request.AssetId,
            ScheduleType = MapScheduleType(request.ScheduleType),
            HorizonStart = Timestamp.FromDateTimeOffset(horizonStart),
            HorizonEnd = Timestamp.FromDateTimeOffset(horizonEnd),
            TimeStep = Duration.FromTimeSpan(request.TimeStep),
            PriceUnit = request.PriceUnit ?? string.Empty,
            MarketBidArea = request.MarketBidArea,
            BaseScheduleVersion = request.BaseScheduleVersion,
            Asset = MapAsset(request.Asset),
        };
        if (request.PricesPerStep is { } prices)
        {
            proto.PricesPerStep.AddRange(prices);
        }
        foreach (var reserve in request.Reserves)
        {
            proto.Reserves.Add(MapReserve(reserve));
        }
        return proto;
    }

    private static Grpc.V1.ScheduleType MapScheduleType(ScheduleType type) => type switch
    {
        ScheduleType.DayAhead => Grpc.V1.ScheduleType.DayAhead,
        ScheduleType.Intraday => Grpc.V1.ScheduleType.Intraday,
        ScheduleType.RegelLeistungReserve => Grpc.V1.ScheduleType.RegelleistungReserve,
        _ => Grpc.V1.ScheduleType.Unspecified,
    };

    private static Grpc.V1.AssetCapabilities MapAsset(BatteryAsset asset) => new()
    {
        AssetId = asset.AssetId,
        CapacityKwh = asset.CapacityKwh,
        MaxChargePowerKw = asset.MaxChargePowerKw,
        MaxDischargePowerKw = asset.MaxDischargePowerKw,
        MinSocPercent = asset.MinSocPercent,
        MaxSocPercent = asset.MaxSocPercent,
        ChargeEfficiency = asset.ChargeEfficiency,
        DischargeEfficiency = asset.DischargeEfficiency,
        MaxRampKwPerSecond = asset.MaxRampKwPerSecond,
        MinOperatingTemperatureCelsius = asset.MinOperatingTemperatureCelsius,
        MaxOperatingTemperatureCelsius = asset.MaxOperatingTemperatureCelsius,
    };

    private static Grpc.V1.ReserveBand MapReserve(ReserveBand band) => new()
    {
        Product = band.Product switch
        {
            ReserveProduct.Fcr => Grpc.V1.ReserveBand.Types.Product.Fcr,
            ReserveProduct.Afrr => Grpc.V1.ReserveBand.Types.Product.Afrr,
            ReserveProduct.Mfrr => Grpc.V1.ReserveBand.Types.Product.Mfrr,
            _ => Grpc.V1.ReserveBand.Types.Product.Unspecified,
        },
        Direction = band.Direction switch
        {
            ReserveDirection.Symmetric => Grpc.V1.ReserveBand.Types.Direction.Symmetric,
            ReserveDirection.Up => Grpc.V1.ReserveBand.Types.Direction.Up,
            ReserveDirection.Down => Grpc.V1.ReserveBand.Types.Direction.Down,
            _ => Grpc.V1.ReserveBand.Types.Direction.Unspecified,
        },
        WindowStart = Timestamp.FromDateTimeOffset(band.Start.ToUniversalTime()),
        WindowEnd = Timestamp.FromDateTimeOffset(band.End.ToUniversalTime()),
        PowerKw = band.PowerKw,
    };
}
