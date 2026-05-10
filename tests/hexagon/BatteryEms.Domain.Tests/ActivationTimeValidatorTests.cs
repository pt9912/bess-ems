using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Domain.Tests;

public sealed class ActivationTimeValidatorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 9, 12, 0, 0, TimeSpan.Zero);

    private static readonly RegelleistungOptions DefaultOptions = new();

    private static RegelleistungActivation ActivationAt(DateTimeOffset signalTimestamp)
        => new(
            sourceId: "tso-source",
            activationId: "act-1",
            sequenceNumber: 1,
            signalTimestampUtc: signalTimestamp,
            product: ReserveProduct.Afrr,
            direction: ReserveDirection.Up,
            powerKw: 25,
            validFrom: Now,
            validUntil: Now + TimeSpan.FromMinutes(15),
            payloadHash: "sha256:abc");

    // Validity-window pin: a fresh signal at Now is accepted.
    [Fact]
    public void Fresh_signal_at_now_is_accepted()
    {
        var result = ActivationTimeValidator.Validate(
            ActivationAt(Now), Now, DefaultOptions);

        Assert.True(result.IsAccepted);
        Assert.Equal(ActivationValidationReasons.Accepted, result.ReasonCode);
    }

    // Stale-timestamp pin: age exactly at MaxAge is accepted (MaxAge is
    // inclusive); age beyond MaxAge fails timestamp-stale.
    [Fact]
    public void Signal_at_exactly_max_age_is_accepted()
    {
        var signal = Now - DefaultOptions.MaxAge;

        var result = ActivationTimeValidator.Validate(
            ActivationAt(signal), Now, DefaultOptions);

        Assert.True(result.IsAccepted);
    }

    [Fact]
    public void Signal_older_than_max_age_is_stale()
    {
        var signal = Now - DefaultOptions.MaxAge - TimeSpan.FromMilliseconds(1);

        var result = ActivationTimeValidator.Validate(
            ActivationAt(signal), Now, DefaultOptions);

        Assert.False(result.IsAccepted);
        Assert.Equal(ActivationValidationReasons.TimestampStale, result.ReasonCode);
    }

    // Future-skew pin: a signal slightly in the future, within tolerance,
    // is accepted.
    [Fact]
    public void Signal_in_future_within_tolerance_is_accepted()
    {
        var signal = Now + TimeSpan.FromMilliseconds(250);

        var result = ActivationTimeValidator.Validate(
            ActivationAt(signal), Now, DefaultOptions);

        Assert.True(result.IsAccepted);
    }

    [Fact]
    public void Signal_in_future_at_exactly_tolerance_is_accepted()
    {
        var signal = Now + DefaultOptions.FutureSkewTolerance;

        var result = ActivationTimeValidator.Validate(
            ActivationAt(signal), Now, DefaultOptions);

        Assert.True(result.IsAccepted);
    }

    // Negative-age fail-closed pin (clock-rollback as per-sample
    // detection, plan §7): a signal far in the future is rejected as
    // future-skew without needing cross-sample monotonic state.
    [Fact]
    public void Signal_in_future_beyond_tolerance_is_future_skew()
    {
        var signal = Now + DefaultOptions.FutureSkewTolerance + TimeSpan.FromMilliseconds(1);

        var result = ActivationTimeValidator.Validate(
            ActivationAt(signal), Now, DefaultOptions);

        Assert.False(result.IsAccepted);
        Assert.Equal(ActivationValidationReasons.TimestampFutureSkew, result.ReasonCode);
    }

    [Fact]
    public void Signal_far_in_future_is_future_skew_clock_rollback_per_sample()
    {
        // A 60-second future skew is way past tolerance — a single
        // sample like this is already a clock-rollback suspect and is
        // rejected fail-closed without any prior history needed.
        var signal = Now + TimeSpan.FromSeconds(60);

        var result = ActivationTimeValidator.Validate(
            ActivationAt(signal), Now, DefaultOptions);

        Assert.False(result.IsAccepted);
        Assert.Equal(ActivationValidationReasons.TimestampFutureSkew, result.ReasonCode);
    }

    [Fact]
    public void Custom_max_age_is_honoured()
    {
        var options = new RegelleistungOptions { MaxAge = TimeSpan.FromMilliseconds(500) };
        var signal = Now - TimeSpan.FromMilliseconds(750);

        var result = ActivationTimeValidator.Validate(
            ActivationAt(signal), Now, options);

        Assert.False(result.IsAccepted);
        Assert.Equal(ActivationValidationReasons.TimestampStale, result.ReasonCode);
    }

    [Fact]
    public void Null_activation_throws()
    {
        Assert.Throws<ArgumentNullException>(() => ActivationTimeValidator.Validate(
            null!, Now, DefaultOptions));
    }

    [Fact]
    public void Null_options_throws()
    {
        Assert.Throws<ArgumentNullException>(() => ActivationTimeValidator.Validate(
            ActivationAt(Now), Now, null!));
    }
}
