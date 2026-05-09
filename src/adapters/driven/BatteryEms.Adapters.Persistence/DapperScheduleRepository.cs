using BatteryEms.Application.Markets;
using BatteryEms.Domain;
using Dapper;
using Npgsql;

namespace BatteryEms.Adapters.Persistence;

public sealed class DapperScheduleRepository : IScheduleRepository
{
    // Replace is intentionally destructive: M1 keeps only the latest
    // schedule per (asset, type). The new schedule's windows fully
    // replace the previous set inside a single transaction so a reader
    // never sees a torn schedule mid-rewrite. RM-M1-14 will own the
    // historical retention story.
    //
    // Concurrency (RM-M3-FUP-02): the header write is split into two
    // explicit branches gated on expectedBaseVersion. Insert path
    // (`expectedBaseVersion == 0`) uses ON CONFLICT DO NOTHING so a
    // pre-existing row surfaces as RowsAffected=0 and a Conflict
    // exception (with the actual version probed back) — never as a
    // silent overwrite. Update path (`> 0`) uses a plain UPDATE with
    // `WHERE version = @expected`; RowsAffected != 1 means the row is
    // gone or the version moved → Conflict exception.
    private const string DeleteWindowsSql = "DELETE FROM schedule_windows WHERE asset_id = @AssetId AND type = @Type;";
    private const string InsertHeaderSql = """
        INSERT INTO schedules (asset_id, type, market_bid_area, version)
        VALUES (@AssetId, @Type, @MarketBidArea, @Version)
        ON CONFLICT (asset_id, type) DO NOTHING;
        """;
    private const string UpdateHeaderWithCasSql = """
        UPDATE schedules
        SET market_bid_area = @MarketBidArea, version = @Version
        WHERE asset_id = @AssetId
          AND type = @Type
          AND version = @ExpectedBaseVersion;
        """;
    private const string SelectHeaderVersionSql = """
        SELECT version FROM schedules
        WHERE asset_id = @AssetId AND type = @Type;
        """;
    private const string InsertWindowSql = """
        INSERT INTO schedule_windows (asset_id, type, window_start, window_end, target_power_kw)
        VALUES (@AssetId, @Type, @WindowStart, @WindowEnd, @TargetPowerKw);
        """;

    private const string SelectHeaderSql = """
        SELECT asset_id, type, market_bid_area, version
        FROM schedules
        WHERE asset_id = @AssetId AND type = @Type;
        """;
    private const string SelectAllHeadersSql = """
        SELECT asset_id, type, market_bid_area, version
        FROM schedules
        WHERE asset_id = @AssetId;
        """;
    private const string SelectWindowsSql = """
        SELECT window_start, window_end, target_power_kw
        FROM schedule_windows
        WHERE asset_id = @AssetId AND type = @Type
        ORDER BY window_start ASC;
        """;

    private readonly NpgsqlDataSource _dataSource;

    public DapperScheduleRepository(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        DapperConfig.EnsureConfigured();
        _dataSource = dataSource;
    }

    public IEnumerable<Schedule> FindAll(string assetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        // Sync entry point on the IScheduleRepository port (matches the
        // existing in-memory adapter) — block on the async path and let
        // the runtime exception bubble if the connection fails. The API/
        // worker that calls this is already inside an async pipeline; if
        // sync becomes a hot path we can lift the port to async later.
        return FindAllAsync(assetId).GetAwaiter().GetResult();
    }

    public Schedule? FindActive(string assetId, ScheduleType type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        return FindActiveAsync(assetId, type).GetAwaiter().GetResult();
    }

    public void Replace(Schedule schedule, int expectedBaseVersion)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ReplaceAsync(schedule, expectedBaseVersion).GetAwaiter().GetResult();
    }

    public async Task<IReadOnlyList<Schedule>> FindAllAsync(string assetId, CancellationToken cancellationToken = default)
    {
        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var headers = await connection.QueryAsync<ScheduleRow>(new CommandDefinition(
                SelectAllHeadersSql,
                new { AssetId = assetId },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            var schedules = new List<Schedule>();
            foreach (var header in headers)
            {
                var windows = await LoadWindowsAsync(connection, header.AssetId, header.Type, cancellationToken).ConfigureAwait(false);
                schedules.Add(BuildSchedule(header, windows));
            }
            return schedules;
        }
    }

    public async Task<Schedule?> FindActiveAsync(string assetId, ScheduleType type, CancellationToken cancellationToken = default)
    {
        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var header = await connection.QuerySingleOrDefaultAsync<ScheduleRow>(new CommandDefinition(
                SelectHeaderSql,
                new { AssetId = assetId, Type = ScheduleTypeWire.ToWire(type) },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (header is null)
            {
                return null;
            }
            var windows = await LoadWindowsAsync(connection, header.AssetId, header.Type, cancellationToken).ConfigureAwait(false);
            return BuildSchedule(header, windows);
        }
    }

    public async Task ReplaceAsync(Schedule schedule, int expectedBaseVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (expectedBaseVersion < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedBaseVersion),
                "expectedBaseVersion must be >= 0 (0 = no prior version).");
        }

        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                var typeWire = ScheduleTypeWire.ToWire(schedule.Type);

                // Header CAS, branch on insert-vs-update. Probe the actual
                // version on conflict so the exception carries useful
                // diagnostic detail.
                int rows;
                if (expectedBaseVersion == 0)
                {
                    rows = await connection.ExecuteAsync(new CommandDefinition(
                        InsertHeaderSql,
                        new
                        {
                            AssetId = schedule.AssetId,
                            Type = typeWire,
                            MarketBidArea = schedule.MarketBidArea,
                            Version = schedule.Version,
                        },
                        transaction: transaction,
                        cancellationToken: cancellationToken)).ConfigureAwait(false);
                }
                else
                {
                    rows = await connection.ExecuteAsync(new CommandDefinition(
                        UpdateHeaderWithCasSql,
                        new
                        {
                            AssetId = schedule.AssetId,
                            Type = typeWire,
                            MarketBidArea = schedule.MarketBidArea,
                            Version = schedule.Version,
                            ExpectedBaseVersion = expectedBaseVersion,
                        },
                        transaction: transaction,
                        cancellationToken: cancellationToken)).ConfigureAwait(false);
                }

                if (rows != 1)
                {
                    var actual = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
                        SelectHeaderVersionSql,
                        new { AssetId = schedule.AssetId, Type = typeWire },
                        transaction: transaction,
                        cancellationToken: cancellationToken)).ConfigureAwait(false);
                    // Rollback before throwing so the windows DELETE/INSERT
                    // we are about to skip is also undone (defence in depth
                    // — `await using` would dispose-rollback as well, but
                    // an explicit Rollback documents the intent).
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    throw new ScheduleConcurrencyConflictException(
                        schedule.AssetId,
                        schedule.Type,
                        expectedBaseVersion,
                        actualVersion: actual ?? 0);
                }

                await connection.ExecuteAsync(new CommandDefinition(
                    DeleteWindowsSql,
                    new { AssetId = schedule.AssetId, Type = typeWire },
                    transaction: transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
                foreach (var window in schedule.Windows)
                {
                    await connection.ExecuteAsync(new CommandDefinition(
                        InsertWindowSql,
                        new
                        {
                            AssetId = schedule.AssetId,
                            Type = typeWire,
                            WindowStart = window.Start,
                            WindowEnd = window.End,
                            TargetPowerKw = window.TargetPowerKw,
                        },
                        transaction: transaction,
                        cancellationToken: cancellationToken)).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<List<ScheduleWindowRow>> LoadWindowsAsync(
        NpgsqlConnection connection,
        string assetId,
        string typeWire,
        CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<ScheduleWindowRow>(new CommandDefinition(
            SelectWindowsSql,
            new { AssetId = assetId, Type = typeWire },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows.ToList();
    }

    private static Schedule BuildSchedule(ScheduleRow header, List<ScheduleWindowRow> windows)
    {
        var domainWindows = windows
            .Select(w => new ScheduleWindow(
                TimestampConverter.ToOffset(w.WindowStart),
                TimestampConverter.ToOffset(w.WindowEnd),
                w.TargetPowerKw))
            .ToArray();
        return new Schedule(
            header.AssetId,
            ScheduleTypeWire.FromWire(header.Type),
            header.MarketBidArea,
            header.Version,
            domainWindows);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated by Dapper via reflection.")]
    private sealed class ScheduleRow
    {
        public string AssetId { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public string MarketBidArea { get; init; } = string.Empty;
        public int Version { get; init; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated by Dapper via reflection.")]
    private sealed class ScheduleWindowRow
    {
        public DateTime WindowStart { get; init; }
        public DateTime WindowEnd { get; init; }
        public double TargetPowerKw { get; init; }
    }
}
