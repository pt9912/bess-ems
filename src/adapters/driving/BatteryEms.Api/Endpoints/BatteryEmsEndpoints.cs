using BatteryEms.Api.Contracts;
using BatteryEms.Application.Api;
using BatteryEms.Application.Time;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BatteryEms.Api.Endpoints;

public static class BatteryEmsEndpoints
{
    public static IEndpointRouteBuilder MapBatteryEms(this IEndpointRouteBuilder routes)
    {
        // LH-API-001: liveness/readiness lite. The Worker will surface
        // deeper signals (DB reachable, simulator connected) once it
        // exists in RM-M1-19; the contract shape stays stable.
        routes.MapGet("/health", (IHealthQuery query) =>
            {
                var status = query.Probe();
                return Results.Ok(new HealthResponse(status.Status, status.At));
            })
            .WithName("Health")
            .WithSummary("Liveness probe (LH-API-001).");

        // LH-API-002: current snapshot + last command for an asset.
        // 404 when the asset is not registered or the registry has not
        // yet seen any telemetry.
        routes.MapGet("/battery/{assetId}/status", async (
                string assetId,
                IBatteryStatusQuery query,
                IClock clock,
                CancellationToken ct) =>
            {
                var view = await query.FindAsync(assetId, clock.UtcNow, ct).ConfigureAwait(false);
                if (view is null)
                {
                    return Results.NotFound();
                }
                return Results.Ok(new BatteryStatusResponse(
                    AssetId: view.AssetId,
                    Telemetry: view.Telemetry is null ? null : TelemetryView.From(view.Telemetry),
                    Quality: view.Quality is null ? null : DataQualityView.From(view.Quality),
                    ObservedAt: view.ObservedAt,
                    LastCommand: view.LastCommand is null ? null : CommandView.From(view.LastCommand)));
            })
            .WithName("BatteryStatus")
            .WithSummary("Current battery status (LH-API-002).");

        // LH-API-003: last command produced for an asset, including the
        // Reason that drove it (covers the LH-MON-004 reason invariant
        // on the API surface).
        routes.MapGet("/battery/{assetId}/command/current", async (
                string assetId,
                IBatteryStatusQuery query,
                IClock clock,
                CancellationToken ct) =>
            {
                var view = await query.FindAsync(assetId, clock.UtcNow, ct).ConfigureAwait(false);
                if (view is null)
                {
                    return Results.NotFound();
                }
                return Results.Ok(new CommandResponse(
                    AssetId: view.AssetId,
                    Command: view.LastCommand is null ? null : CommandView.From(view.LastCommand)));
            })
            .WithName("CurrentCommand")
            .WithSummary("Latest battery command (LH-API-003).");

        // LH-API-004: currently-active schedules for an asset (one entry
        // per ScheduleType). Asset id flows via query string so the
        // route stays /markets/schedules/current per the spec example;
        // empty list = no schedule loaded yet.
        routes.MapGet("/markets/schedules/current", (
                string assetId,
                IScheduleQuery query) =>
            {
                if (string.IsNullOrWhiteSpace(assetId))
                {
                    return Results.BadRequest(new { error = "missing-asset-id" });
                }
                var schedules = query.FindCurrent(assetId);
                return Results.Ok(new SchedulesResponse(
                    AssetId: assetId,
                    Schedules: schedules.Select(ScheduleView.From).ToArray()));
            })
            .WithName("CurrentSchedules")
            .WithSummary("Currently active schedules per asset (LH-API-004).");

        return routes;
    }
}
