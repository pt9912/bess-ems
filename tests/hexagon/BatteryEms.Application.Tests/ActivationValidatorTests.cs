using BatteryEms.Application.Markets;
using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Application.Tests;

public sealed class ActivationValidatorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 9, 12, 0, 0, TimeSpan.Zero);

    private static RegelleistungActivation Activation(
        string sourceId = "tso-source-1",
        string activationId = "act-1",
        long sequenceNumber = 1,
        string payloadHash = "sha256:aaa",
        DateTimeOffset? signalTimestamp = null)
        => new(
            sourceId,
            activationId,
            sequenceNumber,
            signalTimestamp ?? Now,
            ReserveProduct.Afrr,
            ReserveDirection.Up,
            powerKw: 25,
            validFrom: Now,
            validUntil: Now + TimeSpan.FromMinutes(15),
            payloadHash);

    private static (ActivationValidator Validator, InMemoryActivationDedupeStore Dedupe, FakeClock Clock)
        BuildValidator(RegelleistungOptions? options = null)
    {
        var clock = new FakeClock { UtcNow = Now };
        var opts = options ?? new RegelleistungOptions();
        var dedupe = new InMemoryActivationDedupeStore(opts, clock);
        return (new ActivationValidator(opts, dedupe, clock), dedupe, clock);
    }

    [Fact]
    public async Task Happy_path_returns_accepted()
    {
        var (validator, _, _) = BuildValidator();

        var result = await validator.ValidateAsync(Activation(), TimebaseDebounceState.Initial);

        Assert.True(result.IsAccepted);
        Assert.Equal(ActivationValidationReasons.Accepted, result.ReasonCode);
    }

    // Plan §146 schema check: identifier format gate runs first.
    // Construction blocks empty/whitespace, so the validator catches
    // INTERNAL whitespace or control chars (newline, tab, control bytes
    // injected by a permissive source adapter).
    [Fact]
    public async Task Source_id_with_internal_whitespace_is_schema_invalid()
    {
        var (validator, _, _) = BuildValidator();

        var result = await validator.ValidateAsync(
            Activation(sourceId: "tso source 1"), TimebaseDebounceState.Initial);

        Assert.False(result.IsAccepted);
        Assert.Equal(ActivationValidationReasons.SchemaInvalid, result.ReasonCode);
    }

    [Fact]
    public async Task Activation_id_with_control_char_is_schema_invalid()
    {
        var (validator, _, _) = BuildValidator();

        var result = await validator.ValidateAsync(
            Activation(activationId: "act-1"), TimebaseDebounceState.Initial);

        Assert.False(result.IsAccepted);
        Assert.Equal(ActivationValidationReasons.SchemaInvalid, result.ReasonCode);
    }

    [Fact]
    public async Task Payload_hash_with_internal_whitespace_is_schema_invalid()
    {
        var (validator, _, _) = BuildValidator();

        var result = await validator.ValidateAsync(
            Activation(payloadHash: "sha256: deadbeef"), TimebaseDebounceState.Initial);

        Assert.False(result.IsAccepted);
        Assert.Equal(ActivationValidationReasons.SchemaInvalid, result.ReasonCode);
    }

    [Fact]
    public async Task Stale_signal_returns_timestamp_stale()
    {
        var (validator, _, clock) = BuildValidator();
        clock.UtcNow = Now + TimeSpan.FromSeconds(10);

        var result = await validator.ValidateAsync(Activation(), TimebaseDebounceState.Initial);

        Assert.Equal(ActivationValidationReasons.TimestampStale, result.ReasonCode);
    }

    [Fact]
    public async Task Future_skew_signal_returns_timestamp_future_skew()
    {
        var (validator, _, _) = BuildValidator();

        var result = await validator.ValidateAsync(
            Activation(signalTimestamp: Now + TimeSpan.FromSeconds(60)),
            TimebaseDebounceState.Initial);

        Assert.Equal(ActivationValidationReasons.TimestampFutureSkew, result.ReasonCode);
    }

    [Fact]
    public async Task Timebase_degraded_returns_timebase_degraded()
    {
        var (validator, _, _) = BuildValidator();
        var degraded = TimebaseDebounceState.Initial
            .Observe(true).Observe(true).Observe(true);
        Assert.Equal(TimebaseHealth.Degraded, degraded.Health);

        var result = await validator.ValidateAsync(Activation(), degraded);

        Assert.Equal(ActivationValidationReasons.TimebaseDegraded, result.ReasonCode);
    }

    [Fact]
    public async Task Replay_with_same_payload_returns_replay_idempotent()
    {
        var (validator, _, _) = BuildValidator();
        await validator.ValidateAsync(Activation(payloadHash: "sha256:abc"), TimebaseDebounceState.Initial);

        var result = await validator.ValidateAsync(
            Activation(payloadHash: "sha256:abc"), TimebaseDebounceState.Initial);

        Assert.Equal(ActivationValidationReasons.ReplayIdempotent, result.ReasonCode);
    }

    [Fact]
    public async Task Same_identity_with_mutated_payload_returns_dedupe_conflict()
    {
        var (validator, _, _) = BuildValidator();
        await validator.ValidateAsync(Activation(payloadHash: "sha256:original"), TimebaseDebounceState.Initial);

        var result = await validator.ValidateAsync(
            Activation(payloadHash: "sha256:tampered"), TimebaseDebounceState.Initial);

        Assert.Equal(ActivationValidationReasons.DedupeConflict, result.ReasonCode);
    }

    [Fact]
    public async Task Dedupe_store_invalid_propagates_to_pipeline()
    {
        var (validator, dedupe, _) = BuildValidator();
        dedupe.MarkInvalid();

        var result = await validator.ValidateAsync(Activation(), TimebaseDebounceState.Initial);

        Assert.Equal(ActivationValidationReasons.DedupeStoreInvalid, result.ReasonCode);
    }

    // Plan §146 ORDER PIN: this is the key cross-step contract — a
    // replay-hit (identity already stored with same payload) MUST NOT
    // short-circuit the pipeline back to replay-idempotent when the
    // timebase is Degraded. The Degraded gate sits before dedupe so
    // a compromised source can't bypass the freshness/timebase checks
    // by re-sending an old identifier.
    [Fact]
    public async Task Replay_hit_during_timebase_degraded_returns_timebase_degraded_not_replay_idempotent()
    {
        var (validator, _, _) = BuildValidator();
        // Pre-store the activation while healthy.
        await validator.ValidateAsync(Activation(payloadHash: "sha256:stored"), TimebaseDebounceState.Initial);

        // Now degrade the timebase and replay the same activation.
        var degraded = TimebaseDebounceState.Initial
            .Observe(true).Observe(true).Observe(true);

        var result = await validator.ValidateAsync(
            Activation(payloadHash: "sha256:stored"), degraded);

        Assert.Equal(ActivationValidationReasons.TimebaseDegraded, result.ReasonCode);
        // Negative pin: must NOT have leaked into the dedupe step.
        Assert.NotEqual(ActivationValidationReasons.ReplayIdempotent, result.ReasonCode);
    }

    // Plan §146 order pin (variant): a stale signal must not reach
    // the dedupe store either, even if its identity was previously
    // stored — the time validator gates the pipeline at step 2.
    [Fact]
    public async Task Stale_replay_returns_timestamp_stale_not_replay_idempotent()
    {
        var (validator, _, clock) = BuildValidator();
        await validator.ValidateAsync(Activation(payloadHash: "sha256:stored"), TimebaseDebounceState.Initial);

        clock.UtcNow = Now + TimeSpan.FromSeconds(10);
        var result = await validator.ValidateAsync(
            Activation(payloadHash: "sha256:stored"), TimebaseDebounceState.Initial);

        Assert.Equal(ActivationValidationReasons.TimestampStale, result.ReasonCode);
    }

    // Plan §146 "Schema-konformes-aber-misaligntes-Signal": a signal
    // with valid identifiers (passes step 1) but stale timestamp
    // (fails step 2) must return the LATER step's reason, not a fake
    // schema-invalid.
    [Fact]
    public async Task Schema_valid_but_stale_signal_returns_timestamp_stale()
    {
        var (validator, _, clock) = BuildValidator();
        clock.UtcNow = Now + TimeSpan.FromSeconds(10);

        var result = await validator.ValidateAsync(
            Activation(sourceId: "well-formed-id", activationId: "act-1"),
            TimebaseDebounceState.Initial);

        Assert.Equal(ActivationValidationReasons.TimestampStale, result.ReasonCode);
    }

    // The dedupe store itself does not emit AmbiguousDuplicate (Sub-
    // Slice E's use-case surfaces it for tied concurrent candidates).
    // The validator's mapping must still be wired so a future
    // AcceptResult source can return it without a missing-case throw.
    [Fact]
    public async Task Ambiguous_duplicate_from_dedupe_propagates_to_pipeline()
    {
        var clock = new FakeClock { UtcNow = Now };
        var fakeStore = new FixedAcceptStore(AcceptResult.RejectedAmbiguousDuplicate);
        var validator = new ActivationValidator(new RegelleistungOptions(), fakeStore, clock);

        var result = await validator.ValidateAsync(Activation(), TimebaseDebounceState.Initial);

        Assert.Equal(ActivationValidationReasons.AmbiguousDuplicate, result.ReasonCode);
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        var (validator, _, _) = BuildValidator();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => validator.ValidateAsync(Activation(), TimebaseDebounceState.Initial, cts.Token));
    }

    [Fact]
    public void Null_options_throws()
    {
        var (_, dedupe, clock) = BuildValidator();
        Assert.Throws<ArgumentNullException>(() => new ActivationValidator(null!, dedupe, clock));
    }

    [Fact]
    public void Null_dedupe_store_throws()
    {
        var clock = new FakeClock();
        Assert.Throws<ArgumentNullException>(() =>
            new ActivationValidator(new RegelleistungOptions(), null!, clock));
    }

    [Fact]
    public void Null_clock_throws()
    {
        var dedupe = new InMemoryActivationDedupeStore(new RegelleistungOptions(), new FakeClock());
        Assert.Throws<ArgumentNullException>(() =>
            new ActivationValidator(new RegelleistungOptions(), dedupe, null!));
    }

    private sealed class FixedAcceptStore : IActivationDedupeStore
    {
        private readonly AcceptResult _result;
        public FixedAcceptStore(AcceptResult result) => _result = result;
        public Task<AcceptResult> TryAcceptAsync(
            RegelleistungActivation activation,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
        public bool IsInvalid => _result == AcceptResult.RejectedDedupeStoreInvalid;
    }
}
