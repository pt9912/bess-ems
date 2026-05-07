using System.Text.Json;
using BatteryEms.Application.Persistence;
using BatteryEms.Domain;
using Dapper;
using Npgsql;

namespace BatteryEms.Adapters.Persistence;

// Append-only persistence for OptimizationRun (LH-PERSIST-007 /
// LH-OPT-009). Mirrors DapperScheduleRepository's shape — single
// NpgsqlDataSource ctor dependency, snake_case column ↔ PascalCase row
// mapping via DapperConfig, transaction wraps the multi-row aggregate
// write. Inputs / constraint-violations / warnings ride as JSON-text
// columns to keep the wire stable without taking a Dapper TypeHandler
// dependency for Postgres array binding.
public sealed class DapperOptimizationRunRepository : IOptimizationRunRepository
{
    private const string InsertRunSql = """
        INSERT INTO optimization_runs (
            run_id, asset_id, solver_name, status,
            horizon_start, horizon_end, time_step_seconds,
            objective_value, constraint_violations_json, warnings_json,
            solver_runtime_seconds, termination_reason, created_at,
            inputs_json,
            produced_schedule_asset_id, produced_schedule_type, produced_schedule_version)
        VALUES (
            @RunId, @AssetId, @SolverName, @Status,
            @HorizonStart, @HorizonEnd, @TimeStepSeconds,
            @ObjectiveValue, @ConstraintViolationsJson, @WarningsJson,
            @SolverRuntimeSeconds, @TerminationReason, @CreatedAt,
            @InputsJson,
            @ProducedScheduleAssetId, @ProducedScheduleType, @ProducedScheduleVersion);
        """;

    private const string InsertComponentSql = """
        INSERT INTO optimization_objective_breakdowns (
            run_id, position, name, value, unit)
        VALUES (
            @RunId, @Position, @Name, @Value, @Unit);
        """;

    private const string SelectRunByIdSql = """
        SELECT run_id, asset_id, solver_name, status,
               horizon_start, horizon_end, time_step_seconds,
               objective_value, constraint_violations_json, warnings_json,
               solver_runtime_seconds, termination_reason, created_at,
               inputs_json,
               produced_schedule_asset_id, produced_schedule_type, produced_schedule_version
        FROM optimization_runs
        WHERE run_id = @RunId;
        """;

    private const string SelectRunsByAssetRangeSql = """
        SELECT run_id, asset_id, solver_name, status,
               horizon_start, horizon_end, time_step_seconds,
               objective_value, constraint_violations_json, warnings_json,
               solver_runtime_seconds, termination_reason, created_at,
               inputs_json,
               produced_schedule_asset_id, produced_schedule_type, produced_schedule_version
        FROM optimization_runs
        WHERE asset_id = @AssetId
          AND created_at >= @From
          AND created_at < @Until
        ORDER BY created_at ASC;
        """;

    private const string SelectComponentsByRunSql = """
        SELECT run_id, position, name, value, unit
        FROM optimization_objective_breakdowns
        WHERE run_id = @RunId
        ORDER BY position ASC;
        """;

    private const string SelectComponentsByRunsSql = """
        SELECT run_id, position, name, value, unit
        FROM optimization_objective_breakdowns
        WHERE run_id = ANY(@RunIds)
        ORDER BY run_id, position ASC;
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly NpgsqlDataSource _dataSource;

    public DapperOptimizationRunRepository(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        DapperConfig.EnsureConfigured();
        _dataSource = dataSource;
    }

    public async Task AppendAsync(OptimizationRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);

        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                try
                {
                    await connection.ExecuteAsync(new CommandDefinition(
                        InsertRunSql,
                        new
                        {
                            RunId = run.RunId,
                            AssetId = run.AssetId,
                            SolverName = run.SolverName,
                            Status = SolverStatusWire.ToWire(run.Status),
                            HorizonStart = run.HorizonStart,
                            HorizonEnd = run.HorizonEnd,
                            TimeStepSeconds = run.TimeStep.TotalSeconds,
                            ObjectiveValue = run.ObjectiveValue,
                            ConstraintViolationsJson = JsonSerializer.Serialize(run.ConstraintViolations, JsonOptions),
                            WarningsJson = JsonSerializer.Serialize(run.Warnings, JsonOptions),
                            SolverRuntimeSeconds = run.SolverRuntime.TotalSeconds,
                            TerminationReason = run.TerminationReason,
                            CreatedAt = run.CreatedAt,
                            InputsJson = SerializeInputs(run.Inputs),
                            ProducedScheduleAssetId = run.ProducedSchedule?.AssetId,
                            ProducedScheduleType = run.ProducedSchedule is null
                                ? null
                                : ScheduleTypeWire.ToWire(run.ProducedSchedule.Type),
                            ProducedScheduleVersion = (int?)run.ProducedSchedule?.Version,
                        },
                        transaction: transaction,
                        cancellationToken: cancellationToken)).ConfigureAwait(false);
                }
                catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
                {
                    // Append-only contract (LH-OPT-009): re-appending a
                    // RunId is rejected, matching InMemoryOptimizationRunRepository.
                    throw new InvalidOperationException(
                        $"OptimizationRun with id '{run.RunId}' already exists; runs are append-only.",
                        ex);
                }

                for (var position = 0; position < run.ObjectiveBreakdown.Components.Count; position++)
                {
                    var component = run.ObjectiveBreakdown.Components[position];
                    await connection.ExecuteAsync(new CommandDefinition(
                        InsertComponentSql,
                        new
                        {
                            RunId = run.RunId,
                            Position = position,
                            Name = component.Name,
                            Value = component.Value,
                            Unit = component.Unit,
                        },
                        transaction: transaction,
                        cancellationToken: cancellationToken)).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task<OptimizationRun?> FindByIdAsync(Guid runId, CancellationToken cancellationToken)
    {
        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var header = await connection.QuerySingleOrDefaultAsync<RunRow>(new CommandDefinition(
                SelectRunByIdSql,
                new { RunId = runId },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (header is null)
            {
                return null;
            }

            var components = await connection.QueryAsync<ComponentRow>(new CommandDefinition(
                SelectComponentsByRunSql,
                new { RunId = runId },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return BuildRun(header, components.ToList());
        }
    }

    public async Task<IReadOnlyList<OptimizationRun>> QueryAsync(
        string assetId,
        DateTimeOffset from,
        DateTimeOffset until,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        if (until < from)
        {
            throw new ArgumentException(
                "'until' must be greater than or equal to 'from'.", nameof(until));
        }

        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var headers = (await connection.QueryAsync<RunRow>(new CommandDefinition(
                SelectRunsByAssetRangeSql,
                new { AssetId = assetId, From = from, Until = until },
                cancellationToken: cancellationToken)).ConfigureAwait(false)).ToList();
            if (headers.Count == 0)
            {
                return Array.Empty<OptimizationRun>();
            }

            var runIds = headers.Select(h => h.RunId).ToArray();
            var componentsByRun = (await connection.QueryAsync<ComponentRow>(new CommandDefinition(
                SelectComponentsByRunsSql,
                new { RunIds = runIds },
                cancellationToken: cancellationToken)).ConfigureAwait(false))
                .GroupBy(c => c.RunId)
                .ToDictionary(g => g.Key, g => g.OrderBy(c => c.Position).ToList());

            return headers
                .Select(h => BuildRun(
                    h,
                    componentsByRun.TryGetValue(h.RunId, out var list) ? list : new List<ComponentRow>()))
                .ToArray();
        }
    }

    private static string SerializeInputs(IReadOnlyList<ScheduleReference> inputs)
    {
        var dtos = inputs
            .Select(i => new InputDto(i.AssetId, ScheduleTypeWire.ToWire(i.Type), i.Version))
            .ToArray();
        return JsonSerializer.Serialize(dtos, JsonOptions);
    }

    private static ScheduleReference[] DeserializeInputs(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<ScheduleReference>();
        }
        var dtos = JsonSerializer.Deserialize<InputDto[]>(json, JsonOptions)
            ?? Array.Empty<InputDto>();
        return dtos
            .Select(d => new ScheduleReference(
                d.AssetId,
                ScheduleTypeWire.FromWire(d.Type),
                d.Version))
            .ToArray();
    }

    private static string[] DeserializeStringList(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }
        return JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? Array.Empty<string>();
    }

    private static OptimizationRun BuildRun(RunRow header, List<ComponentRow> components)
    {
        var breakdown = components.Count == 0
            ? OptimizationObjectiveBreakdown.Empty
            : new OptimizationObjectiveBreakdown(components
                .Select(c => new OptimizationObjectiveComponent(c.Name, c.Value, c.Unit))
                .ToArray());

        ScheduleReference? produced = null;
        if (header.ProducedScheduleAssetId is not null
            && header.ProducedScheduleType is not null
            && header.ProducedScheduleVersion is int version)
        {
            produced = new ScheduleReference(
                header.ProducedScheduleAssetId,
                ScheduleTypeWire.FromWire(header.ProducedScheduleType),
                version);
        }

        return new OptimizationRun(
            runId: header.RunId,
            assetId: header.AssetId,
            solverName: header.SolverName,
            status: SolverStatusWire.FromWire(header.Status),
            horizonStart: TimestampConverter.ToOffset(header.HorizonStart),
            horizonEnd: TimestampConverter.ToOffset(header.HorizonEnd),
            timeStep: TimeSpan.FromSeconds(header.TimeStepSeconds),
            objectiveValue: header.ObjectiveValue,
            objectiveBreakdown: breakdown,
            constraintViolations: DeserializeStringList(header.ConstraintViolationsJson),
            warnings: DeserializeStringList(header.WarningsJson),
            solverRuntime: TimeSpan.FromSeconds(header.SolverRuntimeSeconds),
            terminationReason: header.TerminationReason,
            createdAt: TimestampConverter.ToOffset(header.CreatedAt),
            inputs: DeserializeInputs(header.InputsJson),
            producedSchedule: produced);
    }

    private sealed record InputDto(string AssetId, string Type, int Version);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated by Dapper via reflection.")]
    private sealed class RunRow
    {
        public Guid RunId { get; init; }
        public string AssetId { get; init; } = string.Empty;
        public string SolverName { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public DateTime HorizonStart { get; init; }
        public DateTime HorizonEnd { get; init; }
        public double TimeStepSeconds { get; init; }
        public double ObjectiveValue { get; init; }
        public string ConstraintViolationsJson { get; init; } = "[]";
        public string WarningsJson { get; init; } = "[]";
        public double SolverRuntimeSeconds { get; init; }
        public string TerminationReason { get; init; } = string.Empty;
        public DateTime CreatedAt { get; init; }
        public string InputsJson { get; init; } = "[]";
        public string? ProducedScheduleAssetId { get; init; }
        public string? ProducedScheduleType { get; init; }
        public int? ProducedScheduleVersion { get; init; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated by Dapper via reflection.")]
    private sealed class ComponentRow
    {
        public Guid RunId { get; init; }
        public int Position { get; init; }
        public string Name { get; init; } = string.Empty;
        public double Value { get; init; }
        public string Unit { get; init; } = string.Empty;
    }
}
