using BatteryEms.Application.Persistence;
using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Application.Tests;

public sealed class InMemoryOperatorAuditLogTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 5, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Query_on_empty_log_returns_empty_collection()
    {
        var log = new InMemoryOperatorAuditLog();
        var result = await log.QueryAsync(T0, T0 + TimeSpan.FromHours(1), CancellationToken.None);
        Assert.Empty(result);
    }

    [Fact]
    public async Task Append_then_query_returns_event_within_half_open_window()
    {
        var log = new InMemoryOperatorAuditLog();
        var ev = new AuditEvent(T0, "op", "operator-stop", "asset-1", "manual", "accepted");
        await log.AppendAsync(ev, CancellationToken.None);

        var hits = await log.QueryAsync(T0, T0 + TimeSpan.FromMinutes(1), CancellationToken.None);
        var miss = await log.QueryAsync(T0 + TimeSpan.FromMinutes(1), T0 + TimeSpan.FromHours(1), CancellationToken.None);

        Assert.Single(hits);
        Assert.Equal(ev, hits[0]);
        Assert.Empty(miss);
    }

    [Fact]
    public async Task Query_returns_events_in_timestamp_order_even_when_appended_out_of_order()
    {
        var log = new InMemoryOperatorAuditLog();
        var later = new AuditEvent(T0 + TimeSpan.FromMinutes(2), "op", "operator-stop", "a", "r", "accepted");
        var earlier = new AuditEvent(T0, "op", "operator-stop", "a", "r", "accepted");
        await log.AppendAsync(later, CancellationToken.None);
        await log.AppendAsync(earlier, CancellationToken.None);

        var ordered = await log.QueryAsync(T0, T0 + TimeSpan.FromHours(1), CancellationToken.None);
        Assert.Equal(new[] { earlier, later }, ordered);
    }

    [Fact]
    public async Task Append_throws_when_audit_event_is_blank_in_a_required_field()
    {
        var log = new InMemoryOperatorAuditLog();
        var blankReason = new AuditEvent(T0, "op", "operator-stop", "a", "", "accepted");
        await Assert.ThrowsAsync<ArgumentException>(() =>
            log.AppendAsync(blankReason, CancellationToken.None));
    }

    [Fact]
    public async Task Query_with_until_before_from_throws()
    {
        var log = new InMemoryOperatorAuditLog();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            log.QueryAsync(T0 + TimeSpan.FromMinutes(1), T0, CancellationToken.None));
    }
}
