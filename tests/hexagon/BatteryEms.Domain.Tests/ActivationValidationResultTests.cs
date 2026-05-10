using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Domain.Tests;

public sealed class ActivationValidationResultTests
{
    [Fact]
    public void Accepted_factory_marks_is_accepted_and_uses_canonical_reason_code()
    {
        var result = ActivationValidationResult.Accepted("ok");

        Assert.True(result.IsAccepted);
        Assert.Equal("accepted", result.ReasonCode);
        Assert.Equal(ActivationValidationReasons.Accepted, result.ReasonCode);
        Assert.Equal("ok", result.Details);
    }

    [Fact]
    public void Reject_factory_marks_not_accepted_and_propagates_reason_code()
    {
        var result = ActivationValidationResult.Reject(
            ActivationValidationReasons.TimestampStale, "stale 3000ms");

        Assert.False(result.IsAccepted);
        Assert.Equal("timestamp-stale", result.ReasonCode);
        Assert.Equal("stale 3000ms", result.Details);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Reject_with_empty_reason_code_throws(string reasonCode)
    {
        Assert.Throws<ArgumentException>(() =>
            ActivationValidationResult.Reject(reasonCode, "details"));
    }

    [Fact]
    public void Reject_with_accepted_reason_code_throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ActivationValidationResult.Reject(ActivationValidationReasons.Accepted, "x"));
    }

    // Pin canonical kebab-case reason codes; later sub-slices extend the
    // set, but the codes introduced through A/B/C must not silently rename.
    [Fact]
    public void Reason_code_constants_are_canonical_kebab_case()
    {
        Assert.Equal("accepted", ActivationValidationReasons.Accepted);
        Assert.Equal("schema-invalid", ActivationValidationReasons.SchemaInvalid);
        Assert.Equal("timestamp-stale", ActivationValidationReasons.TimestampStale);
        Assert.Equal("timestamp-future-skew", ActivationValidationReasons.TimestampFutureSkew);
        Assert.Equal("timebase-degraded", ActivationValidationReasons.TimebaseDegraded);
        Assert.Equal("replay-idempotent", ActivationValidationReasons.ReplayIdempotent);
        Assert.Equal("dedupe-conflict", ActivationValidationReasons.DedupeConflict);
        Assert.Equal("ambiguous-duplicate", ActivationValidationReasons.AmbiguousDuplicate);
        Assert.Equal("dedupe-store-invalid", ActivationValidationReasons.DedupeStoreInvalid);
    }
}
