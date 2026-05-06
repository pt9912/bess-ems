using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Domain.Tests;

public sealed class AuditEventTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EnsureValid_passes_when_all_required_fields_are_set()
    {
        var ev = new AuditEvent(
            Timestamp: Now,
            Operator: "operator-1",
            Action: "operator-stop",
            TargetAssetId: "asset-1",
            Reason: "manual-shutdown",
            Outcome: "command-issued");

        // Returns the same instance unchanged.
        Assert.Same(ev, ev.EnsureValid());
    }

    [Fact]
    public void EnsureValid_allows_null_target_asset_id()
    {
        // System-wide actions (e.g. operator login, broker reconfig) carry
        // no asset target. The audit log must still capture them; null is
        // the explicit "not asset-scoped" signal.
        var ev = new AuditEvent(
            Timestamp: Now,
            Operator: "operator-1",
            Action: "operator-login",
            TargetAssetId: null,
            Reason: "shift-change",
            Outcome: "logged-in");

        Assert.Same(ev, ev.EnsureValid());
    }

    [Theory]
    [InlineData("", "operator-stop", "manual", "ok")]
    [InlineData("operator-1", "", "manual", "ok")]
    [InlineData("operator-1", "operator-stop", "", "ok")]
    [InlineData("operator-1", "operator-stop", "manual", "")]
    public void EnsureValid_rejects_blank_required_fields(string @operator, string action, string reason, string outcome)
    {
        var ev = new AuditEvent(Now, @operator, action, "asset-1", reason, outcome);

        Assert.Throws<ArgumentException>(ev.EnsureValid);
    }

    [Fact]
    public void Records_with_same_field_values_are_equal()
    {
        // Records' value semantics matter for diffing audit reads against
        // expected histories in tests and for de-duplicating concurrent
        // writes from operators if a future revision adds idempotency.
        var a = new AuditEvent(Now, "operator-1", "stop", "asset-1", "manual", "ok");
        var b = new AuditEvent(Now, "operator-1", "stop", "asset-1", "manual", "ok");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }
}
