using System.Globalization;
using System.Text.RegularExpressions;
using BatteryEms.Application.Markets;
using BatteryEms.Application.Time;
using BatteryEms.Domain;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BatteryEms.Adapters.Persistence;

// RM-M4-03-B: Postgres-backed dedupe tracker for Regelleistung
// activation signals (plan-RM-M4-03 §145). Identity is
// (source_id, activation_id); replay detection compares payload_hash on
// ON-CONFLICT. Retention compaction runs inside the same transaction
// as the accept so a successful TryAccept either persists the new
// entry + drops stale/excess ones atomically, or leaves the table
// untouched.
//
// Tracker-Load fail-closed (master DoD sub-cases a/b/c/d):
//   (a) incompatible checkpoint — __schema_versions journal reports a
//       migration newer than this build's LatestKnownMigrationNumber;
//   (b) oversize checkpoint — any source has more rows than
//       RegelleistungOptions.MaxEntriesPerSource (compaction failed);
//   (c) partial corruption — any row has empty payload_hash or a
//       negative sequence_number (DDL guards NOT NULL but not the
//       semantic bounds);
//   (d) parse/decode fail — any unexpected exception during the
//       validation queries.
// All four set the in-memory _invalid flag; subsequent TryAccept
// returns RejectedDedupeStoreInvalid until ResetForRecovery() is
// called (or the host restarts the store). The flag is sticky on
// purpose: once the store has detected a corrupt checkpoint we
// refuse to accept new activations even if the underlying issue
// transitions back to clean — operator action is required.
public sealed partial class DapperActivationDedupeStore : IActivationDedupeStore, IDisposable
{
    // The highest migration number this assembly knows about. Bump
    // this in lockstep with new Migrations/RunOnce/00NN_*.sql files
    // — the (a) sub-case fires when the DB has a newer migration
    // than the running app, which would mean we cannot safely read
    // the dedupe table.
    //
    // 3 ⇐ RM-M5-01-C step 4: 0003_optimization_idempotency.sql adds
    // the worker-owned Idempotency-Tracker-Tabelle. Migration berührt
    // regelleistung_activations nicht; das Bumpen verhindert nur
    // false-positive incompatible-checkpoint-fail-closed-Treffer wenn
    // beide Migrations bereits angewendet sind.
    //
    // 5 ⇐ RM-M6-04: 0005_timescale_telemetry_hypertable.sql is an
    // optional telemetry-only migration. It does not change the
    // dedupe table, but the compatibility ceiling still tracks every
    // applied RunOnce migration.
    private const int LatestKnownMigrationNumber = 5;

    private const string InsertSql = """
        INSERT INTO regelleistung_activations
            (source_id, activation_id, sequence_number, signal_timestamp_utc,
             payload_hash, winner_chosen_at)
        VALUES (@SourceId, @ActivationId, @SequenceNumber, @SignalTimestampUtc,
                @PayloadHash, @WinnerChosenAt)
        ON CONFLICT (source_id, activation_id) DO NOTHING;
        """;

    private const string SelectPayloadHashSql = """
        SELECT payload_hash FROM regelleistung_activations
        WHERE source_id = @SourceId AND activation_id = @ActivationId;
        """;

    // Retention compaction: keep the most recent entry per source
    // unconditionally (rn = 1), and drop anything older than the
    // replay-window cutoff or beyond the per-source cap.
    private const string RetentionSql = """
        WITH ordered AS (
            SELECT source_id, activation_id, winner_chosen_at,
                   ROW_NUMBER() OVER (PARTITION BY source_id ORDER BY winner_chosen_at DESC) AS rn
            FROM regelleistung_activations
            WHERE source_id = @SourceId
        )
        DELETE FROM regelleistung_activations a
        USING ordered o
        WHERE a.source_id = o.source_id
          AND a.activation_id = o.activation_id
          AND o.rn > 1
          AND (o.winner_chosen_at < @Cutoff OR o.rn > @Max);
        """;

    private const string SelectLatestMigrationSql = """
        SELECT scriptname FROM __schema_versions
        ORDER BY scriptname DESC
        LIMIT 1;
        """;

    private const string CheckOversizeSql = """
        SELECT 1 FROM regelleistung_activations
        GROUP BY source_id
        HAVING COUNT(*) > @Max
        LIMIT 1;
        """;

    private const string CheckCorruptionSql = """
        SELECT 1 FROM regelleistung_activations
        WHERE payload_hash = '' OR sequence_number < 0
        LIMIT 1;
        """;

    private readonly NpgsqlDataSource _dataSource;
    private readonly RegelleistungOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<DapperActivationDedupeStore> _logger;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly object _stateGate = new();
    private bool _invalid;
    private bool _loaded;

    public DapperActivationDedupeStore(
        NpgsqlDataSource dataSource,
        RegelleistungOptions options,
        IClock clock,
        ILogger<DapperActivationDedupeStore> logger)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        options.EnsureValid();
        DapperConfig.EnsureConfigured();
        _dataSource = dataSource;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    public async Task<AcceptResult> TryAcceptAsync(
        RegelleistungActivation activation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activation);
        cancellationToken.ThrowIfCancellationRequested();

        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        lock (_stateGate)
        {
            if (_invalid)
            {
                return AcceptResult.RejectedDedupeStoreInvalid;
            }
        }

        try
        {
            return await DoTryAcceptAsync(activation, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Do not catch general exception types — fail-closed is the contract.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogAcceptFailed(_logger, ex);
            lock (_stateGate)
            {
                _invalid = true;
            }
            return AcceptResult.RejectedDedupeStoreInvalid;
        }
    }

    public void Dispose()
    {
        _loadGate.Dispose();
    }

    public bool IsInvalid
    {
        get { lock (_stateGate) { return _invalid; } }
    }

    // Idempotent: runs the four-sub-case validation on first call and
    // caches the result. Host wires this at startup so the first real
    // TryAccept doesn't pay the validation cost; tests can call it
    // explicitly to assert load behaviour.
    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateGate)
        {
            if (_loaded)
            {
                return;
            }
        }

        await _loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_stateGate)
            {
                if (_loaded)
                {
                    return;
                }
            }

            var valid = await ValidateAsync(cancellationToken).ConfigureAwait(false);
            lock (_stateGate)
            {
                _loaded = true;
                _invalid = !valid;
            }
        }
        finally
        {
            _loadGate.Release();
        }
    }

    // Operator-explicit recovery hook: clears the sticky invalid flag
    // and forces re-validation on the next TryAccept. Sub-Slice D's
    // health endpoint surfaces the recovery action.
    public void ResetForRecovery()
    {
        lock (_stateGate)
        {
            _invalid = false;
            _loaded = false;
        }
    }

    private async Task<bool> ValidateAsync(CancellationToken cancellationToken)
    {
        try
        {
            var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                // (a) DbUp journal: any migration newer than this app
                // knows about means we cannot trust the schema shape.
                var latestApplied = await connection.QueryFirstOrDefaultAsync<string?>(
                    new CommandDefinition(SelectLatestMigrationSql, cancellationToken: cancellationToken))
                    .ConfigureAwait(false);
                if (latestApplied is not null)
                {
                    var match = ScriptNumberRegex().Match(latestApplied);
                    if (match.Success)
                    {
                        var num = int.Parse(match.Groups["num"].Value, CultureInfo.InvariantCulture);
                        if (num > LatestKnownMigrationNumber)
                        {
                            LogIncompatibleCheckpoint(_logger, latestApplied, LatestKnownMigrationNumber);
                            return false;
                        }
                    }
                }

                // (b) Per-source oversize: a source with more rows than
                // MaxEntriesPerSource means a prior compaction did not
                // run to completion — fail-closed until operator clears it.
                var oversize = await connection.QueryFirstOrDefaultAsync<int?>(
                    new CommandDefinition(
                        CheckOversizeSql,
                        new { Max = _options.MaxEntriesPerSource },
                        cancellationToken: cancellationToken))
                    .ConfigureAwait(false);
                if (oversize.HasValue)
                {
                    LogOversizeCheckpoint(_logger, _options.MaxEntriesPerSource);
                    return false;
                }

                // (c) Partial-row corruption: DDL guards NOT NULL but
                // not the semantic bounds — empty payload_hash or
                // negative sequence_number indicates a tampered or
                // half-applied row.
                var corrupt = await connection.QueryFirstOrDefaultAsync<int?>(
                    new CommandDefinition(CheckCorruptionSql, cancellationToken: cancellationToken))
                    .ConfigureAwait(false);
                if (corrupt.HasValue)
                {
                    LogPartialCorruption(_logger);
                    return false;
                }
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Do not catch general exception types — fail-closed is the contract for sub-case (d).
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // (d) Catch-all for parse/decode failures and any other
            // unexpected error during validation — fail-closed.
            LogValidationFailed(_logger, ex);
            return false;
        }
    }

    private async Task<AcceptResult> DoTryAcceptAsync(
        RegelleistungActivation activation,
        CancellationToken cancellationToken)
    {
        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                var now = _clock.UtcNow;
                var rows = await connection.ExecuteAsync(new CommandDefinition(
                    InsertSql,
                    new
                    {
                        SourceId = activation.SourceId,
                        ActivationId = activation.ActivationId,
                        SequenceNumber = activation.SequenceNumber,
                        SignalTimestampUtc = activation.SignalTimestampUtc,
                        PayloadHash = activation.PayloadHash,
                        WinnerChosenAt = now,
                    },
                    transaction: transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

                if (rows == 0)
                {
                    // Identity already stored — probe payload to decide
                    // ReplayIdempotent vs RejectedDedupeConflict.
                    var existingHash = await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
                        SelectPayloadHashSql,
                        new
                        {
                            SourceId = activation.SourceId,
                            ActivationId = activation.ActivationId,
                        },
                        transaction: transaction,
                        cancellationToken: cancellationToken)).ConfigureAwait(false);
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    return string.Equals(existingHash, activation.PayloadHash, StringComparison.Ordinal)
                        ? AcceptResult.ReplayIdempotent
                        : AcceptResult.RejectedDedupeConflict;
                }

                var cutoff = now - ComputeRetentionWindow();
                await connection.ExecuteAsync(new CommandDefinition(
                    RetentionSql,
                    new
                    {
                        SourceId = activation.SourceId,
                        Cutoff = cutoff,
                        Max = _options.MaxEntriesPerSource,
                    },
                    transaction: transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return AcceptResult.Accepted;
            }
        }
    }

    private TimeSpan ComputeRetentionWindow()
    {
        var window = _options.MaxAge + _options.FutureSkewTolerance + _options.DedupeWindow;
        return window < TimeSpan.FromSeconds(60) ? TimeSpan.FromSeconds(60) : window;
    }

    [GeneratedRegex(@"\.(?<num>\d{4})_[^.]+\.sql$")]
    private static partial Regex ScriptNumberRegex();

    [LoggerMessage(EventId = 4001, Level = LogLevel.Error,
        Message = "Activation dedupe accept failed; marking store invalid.")]
    private static partial void LogAcceptFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Error,
        Message = "Dedupe store load failed: __schema_versions reports migration '{Script}' newer than known {Latest:D4}.")]
    private static partial void LogIncompatibleCheckpoint(ILogger logger, string script, int latest);

    [LoggerMessage(EventId = 4003, Level = LogLevel.Error,
        Message = "Dedupe store load failed: at least one source exceeds MaxEntriesPerSource={Max}.")]
    private static partial void LogOversizeCheckpoint(ILogger logger, int max);

    [LoggerMessage(EventId = 4004, Level = LogLevel.Error,
        Message = "Dedupe store load failed: at least one row has empty payload_hash or negative sequence_number.")]
    private static partial void LogPartialCorruption(ILogger logger);

    [LoggerMessage(EventId = 4005, Level = LogLevel.Error,
        Message = "Dedupe store load failed: unexpected error during validation.")]
    private static partial void LogValidationFailed(ILogger logger, Exception ex);
}
