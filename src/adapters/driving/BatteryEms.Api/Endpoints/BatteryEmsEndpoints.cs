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
        ArgumentNullException.ThrowIfNull(routes);
        MapHealth(routes);
        MapBatteryStatus(routes);
        MapCurrentCommand(routes);
        MapCurrentSchedules(routes);
        MapOperatorStop(routes);
        return routes;
    }

    private static void MapHealth(IEndpointRouteBuilder routes)
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
    }

    private static void MapBatteryStatus(IEndpointRouteBuilder routes)
    {
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
    }

    private static void MapCurrentCommand(IEndpointRouteBuilder routes)
    {
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
    }

    private static void MapCurrentSchedules(IEndpointRouteBuilder routes)
    {
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
    }

    private static void MapOperatorStop(IEndpointRouteBuilder routes)
    {
        // LH-API-006: operator-stop sets a flag the control loop reads
        // on every cycle, short-circuiting to a safe stop until the
        // process restarts (the M1 in-memory registry has no clear
        // endpoint by design; see Application.Control.IOperatorStopRegistry).
        // The endpoint is open for now — RM-M1-16 layers AuthN/AuthZ +
        // audit on top without changing this contract.
        routes.MapPost("/operator/stop", (OperatorStopRequestBody body, IOperatorStopUseCase useCase) =>
            {
                if (body is null
                    || string.IsNullOrWhiteSpace(body.AssetId)
                    || string.IsNullOrWhiteSpace(body.Operator)
                    || string.IsNullOrWhiteSpace(body.Reason))
                {
                    return Results.BadRequest(new { error = "missing-required-field" });
                }
                var state = useCase.Execute(new OperatorStopRequest(body.AssetId, body.Operator, body.Reason));
                return Results.Ok(new OperatorStopResponse(
                    AssetId: state.AssetId,
                    Operator: state.Operator,
                    Reason: state.Reason,
                    ActivatedAt: state.ActivatedAt));
            })
            .WithName("OperatorStop")
            .WithSummary("Operator-driven safe stop for an asset (LH-API-006).");
    }
}
