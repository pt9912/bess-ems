using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BatteryEms.Application.Mpc;

// Canonical RM-M5-02-D identity tuple for one MPC control-cycle run.
// The request id is derived from exactly the reproducibility axes that
// make a replay different: asset, tick, sample time, model, estimator
// variant/config, solver config and random seed.
public sealed record MpcRunIdentity(
    string MpcRequestId,
    string AssetId,
    long ControlCycleTickUtcMs,
    long SampleTimeMs,
    int HorizonLength,
    string MpcModelVersion,
    string StateEstimatorVariant,
    string SolverConfigHash,
    string EstimatorConfigHash,
    long RandomSeed,
    string NumerikStampJson,
    double P0FrobeniusDisplay,
    DeterministicMode DeterministicMode)
{
    public IReadOnlyDictionary<string, string> ToStamps() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["mpc_request_id"] = MpcRequestId,
            ["asset_id"] = AssetId,
            ["control_cycle_tick_utc_ms_truncated"] = ControlCycleTickUtcMs.ToString(CultureInfo.InvariantCulture),
            ["sample_time_ms"] = SampleTimeMs.ToString(CultureInfo.InvariantCulture),
            ["horizon_length"] = HorizonLength.ToString(CultureInfo.InvariantCulture),
            ["mpc_model_version"] = MpcModelVersion,
            ["state_estimator_variant"] = StateEstimatorVariant,
            ["estimator_variant"] = StateEstimatorVariant,
            ["solver_config_hash"] = SolverConfigHash,
            ["estimator_config_hash"] = EstimatorConfigHash,
            ["random_seed"] = RandomSeed.ToString(CultureInfo.InvariantCulture),
            ["numerik_stamp_json"] = NumerikStampJson,
            ["p0_frobenius_display"] = P0FrobeniusDisplay.ToString("R", CultureInfo.InvariantCulture),
            ["deterministic_mode"] = DeterministicMode.ToString(),
        };

    public static MpcRunIdentity Build(MpcRequest request, string stateEstimatorVariant)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateEstimatorVariant);

        var tickMs = request.CommandTick.ToUnixTimeMilliseconds();
        var sampleMs = (long)request.Options.SampleTime.TotalMilliseconds;
        var solverConfigHash = HashCanonical(BuildSolverConfigCanonical(request));
        var estimatorConfigHash = HashCanonical(BuildEstimatorConfigCanonical(request, stateEstimatorVariant));
        var seed = request.Options.RandomSeedOverride
            ?? DeriveDefaultSeed(request.AssetId, tickMs, sampleMs, request.Model.ModelVersion, stateEstimatorVariant, solverConfigHash, estimatorConfigHash);
        var p0Frobenius = FrobeniusNorm(request.Options.Estimator.InitialCovariance);
        var numerikStamp = BuildNumerikStampJson(request.Options.DeterministicMode);
        var identityCanonical = string.Join('\n',
            "mpc-run-identity-v1",
            $"asset_id={request.AssetId}",
            $"control_cycle_tick_utc_ms_truncated={tickMs.ToString(CultureInfo.InvariantCulture)}",
            $"sample_time_ms={sampleMs.ToString(CultureInfo.InvariantCulture)}",
            $"mpc_model_version={request.Model.ModelVersion}",
            $"state_estimator_variant={stateEstimatorVariant}",
            $"solver_config_hash={solverConfigHash}",
            $"estimator_config_hash={estimatorConfigHash}",
            $"random_seed={seed.ToString(CultureInfo.InvariantCulture)}");

        return new MpcRunIdentity(
            "mpc-" + HashCanonical(identityCanonical),
            request.AssetId,
            tickMs,
            sampleMs,
            request.Options.HorizonLength,
            request.Model.ModelVersion,
            stateEstimatorVariant,
            solverConfigHash,
            estimatorConfigHash,
            seed,
            numerikStamp,
            p0Frobenius,
            request.Options.DeterministicMode);
    }

    private static long DeriveDefaultSeed(
        string assetId,
        long tickMs,
        long sampleMs,
        string modelVersion,
        string stateEstimatorVariant,
        string solverConfigHash,
        string estimatorConfigHash)
    {
        var canonical = string.Join('\n',
            "mpc-default-seed-v1",
            $"asset_id={assetId}",
            $"control_cycle_tick_utc_ms_truncated={tickMs.ToString(CultureInfo.InvariantCulture)}",
            $"sample_time_ms={sampleMs.ToString(CultureInfo.InvariantCulture)}",
            $"mpc_model_version={modelVersion}",
            $"state_estimator_variant={stateEstimatorVariant}",
            $"solver_config_hash={solverConfigHash}",
            $"estimator_config_hash={estimatorConfigHash}");
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return BitConverter.ToInt64(bytes, 0) & long.MaxValue;
    }

    private static string BuildSolverConfigCanonical(MpcRequest request) =>
        string.Join('\n',
            "solver-config-v1",
            $"horizon_length={request.Options.HorizonLength.ToString(CultureInfo.InvariantCulture)}",
            $"sample_time_ticks={request.Options.SampleTime.Ticks.ToString(CultureInfo.InvariantCulture)}",
            $"deterministic_mode={request.Options.DeterministicMode}",
            $"time_limit_ticks={request.Options.Solver.TimeLimit.Ticks.ToString(CultureInfo.InvariantCulture)}",
            $"optimality_gap={request.Options.Solver.OptimalityGap.ToString("R", CultureInfo.InvariantCulture)}",
            $"max_iterations={request.Options.Solver.MaxIterations.ToString(CultureInfo.InvariantCulture)}",
            $"constraints={BuildConstraintsCanonical(request.Model.Constraints)}");

    private static string BuildEstimatorConfigCanonical(MpcRequest request, string stateEstimatorVariant) =>
        string.Join('\n',
            "estimator-config-v1",
            $"state_estimator_variant={stateEstimatorVariant}",
            $"max_consecutive_missing_measurements={request.Options.Estimator.MaxConsecutiveMissingMeasurements.ToString(CultureInfo.InvariantCulture)}",
            $"initial_covariance={BuildMatrixCanonical(request.Options.Estimator.InitialCovariance)}",
            $"process_noise={BuildMatrixCanonical(request.Options.Estimator.ProcessNoise)}",
            $"measurement_noise={BuildMatrixCanonical(request.Options.Estimator.MeasurementNoise)}");

    private static string BuildConstraintsCanonical(MpcConstraints constraints) =>
        string.Join('|',
            constraints.MinSocPercent.ToString("R", CultureInfo.InvariantCulture),
            constraints.MaxSocPercent.ToString("R", CultureInfo.InvariantCulture),
            constraints.MinActivePowerKw.ToString("R", CultureInfo.InvariantCulture),
            constraints.MaxActivePowerKw.ToString("R", CultureInfo.InvariantCulture),
            constraints.MaxRampKwPerSecond.ToString("R", CultureInfo.InvariantCulture));

    private static string BuildMatrixCanonical(MpcMatrix matrix) =>
        $"{matrix.Rows.ToString(CultureInfo.InvariantCulture)}x{matrix.Columns.ToString(CultureInfo.InvariantCulture)}:"
        + string.Join(',', matrix.Elements.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));

    private static string BuildNumerikStampJson(DeterministicMode deterministicMode) =>
        "{\"schema\":\"mpc-numerik-stamp-v1\",\"backend\":\"local_osqp\","
        + "\"floating_point\":\"ieee754-double\",\"deterministic_mode\":\""
        + deterministicMode
        + "\"}";

    private static double FrobeniusNorm(MpcMatrix matrix)
    {
        var sum = 0.0;
        foreach (var value in matrix.Elements)
        {
            sum += value * value;
        }
        return Math.Sqrt(sum);
    }

    private static string HashCanonical(string canonical) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToUpperInvariant();
}
