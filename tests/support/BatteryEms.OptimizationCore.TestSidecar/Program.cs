using BatteryEms.Adapters.OptimizationCore.Grpc.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);
var grpcPort = ParsePort(Environment.GetEnvironmentVariable("BESS_TEST_SIDECAR_GRPC_PORT"), 8081);
var healthPort = ParsePort(Environment.GetEnvironmentVariable("BESS_TEST_SIDECAR_HEALTH_PORT"), 8082);

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(grpcPort, listen =>
    {
        listen.Protocols = HttpProtocols.Http2;
    });
    options.ListenAnyIP(healthPort, listen =>
    {
        listen.Protocols = HttpProtocols.Http1;
    });
});
builder.Services.AddGrpc();
builder.Services.AddSingleton(TestSidecarOptions.FromEnvironment());
builder.Services.AddSingleton<OptimizationCoreTestService>();

var app = builder.Build();
app.MapGet("/healthz", (TestSidecarOptions options) => Results.Ok(new
{
    status = "ok",
    sidecar_status = options.HealthStatus.ToString(),
    contract_version = options.ContractVersion,
}));
app.MapGrpcService<OptimizationCoreTestService>();
app.Run();

static int ParsePort(string? value, int defaultPort)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return defaultPort;
    }

    return int.TryParse(value, out var port) && port is > 0 and <= 65535
        ? port
        : throw new InvalidOperationException(
            $"Sidecar port environment value must be a valid TCP port, got `{value}`.");
}

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1812",
    Justification = "Instantiated by ASP.NET Core DI for the RM-M5-06 compose sidecar.")]
internal sealed partial class OptimizationCoreTestService : OptimizationCore.OptimizationCoreBase
{
    private readonly TestSidecarOptions _options;
    private readonly ILogger<OptimizationCoreTestService> _logger;

    public OptimizationCoreTestService(
        TestSidecarOptions options,
        ILogger<OptimizationCoreTestService> logger)
    {
        _options = options;
        _logger = logger;
    }

    public override Task<HealthResponse> Health(
        HealthRequest request,
        ServerCallContext context)
    {
        TestSidecarLog.LogHealth(_logger, _options.HealthStatus);
        return Task.FromResult(new HealthResponse
        {
            Status = _options.HealthStatus,
        });
    }

    public override Task<VersionResponse> Version(
        VersionRequest request,
        ServerCallContext context)
    {
        TestSidecarLog.LogVersion(_logger, _options.ContractVersion);
        var response = new VersionResponse
        {
            ContractVersion = _options.ContractVersion,
            MinCompatibleVersion = _options.MinCompatibleVersion,
            MaxCompatibleVersion = _options.MaxCompatibleVersion,
        };
        response.Features.Add("has-usable-solution");
        return Task.FromResult(response);
    }

    public override async Task Optimize(
        OptimizeRequest request,
        IServerStreamWriter<OptimizeUpdate> responseStream,
        ServerCallContext context)
    {
        TestSidecarLog.LogOptimizeStarted(_logger, request.RequestId, request.AssetId);
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
            SolverName = "rm-m5-06-compose-sidecar",
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
        for (var cursor = horizonStart; cursor < horizonEnd; cursor += timeStep)
        {
            var windowEnd = cursor + timeStep;
            if (windowEnd > horizonEnd)
            {
                windowEnd = horizonEnd;
            }

            result.SchedulePoints.Add(new SchedulePoint
            {
                WindowStart = Timestamp.FromDateTimeOffset(cursor),
                WindowEnd = Timestamp.FromDateTimeOffset(windowEnd),
                TargetPowerKw = 0.0,
            });
        }

        await responseStream.WriteAsync(new OptimizeUpdate { Result = result })
            .ConfigureAwait(false);
        TestSidecarLog.LogOptimizeCompleted(_logger, request.RequestId, request.AssetId);
    }
}

internal sealed record TestSidecarOptions(
    HealthResponse.Types.Status HealthStatus,
    string ContractVersion,
    string MinCompatibleVersion,
    string MaxCompatibleVersion)
{
    public static TestSidecarOptions FromEnvironment()
    {
        var status = Environment.GetEnvironmentVariable("BESS_TEST_SIDECAR_HEALTH") switch
        {
            null or "" or "serving" => HealthResponse.Types.Status.Serving,
            "not_serving" => HealthResponse.Types.Status.NotServing,
            var value => throw new InvalidOperationException(
                $"Unsupported BESS_TEST_SIDECAR_HEALTH `{value}`."),
        };

        return new TestSidecarOptions(
            status,
            Environment.GetEnvironmentVariable("BESS_TEST_SIDECAR_CONTRACT_VERSION") ?? "1.0.0",
            Environment.GetEnvironmentVariable("BESS_TEST_SIDECAR_MIN_VERSION") ?? "1.0.0",
            Environment.GetEnvironmentVariable("BESS_TEST_SIDECAR_MAX_VERSION") ?? "1.0.0");
    }
}

internal static partial class TestSidecarLog
{
    [LoggerMessage(EventId = 5601, Level = LogLevel.Information,
        Message = "optimization-core-test-sidecar health status={Status}")]
    public static partial void LogHealth(
        ILogger logger,
        HealthResponse.Types.Status status);

    [LoggerMessage(EventId = 5602, Level = LogLevel.Information,
        Message = "optimization-core-test-sidecar version contract_version={ContractVersion}")]
    public static partial void LogVersion(ILogger logger, string contractVersion);

    [LoggerMessage(EventId = 5603, Level = LogLevel.Information,
        Message = "optimization-core-test-sidecar optimize started request_id={RequestId} asset_id={AssetId}")]
    public static partial void LogOptimizeStarted(
        ILogger logger,
        string requestId,
        string assetId);

    [LoggerMessage(EventId = 5604, Level = LogLevel.Information,
        Message = "optimization-core-test-sidecar optimize completed request_id={RequestId} asset_id={AssetId}")]
    public static partial void LogOptimizeCompleted(
        ILogger logger,
        string requestId,
        string assetId);
}
