using System.Security.Claims;
using BatteryEms.Api.Auth;
using BatteryEms.Api.Contracts;
using BatteryEms.Application.Api;
using BatteryEms.Application.Assets;
using BatteryEms.Application.Optimization;
using BatteryEms.Application.Persistence;
using BatteryEms.Application.Time;
using BatteryEms.Domain;
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
        MapDayAheadOptimize(routes);
        MapOptimizationRunStatus(routes);
        return routes;
    }

    private static void MapHealth(IEndpointRouteBuilder routes)
    {
        // LH-API-001: liveness/readiness probe. RM-M1-19c upgrades the
        // probe to surface component statuses (database reachable etc.)
        // and returns 503 when any critical component is unhealthy so
        // the Docker HEALTHCHECK marks the container unhealthy.
        routes.MapGet("/health", (IHealthQuery query) =>
            {
                var status = query.Probe();
                var response = new HealthResponse(status.Status, status.At, status.Components);
                if (status.Status != "ok")
                {
                    return Results.Json(response, statusCode: 503);
                }
                return Results.Ok(response);
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
        // LH-API-006/007: operator-stop sets a flag the control loop reads
        // on every cycle (Application.Control.IOperatorStopRegistry) and
        // every attempt — accepted, invalid, unauthorized, forbidden —
        // lands in the audit log. The operator identity comes from the
        // authenticated principal so a caller can't impersonate someone
        // else by editing the body. AuthN/AuthZ rejection is audited from
        // ApiTokenAuthenticationHandler; this delegate handles accepted +
        // invalid because it owns the request body.
        routes.MapPost("/operator/stop", async (
                OperatorStopRequestBody body,
                ClaimsPrincipal user,
                IOperatorStopUseCase useCase,
                IOperatorAuditLog auditLog,
                IClock clock,
                CancellationToken ct) =>
            {
                var operatorId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? AuthConstants.AnonymousOperator;

                if (body is null
                    || string.IsNullOrWhiteSpace(body.AssetId)
                    || string.IsNullOrWhiteSpace(body.Reason))
                {
                    await auditLog.AppendAsync(
                        new AuditEvent(
                            clock.UtcNow,
                            operatorId,
                            AuthConstants.OperatorStopAction,
                            TargetAssetId: body?.AssetId,
                            Reason: "missing-required-field",
                            Outcome: AuthConstants.OutcomeInvalid),
                        ct).ConfigureAwait(false);
                    return Results.BadRequest(new { error = "missing-required-field" });
                }

                var state = useCase.Execute(new OperatorStopRequest(body.AssetId, operatorId, body.Reason));
                await auditLog.AppendAsync(
                    new AuditEvent(
                        clock.UtcNow,
                        operatorId,
                        AuthConstants.OperatorStopAction,
                        TargetAssetId: state.AssetId,
                        Reason: state.Reason,
                        Outcome: AuthConstants.OutcomeAccepted),
                    ct).ConfigureAwait(false);
                return Results.Ok(new OperatorStopResponse(
                    AssetId: state.AssetId,
                    Operator: state.Operator,
                    Reason: state.Reason,
                    ActivatedAt: state.ActivatedAt));
            })
            .RequireAuthorization(AuthConstants.OperatorPolicy)
            .WithName("OperatorStop")
            .WithSummary("Operator-driven safe stop for an asset (LH-API-006/007).");
    }

    private static void MapDayAheadOptimize(IEndpointRouteBuilder routes)
    {
        // LH-API-005: triggers a day-ahead schedule optimisation. The use
        // case persists the OptimizationRun + replaces the schedule
        // version (RM-M2-OP-03); the response is the API-facing summary.
        // Operator-policy guarded — schedule optimisation is a write
        // action against the schedule repository.
        routes.MapPost("/markets/day-ahead/optimize", async (
                OptimizationRequestBody body,
                IBatteryAssetRegistry assets,
                IScheduleOptimizationUseCase useCase,
                CancellationToken ct) =>
            {
                if (body is null
                    || string.IsNullOrWhiteSpace(body.AssetId)
                    || body.TimeStepSeconds <= 0
                    || body.HorizonStart >= body.HorizonEnd)
                {
                    return Results.BadRequest(new { error = "missing-or-invalid-field" });
                }

                var asset = assets.Find(body.AssetId);
                if (asset is null)
                {
                    return Results.NotFound(new { error = "asset-not-registered", asset_id = body.AssetId });
                }

                var scheduleType = ParseScheduleType(body.ScheduleType);
                if (scheduleType is null)
                {
                    return Results.BadRequest(new { error = "unknown-schedule-type", value = body.ScheduleType });
                }

                ScheduleOptimizationCommand command;
                try
                {
                    command = new ScheduleOptimizationCommand(
                        assetId: body.AssetId,
                        scheduleType: scheduleType.Value,
                        asset: asset,
                        horizonStart: body.HorizonStart,
                        horizonEnd: body.HorizonEnd,
                        timeStep: TimeSpan.FromSeconds(body.TimeStepSeconds),
                        pricesPerStep: body.PricesPerStep,
                        priceUnit: body.PriceUnit);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { error = "invalid-request", detail = ex.Message });
                }

                var outcome = await useCase.ExecuteAsync(command, ct).ConfigureAwait(false);
                return Results.Ok(new OptimizationResponse(
                    RunId: outcome.RunId,
                    Status: outcome.Status,
                    HorizonStart: command.HorizonStart,
                    HorizonEnd: command.HorizonEnd,
                    ProducedScheduleVersion: outcome.ProducedScheduleVersion,
                    TerminationReason: outcome.TerminationReason));
            })
            .RequireAuthorization(AuthConstants.OperatorPolicy)
            .WithName("DayAheadOptimize")
            .WithSummary("Trigger a day-ahead schedule optimisation (LH-API-005).");
    }

    private static void MapOptimizationRunStatus(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/optimization/runs/{runId:guid}", async (
                Guid runId,
                IOptimizationRunRepository runs,
                CancellationToken ct) =>
            {
                var run = await runs.FindByIdAsync(runId, ct).ConfigureAwait(false);
                return run is null
                    ? Results.NotFound(new { error = "optimization-run-not-found", run_id = runId })
                    : Results.Ok(OptimizationRunResponse.From(run));
            })
            .WithName("OptimizationRunStatus")
            .WithSummary("Status and persisted payload for an optimisation run (LH-API-005).");
    }

    private static ScheduleType? ParseScheduleType(string? value) => value switch
    {
        "day_ahead" => ScheduleType.DayAhead,
        "intraday" => ScheduleType.Intraday,
        "regel_leistung_reserve" => ScheduleType.RegelLeistungReserve,
        _ => null,
    };
}
