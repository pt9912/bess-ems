using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Domain.Tests;

public sealed class RetentionPolicyTests
{
    [Fact]
    public void AuditPreserved_default_has_all_classes_null()
    {
        // LH-PERSIST-006 default contract: nothing is auto-deleted without
        // an explicit operator decision, and audit in particular stays
        // null even after the operator opts other classes in.
        var policy = RetentionPolicy.AuditPreserved;

        Assert.Null(policy.TelemetryRetention);
        Assert.Null(policy.CommandsRetention);
        Assert.Null(policy.SchedulesRetention);
        Assert.Null(policy.OperatorAuditRetention);
    }

    [Fact]
    public void EnsureValid_passes_for_null_or_non_negative_durations()
    {
        var policy = new RetentionPolicy(
            TelemetryRetention: TimeSpan.FromDays(90),
            CommandsRetention: TimeSpan.Zero,
            SchedulesRetention: null,
            OperatorAuditRetention: TimeSpan.FromDays(365));

        Assert.Same(policy, policy.EnsureValid());
    }

    [Theory]
    [InlineData(0, 0, 0, -1)]
    [InlineData(0, 0, -1, 0)]
    [InlineData(0, -1, 0, 0)]
    [InlineData(-1, 0, 0, 0)]
    public void EnsureValid_rejects_negative_durations(int telSecs, int cmdSecs, int schedSecs, int auditSecs)
    {
        var policy = new RetentionPolicy(
            TelemetryRetention: TimeSpan.FromSeconds(telSecs),
            CommandsRetention: TimeSpan.FromSeconds(cmdSecs),
            SchedulesRetention: TimeSpan.FromSeconds(schedSecs),
            OperatorAuditRetention: TimeSpan.FromSeconds(auditSecs));

        Assert.Throws<ArgumentOutOfRangeException>(policy.EnsureValid);
    }

    [Fact]
    public void Records_with_same_durations_are_equal()
    {
        // Value semantics matter for diff-based config reload tests and
        // for "is the retention policy the same as it was on last boot?"
        // checks the worker may add later.
        var a = new RetentionPolicy(TimeSpan.FromDays(90), TimeSpan.FromDays(365), null, null);
        var b = new RetentionPolicy(TimeSpan.FromDays(90), TimeSpan.FromDays(365), null, null);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }
}
