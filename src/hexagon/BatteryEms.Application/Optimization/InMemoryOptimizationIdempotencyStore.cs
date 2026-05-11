using System.Collections.Concurrent;

namespace BatteryEms.Application.Optimization;

// In-Memory-Default-Impl für das Idempotency-Port. Reicht für
// Test-Scenarios und für Headless-Deployments ohne Postgres
// (Worker-Restart verliert dann allerdings den Tracker — eine
// produktionsnahe Persistenz lebt im Dapper-Adapter, RM-M5-01-C-Persistence-
// Slice). Atomare CAS-Operation via `ConcurrentDictionary`.
public sealed class InMemoryOptimizationIdempotencyStore
    : IOptimizationIdempotencyStore
{
    private readonly ConcurrentDictionary<string, OptimizationIdempotencyEntry> _store = new();

    public Task<OptimizationIdempotencyBeginResult> TryBeginAsync(
        string requestId,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        cancellationToken.ThrowIfCancellationRequested();

        var newEntry = new OptimizationIdempotencyEntry(
            RequestId: requestId,
            TerminalState: OptimizationTerminalState.Pending,
            TerminalReason: "none",
            RunId: null,
            ProducedVersion: null,
            CreatedAt: createdAt,
            CommittedAt: null);

        // `GetOrAdd` ist die atomare CAS-Operation: existiert kein
        // Eintrag, wird `newEntry` eingefügt; existiert einer, gibt
        // GetOrAdd den vorhandenen zurück. ReferenceEquals trennt
        // "neu" vs "existiert".
        var stored = _store.GetOrAdd(requestId, newEntry);
        var isNew = ReferenceEquals(stored, newEntry);
        return Task.FromResult(new OptimizationIdempotencyBeginResult(stored, isNew));
    }

    public Task<bool> TryFinalizeAsync(
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

        // CAS-Loop: lese den aktuellen Eintrag, baue den finalisierten,
        // versuche TryUpdate. Schlägt fehl wenn ein anderer Caller in
        // der Zwischenzeit den Eintrag geändert hat → Re-Read und
        // erneuter Versuch. Bei einem bereits-finalen Eintrag gewinnt
        // der bestehende Terminalzustand → return false.
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_store.TryGetValue(requestId, out var current))
            {
                // Kein Pending-Eintrag → TryFinalize ohne vorherige
                // TryBegin verboten.
                return Task.FromResult(false);
            }
            if (current.IsFinal)
            {
                return Task.FromResult(false);
            }
            var finalized = current with
            {
                TerminalState = terminalState,
                TerminalReason = terminalReason,
                RunId = runId,
                ProducedVersion = producedVersion,
                CommittedAt = committedAt,
            };
            if (_store.TryUpdate(requestId, finalized, current))
            {
                return Task.FromResult(true);
            }
            // Concurrent-Update-Loser → Re-Read.
        }
    }

    public Task<OptimizationIdempotencyEntry?> ReadAsync(
        string requestId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        cancellationToken.ThrowIfCancellationRequested();
        _store.TryGetValue(requestId, out var entry);
        return Task.FromResult<OptimizationIdempotencyEntry?>(entry);
    }
}
