using BatteryEms.Application.Optimization;
using Dapper;
using Npgsql;

namespace BatteryEms.Adapters.Persistence;

// RM-M5-01-C step 4: Postgres-backed Idempotency-Store für den
// optimization-core-Sidecar (plan-RM-M5 §Request-Idempotenz Und
// Retry). Atomare CAS via `INSERT ... ON CONFLICT (request_id) DO
// NOTHING` für TryBegin + `UPDATE ... WHERE terminal_state = 'Pending'`
// für TryFinalize. Restart-Replay-fest via PRIMARY KEY auf request_id.
//
// Lifecycle analog zu DapperActivationDedupeStore: ein Singleton mit
// geteilter NpgsqlDataSource (Connection-Pool). Kein eigener Tracker-
// Load-Pfad — anders als beim ActivationDedupeStore lädt der Worker
// keinen In-Memory-Spiegel, sondern fragt pro Request direkt die
// Tabelle ab.
public sealed class DapperOptimizationIdempotencyStore
    : IOptimizationIdempotencyStore
{
    private const string InsertSql = """
        INSERT INTO optimization_idempotency
            (request_id, terminal_state, terminal_reason,
             run_id, produced_version, created_at, committed_at)
        VALUES (@RequestId, @TerminalState, @TerminalReason,
                @RunId, @ProducedVersion, @CreatedAt, @CommittedAt)
        ON CONFLICT (request_id) DO NOTHING;
        """;

    private const string SelectSql = """
        SELECT request_id AS RequestId,
               terminal_state AS TerminalState,
               terminal_reason AS TerminalReason,
               run_id AS RunId,
               produced_version AS ProducedVersion,
               created_at AS CreatedAt,
               committed_at AS CommittedAt
        FROM optimization_idempotency
        WHERE request_id = @RequestId;
        """;

    // CAS-Update: nur wenn der aktuelle Terminalzustand noch Pending
    // ist. RowsAffected==1 ⇒ Win; ==0 ⇒ Loser (Eintrag schon final
    // oder existiert nicht).
    private const string UpdateSql = """
        UPDATE optimization_idempotency
        SET terminal_state = @TerminalState,
            terminal_reason = @TerminalReason,
            run_id = @RunId,
            produced_version = @ProducedVersion,
            committed_at = @CommittedAt
        WHERE request_id = @RequestId
          AND terminal_state = @PendingMarker;
        """;

    private readonly NpgsqlDataSource _dataSource;

    public DapperOptimizationIdempotencyStore(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        DapperConfig.EnsureConfigured();
        _dataSource = dataSource;
    }

    public async Task<OptimizationIdempotencyBeginResult> TryBeginAsync(
        string requestId,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        cancellationToken.ThrowIfCancellationRequested();

        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            // Atomarer Insert-Versuch. ON CONFLICT DO NOTHING gibt
            // bei `affected == 0` zurück, dass schon ein Eintrag
            // existiert. Wir lesen anschließend den persistierten
            // Stand und melden `IsNewlyCreated = (affected == 1)`.
            var affected = await connection.ExecuteAsync(
                new CommandDefinition(InsertSql, new
                {
                    RequestId = requestId,
                    TerminalState = OptimizationTerminalState.Pending.ToString(),
                    TerminalReason = "none",
                    RunId = (Guid?)null,
                    ProducedVersion = (int?)null,
                    CreatedAt = createdAt,
                    CommittedAt = (DateTimeOffset?)null,
                },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            var entry = await ReadEntryAsync(connection, requestId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"optimization-idempotency-store-invariant-broken: row for "
                    + $"request_id `{requestId}` vanished between insert and select.");

            return new OptimizationIdempotencyBeginResult(entry, IsNewlyCreated: affected == 1);
        }
    }

    public async Task<bool> TryFinalizeAsync(
        string requestId,
        OptimizationTerminalState terminalState,
        string terminalReason,
        Guid? runId,
        int? producedVersion,
        DateTimeOffset committedAt,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(terminalReason);
        if (terminalState == OptimizationTerminalState.Pending)
        {
            throw new ArgumentException(
                "TerminalState must not be Pending in TryFinalizeAsync.",
                nameof(terminalState));
        }
        cancellationToken.ThrowIfCancellationRequested();

        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var affected = await connection.ExecuteAsync(
                new CommandDefinition(UpdateSql, new
                {
                    RequestId = requestId,
                    TerminalState = terminalState.ToString(),
                    TerminalReason = terminalReason,
                    RunId = runId,
                    ProducedVersion = producedVersion,
                    CommittedAt = committedAt,
                    PendingMarker = OptimizationTerminalState.Pending.ToString(),
                },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            return affected == 1;
        }
    }

    public async Task<OptimizationIdempotencyEntry?> ReadAsync(
        string requestId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        cancellationToken.ThrowIfCancellationRequested();

        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            return await ReadEntryAsync(connection, requestId, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task<OptimizationIdempotencyEntry?> ReadEntryAsync(
        NpgsqlConnection connection,
        string requestId,
        CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<Row>(
            new CommandDefinition(SelectSql, new { RequestId = requestId },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (row is null) { return null; }
        var state = Enum.Parse<OptimizationTerminalState>(row.TerminalState);
        return new OptimizationIdempotencyEntry(
            RequestId: row.RequestId,
            TerminalState: state,
            TerminalReason: row.TerminalReason,
            RunId: row.RunId,
            ProducedVersion: row.ProducedVersion,
            CreatedAt: row.CreatedAt,
            CommittedAt: row.CommittedAt);
    }

    // Dapper-Mapping-Hilfsklasse — public-Properties matchen die
    // SELECT-Aliase oben. Bewusst sealed + mutable für Dapper.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812",
        Justification = "Instantiated by Dapper via reflection.")]
    private sealed class Row
    {
        public string RequestId { get; set; } = string.Empty;
        public string TerminalState { get; set; } = string.Empty;
        public string TerminalReason { get; set; } = string.Empty;
        public Guid? RunId { get; set; }
        public int? ProducedVersion { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? CommittedAt { get; set; }
    }
}
