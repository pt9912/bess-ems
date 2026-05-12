using System.Text.Json;

namespace BatteryEms.Application.Mpc;

public sealed record MpcRunRetentionPolicy
{
    public static readonly MpcRunRetentionPolicy Disabled = new(int.MaxValue, null);

    public int KeepLatestPerAsset { get; }
    public TimeSpan? MaxAge { get; }

    public MpcRunRetentionPolicy(int keepLatestPerAsset, TimeSpan? maxAge)
    {
        if (keepLatestPerAsset <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(keepLatestPerAsset), keepLatestPerAsset, "KeepLatestPerAsset must be positive.");
        }
        if (maxAge <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAge), maxAge, "MaxAge must be null or positive.");
        }

        KeepLatestPerAsset = keepLatestPerAsset;
        MaxAge = maxAge;
    }
}

public sealed record MpcRun
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public string MpcRequestId { get; }
    public string AssetId { get; }
    public DateTimeOffset ControlCycleTickUtc { get; }
    public TimeSpan SampleTime { get; }
    public string MpcModelVersion { get; }
    public string StateEstimatorVariant { get; }
    public string SolverConfigHash { get; }
    public string EstimatorConfigHash { get; }
    public long RandomSeed { get; }
    public string NumerikStampJson { get; }
    public double P0FrobeniusDisplay { get; }
    public DeterministicMode DeterministicMode { get; }
    public bool IsUsable { get; }
    public string TerminalReason { get; }
    public string? TrajectoryJson { get; }
    public string? TerminalStateJson { get; }
    public DateTimeOffset CreatedAtUtc { get; }

    public MpcRun(
        string mpcRequestId,
        string assetId,
        DateTimeOffset controlCycleTickUtc,
        TimeSpan sampleTime,
        string mpcModelVersion,
        string stateEstimatorVariant,
        string solverConfigHash,
        string estimatorConfigHash,
        long randomSeed,
        string numerikStampJson,
        double p0FrobeniusDisplay,
        DeterministicMode deterministicMode,
        bool isUsable,
        string terminalReason,
        string? trajectoryJson,
        string? terminalStateJson,
        DateTimeOffset createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mpcRequestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(mpcModelVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateEstimatorVariant);
        ArgumentException.ThrowIfNullOrWhiteSpace(solverConfigHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(estimatorConfigHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(numerikStampJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(terminalReason);
        if (sampleTime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleTime), sampleTime, "SampleTime must be positive.");
        }
        if (!double.IsFinite(p0FrobeniusDisplay) || p0FrobeniusDisplay < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(p0FrobeniusDisplay), p0FrobeniusDisplay, "P0FrobeniusDisplay must be finite and non-negative.");
        }

        MpcRequestId = mpcRequestId;
        AssetId = assetId;
        ControlCycleTickUtc = controlCycleTickUtc;
        SampleTime = sampleTime;
        MpcModelVersion = mpcModelVersion;
        StateEstimatorVariant = stateEstimatorVariant;
        SolverConfigHash = solverConfigHash;
        EstimatorConfigHash = estimatorConfigHash;
        RandomSeed = randomSeed;
        NumerikStampJson = numerikStampJson;
        P0FrobeniusDisplay = p0FrobeniusDisplay;
        DeterministicMode = deterministicMode;
        IsUsable = isUsable;
        TerminalReason = terminalReason;
        TrajectoryJson = trajectoryJson;
        TerminalStateJson = terminalStateJson;
        CreatedAtUtc = createdAtUtc;
    }

    public static MpcRun FromResult(
        MpcRequest request,
        MpcDispatchResult result,
        DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);

        return new MpcRun(
            result.RequestId,
            request.AssetId,
            request.CommandTick,
            request.Options.SampleTime,
            request.Model.ModelVersion,
            result.Stamps["state_estimator_variant"],
            result.Stamps["solver_config_hash"],
            result.Stamps["estimator_config_hash"],
            long.Parse(result.Stamps["random_seed"], System.Globalization.CultureInfo.InvariantCulture),
            result.Stamps["numerik_stamp_json"],
            double.Parse(result.Stamps["p0_frobenius_display"], System.Globalization.CultureInfo.InvariantCulture),
            request.Options.DeterministicMode,
            result.IsUsable,
            result.Reason,
            SerializeTrajectory(result.Trajectory),
            SerializeState(result.PosteriorState),
            createdAtUtc);
    }

    private static string? SerializeTrajectory(MpcTrajectory? trajectory)
    {
        if (trajectory is null)
        {
            return null;
        }

        return JsonSerializer.Serialize(
            new
            {
                sampleTimeMs = (long)trajectory.SampleTime.TotalMilliseconds,
                points = trajectory.Points.Select(p => new
                {
                    timeUtc = p.Time,
                    p.ActivePowerKw,
                    p.PredictedSocPercent,
                }),
            },
            JsonOptions);
    }

    private static string? SerializeState(MpcState? state)
    {
        if (state is null)
        {
            return null;
        }

        return JsonSerializer.Serialize(
            new
            {
                timestampUtc = state.Timestamp,
                state.Mean,
                covariance = new
                {
                    state.Covariance.Rows,
                    state.Covariance.Columns,
                    state.Covariance.Elements,
                },
            },
            JsonOptions);
    }
}
