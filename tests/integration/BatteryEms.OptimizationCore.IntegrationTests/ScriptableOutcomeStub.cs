using System.Collections.Concurrent;
using BatteryEms.Adapters.OptimizationCore.Grpc.V1;
using Grpc.Core;

namespace BatteryEms.OptimizationCore.IntegrationTests;

// Per-Test-Queue-Stub: jedes Test pin'd eine konkrete Outcome-
// Konfiguration (Solver-Status, has_usable_solution, Schedule-Points,
// Progress-Updates, Mid-Stream-Delay). Deckt die Sidecar-Status-
// Taxonomie-Matrix plus Negative-Pfade (TimeLimit, Infeasible,
// Cancel-mid-stream, Crash).
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812",
    Justification = "Instantiated via DI by EmbeddedOptimizationCoreSidecar.")]
internal sealed class ScriptableOutcomeStub
    : BatteryEms.Adapters.OptimizationCore.Grpc.V1.OptimizationCore.OptimizationCoreBase
{
    private readonly ConcurrentQueue<Func<OptimizeRequest, IServerStreamWriter<OptimizeUpdate>, ServerCallContext, Task>>
        _scriptedOptimize = new();
    private HealthResponse _health = new()
    {
        Status = HealthResponse.Types.Status.Serving,
    };
    private VersionResponse _version = BuildDefaultVersion();

    public void SetHealth(HealthResponse.Types.Status status)
    {
        _health = new HealthResponse { Status = status };
    }

    public void SetVersion(string contractVersion, string min, string max, params string[] features)
    {
        var response = new VersionResponse
        {
            ContractVersion = contractVersion,
            MinCompatibleVersion = min,
            MaxCompatibleVersion = max,
        };
        response.Features.AddRange(features);
        _version = response;
    }

    public void EnqueueOptimize(
        Func<OptimizeRequest, IServerStreamWriter<OptimizeUpdate>, ServerCallContext, Task> handler)
    {
        _scriptedOptimize.Enqueue(handler);
    }

    public override Task<HealthResponse> Health(
        HealthRequest request, ServerCallContext context)
        => Task.FromResult(_health);

    public override Task<VersionResponse> Version(
        VersionRequest request, ServerCallContext context)
        => Task.FromResult(_version);

    public override Task Optimize(
        OptimizeRequest request,
        IServerStreamWriter<OptimizeUpdate> responseStream,
        ServerCallContext context)
    {
        if (!_scriptedOptimize.TryDequeue(out var handler))
        {
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition,
                "no scripted optimize handler enqueued for this call"));
        }
        return handler(request, responseStream, context);
    }

    private static VersionResponse BuildDefaultVersion()
    {
        var response = new VersionResponse
        {
            ContractVersion = "1.0.0",
            MinCompatibleVersion = "1.0.0",
            MaxCompatibleVersion = "1.0.0",
        };
        response.Features.Add("has-usable-solution");
        return response;
    }
}
