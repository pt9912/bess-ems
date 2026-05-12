using System.Security.Cryptography;
using System.Text;
using BatteryEms.Application.Optimization;

namespace BatteryEms.Adapters.OptimizationCore;

internal static class OptimizationCoreRequestIdentity
{
    public static string ComputeRequestId(ScheduleOptimizationRequest request)
    {
        var canonical = string.Join('|',
            request.AssetId,
            request.ScheduleType.ToString(),
            request.HorizonStart.ToUniversalTime().ToString("O"),
            request.HorizonEnd.ToUniversalTime().ToString("O"),
            request.TimeStep.ToString("c"),
            request.BaseScheduleVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.MarketBidArea);
        var bytes = Encoding.UTF8.GetBytes(canonical);
        var hash = SHA256.HashData(bytes);
        return new Guid(hash.AsSpan(0, 16).ToArray()).ToString("D");
    }
}
