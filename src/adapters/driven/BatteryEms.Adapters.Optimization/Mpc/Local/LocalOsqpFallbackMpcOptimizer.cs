using BatteryEms.Application.Mpc;

namespace BatteryEms.Adapters.Optimization.Mpc.Local;

public sealed class LocalOsqpFallbackMpcOptimizer : IFallbackMpcOptimizer
{
    private readonly DefaultMpcDispatchOrchestrator _inner;

    public LocalOsqpFallbackMpcOptimizer(IMpcStateEstimator estimator, LocalOsqpMpcSolver solver)
    {
        ArgumentNullException.ThrowIfNull(estimator);
        ArgumentNullException.ThrowIfNull(solver);
        _inner = new DefaultMpcDispatchOrchestrator(estimator, solver);
    }

    public Task<MpcDispatchResult> NextStepAsync(
        MpcRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _inner.NextStepAsync(ApplyFallbackProfile(request), cancellationToken);
    }

    private static MpcRequest ApplyFallbackProfile(MpcRequest request)
    {
        var profile = request.Options.SolverFallbackProfile;
        if (profile is null)
        {
            return request;
        }

        var fallbackOptions = new MpcOptions(
            request.Options.SampleTime,
            profile.HorizonLength,
            profile.Solver,
            request.Options.Estimator,
            request.Options.DeterministicMode,
            request.Options.RandomSeedOverride);

        return new MpcRequest(
            request.AssetId,
            request.CommandTick,
            request.Asset,
            request.LatestMeasurement,
            request.Model,
            fallbackOptions,
            request.PriorState);
    }
}
