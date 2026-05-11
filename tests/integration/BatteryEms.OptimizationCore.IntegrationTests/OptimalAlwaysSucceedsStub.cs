using BatteryEms.Adapters.OptimizationCore.Grpc.V1;
using Grpc.Core;

namespace BatteryEms.OptimizationCore.IntegrationTests;

// Echo-Stub: nimmt das `OptimizeRequest` und liefert ein
// `OptimizeResult` mit `solver_status=OPTIMAL`, `has_usable_solution=
// true` plus identisch geformten `SchedulePoint`s (Power 0kW pro
// Window). Wird vom Happy-Path-Pin verwendet.
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812",
    Justification = "Instantiated via DI by EmbeddedOptimizationCoreSidecar.")]
internal sealed class OptimalAlwaysSucceedsStub
    : BatteryEms.Adapters.OptimizationCore.Grpc.V1.OptimizationCore.OptimizationCoreBase
{
    public const string ContractVersion = "1.0.0";

    public override Task<HealthResponse> Health(
        HealthRequest request, ServerCallContext context)
    {
        return Task.FromResult(new HealthResponse
        {
            Status = HealthResponse.Types.Status.Serving,
        });
    }

    public override Task<VersionResponse> Version(
        VersionRequest request, ServerCallContext context)
    {
        var response = new VersionResponse
        {
            ContractVersion = ContractVersion,
            MinCompatibleVersion = "1.0.0",
            MaxCompatibleVersion = "1.0.0",
        };
        response.Features.Add("has-usable-solution");
        return Task.FromResult(response);
    }

    public override async Task Optimize(
        OptimizeRequest request,
        IServerStreamWriter<OptimizeUpdate> responseStream,
        ServerCallContext context)
    {
        // Ein progressives Update plus ein finales Result. Echo-Form:
        // ein Schedule-Window pro time_step, target_power_kw=0.
        await responseStream.WriteAsync(new OptimizeUpdate
        {
            Progress = new OptimizeProgress
            {
                StepIndex = 0,
                ObjectiveSoFar = 0.0,
            },
        }).ConfigureAwait(false);

        var result = new OptimizeResult
        {
            SolverStatus = OptimizeResult.Types.SolverStatus.Optimal,
            HasUsableSolution = true,
            SolutionQuality = "optimal",
            ObjectiveValue = 0.0,
            TerminationCode = "OPTIMAL",
            SolverName = "test-sidecar-optimal-stub",
            ObjectiveBreakdown = new ObjectiveBreakdown
            {
                EnergyCost = 0.0,
                DegradationCost = 0.0,
                SocTargetPenalty = 0.0,
            },
        };

        var horizonStart = request.HorizonStart.ToDateTimeOffset();
        var horizonEnd = request.HorizonEnd.ToDateTimeOffset();
        var timeStep = request.TimeStep.ToTimeSpan();
        var cursor = horizonStart;
        while (cursor < horizonEnd)
        {
            var windowEnd = cursor + timeStep;
            if (windowEnd > horizonEnd) { windowEnd = horizonEnd; }
            result.SchedulePoints.Add(new SchedulePoint
            {
                WindowStart = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(cursor),
                WindowEnd = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(windowEnd),
                TargetPowerKw = 0.0,
            });
            cursor = windowEnd;
        }

        await responseStream.WriteAsync(new OptimizeUpdate
        {
            Result = result,
        }).ConfigureAwait(false);
    }
}
