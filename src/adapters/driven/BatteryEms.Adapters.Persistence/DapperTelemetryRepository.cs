using BatteryEms.Application.Persistence;
using BatteryEms.Domain;
using Dapper;
using Npgsql;

namespace BatteryEms.Adapters.Persistence;

public sealed class DapperTelemetryRepository : ITelemetryRepository
{
    private const string InsertSql = """
        INSERT INTO telemetry (
            asset_id, recorded_at, soc_percent, soh_percent,
            active_power_kw, reactive_power_kvar, dc_voltage, dc_current,
            temperature_celsius, available, fault_status,
            data_quality_state, data_quality_reason)
        VALUES (
            @AssetId, @RecordedAt, @SocPercent, @SohPercent,
            @ActivePowerKw, @ReactivePowerKvar, @DcVoltage, @DcCurrent,
            @TemperatureCelsius, @Available, @FaultStatus,
            @DataQualityState, @DataQualityReason);
        """;

    private const string SelectRangeSql = """
        SELECT asset_id, recorded_at, soc_percent, soh_percent,
               active_power_kw, reactive_power_kvar, dc_voltage, dc_current,
               temperature_celsius, available, fault_status,
               data_quality_state, data_quality_reason
        FROM telemetry
        WHERE asset_id = @AssetId
          AND recorded_at >= @From
          AND recorded_at < @Until
        ORDER BY recorded_at ASC;
        """;

    private const string SelectLatestSql = """
        SELECT asset_id, recorded_at, soc_percent, soh_percent,
               active_power_kw, reactive_power_kvar, dc_voltage, dc_current,
               temperature_celsius, available, fault_status,
               data_quality_state, data_quality_reason
        FROM telemetry
        WHERE asset_id = @AssetId
        ORDER BY recorded_at DESC
        LIMIT 1;
        """;

    private readonly NpgsqlDataSource _dataSource;

    public DapperTelemetryRepository(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        DapperConfig.EnsureConfigured();
        _dataSource = dataSource;
    }

    public async Task AppendAsync(BatteryTelemetry telemetry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(telemetry);

        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await connection.ExecuteAsync(new CommandDefinition(InsertSql, ToRow(telemetry), cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<BatteryTelemetry>> QueryAsync(
        string assetId,
        DateTimeOffset from,
        DateTimeOffset until,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);

        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var rows = await connection.QueryAsync<TelemetryRow>(new CommandDefinition(
                SelectRangeSql,
                new { AssetId = assetId, From = from, Until = until },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            return rows.Select(FromRow).ToArray();
        }
    }

    public async Task<BatteryTelemetry?> FindLatestAsync(string assetId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);

        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var row = await connection.QuerySingleOrDefaultAsync<TelemetryRow>(new CommandDefinition(
                SelectLatestSql,
                new { AssetId = assetId },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            return row is null ? null : FromRow(row);
        }
    }

    private static object ToRow(BatteryTelemetry t) => new
    {
        AssetId = t.AssetId,
        RecordedAt = t.Timestamp,
        SocPercent = t.SocPercent,
        SohPercent = t.SohPercent,
        ActivePowerKw = t.ActivePowerKw,
        ReactivePowerKvar = t.ReactivePowerKvar,
        DcVoltage = t.DcVoltage,
        DcCurrent = t.DcCurrent,
        TemperatureCelsius = t.TemperatureCelsius,
        Available = t.Available,
        FaultStatus = t.FaultStatus,
        DataQualityState = t.DataQuality.Flag.ToString(),
        DataQualityReason = t.DataQuality.Reason,
    };

    private static BatteryTelemetry FromRow(TelemetryRow r)
    {
        var quality = Enum.TryParse<DataQualityState>(r.DataQualityState, out var state)
            ? new DataQuality(state, r.DataQualityReason)
            : DataQuality.ProtocolError($"unknown-data-quality-state:{r.DataQualityState}");

        return new BatteryTelemetry(
            Timestamp: TimestampConverter.ToOffset(r.RecordedAt),
            AssetId: r.AssetId,
            SocPercent: r.SocPercent,
            SohPercent: r.SohPercent,
            ActivePowerKw: r.ActivePowerKw,
            ReactivePowerKvar: r.ReactivePowerKvar,
            DcVoltage: r.DcVoltage,
            DcCurrent: r.DcCurrent,
            TemperatureCelsius: r.TemperatureCelsius,
            Available: r.Available,
            FaultStatus: r.FaultStatus,
            DataQuality: quality);
    }

    // Mutable {get;init;} class instead of a record positional ctor:
    // Dapper's MatchNamesWithUnderscores maps snake_case columns to
    // PascalCase property setters but does not match record constructor
    // parameters; using property setters keeps the SQL free of AS aliases.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated by Dapper via reflection.")]
    private sealed class TelemetryRow
    {
        public string AssetId { get; init; } = string.Empty;
        public DateTime RecordedAt { get; init; }
        public double SocPercent { get; init; }
        public double SohPercent { get; init; }
        public double ActivePowerKw { get; init; }
        public double ReactivePowerKvar { get; init; }
        public double DcVoltage { get; init; }
        public double DcCurrent { get; init; }
        public double TemperatureCelsius { get; init; }
        public bool Available { get; init; }
        public string FaultStatus { get; init; } = string.Empty;
        public string DataQualityState { get; init; } = string.Empty;
        public string DataQualityReason { get; init; } = string.Empty;
    }
}
