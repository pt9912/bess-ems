using BatteryEms.Api.Contracts;
using BatteryEms.Application.Assets;
using BatteryEms.Application.Control;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BatteryEms.Api.Endpoints;

public static class OperatorUiEndpoints
{
    public static IEndpointRouteBuilder MapOperatorUiSupport(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        MapAssets(routes);
        MapOperatorStopStatus(routes);
        return routes;
    }

    private static void MapAssets(IEndpointRouteBuilder routes)
    {
        // RM-M6-01: operator UI asset selector. Read-only API surface
        // over the existing registry; no UI-owned asset model.
        routes.MapGet("/assets", (IBatteryAssetRegistry registry) =>
            {
                var assets = registry.GetAll()
                    .OrderBy(asset => asset.AssetId, StringComparer.Ordinal)
                    .Select(AssetView.From)
                    .ToArray();
                return Results.Ok(new AssetsResponse(assets));
            })
            .WithName("Assets")
            .WithSummary("Registered battery assets for operator views (RM-M6-01).");
    }

    private static void MapOperatorStopStatus(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/operator/stops/current", (
                string? assetId,
                IOperatorStopRegistry registry) =>
            {
                if (string.IsNullOrWhiteSpace(assetId))
                {
                    return Results.BadRequest(new { error = "missing-asset-id" });
                }

                var state = registry.Find(assetId);
                return Results.Ok(new OperatorStopStatusResponse(
                    AssetId: assetId,
                    Stop: state is null ? null : OperatorStopView.From(state)));
            })
            .WithName("OperatorStopStatus")
            .WithSummary("Current operator-stop state for an asset (RM-M6-01).");
    }
}
