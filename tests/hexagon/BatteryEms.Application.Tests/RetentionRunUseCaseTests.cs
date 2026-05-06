using BatteryEms.Application.Persistence;
using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Application.Tests;

public sealed class RetentionRunUseCaseTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Null_policy_for_a_class_skips_that_class_entirely()
    {
        var repo = new RecordingRepository();
        var useCase = new RetentionRunUseCase(repo, new FixedClock(Now));

        var result = await useCase.ExecuteAsync(RetentionPolicy.AuditPreserved, CancellationToken.None);

        // Nothing was called: every retention is null in the default
        // AuditPreserved policy. The use case must not contact the
        // repository at all in that case so a misconfigured system never
        // accidentally deletes data.
        Assert.Empty(repo.Calls);
        Assert.Equal(0, result.TotalDeleted);
        Assert.True(result.OperatorAuditPreserved);
    }

    [Fact]
    public async Task Audit_retention_is_only_applied_when_explicitly_configured()
    {
        var repo = new RecordingRepository();
        var useCase = new RetentionRunUseCase(repo, new FixedClock(Now));

        // Telemetry has a retention; audit does not. Audit must stay
        // untouched per LH-PERSIST-006.
        var policy = new RetentionPolicy(
            TelemetryRetention: TimeSpan.FromDays(30),
            CommandsRetention: null,
            SchedulesRetention: null,
            OperatorAuditRetention: null);

        var result = await useCase.ExecuteAsync(policy, CancellationToken.None);

        Assert.True(result.OperatorAuditPreserved);
        Assert.Equal(0, result.OperatorAuditDeleted);
        Assert.DoesNotContain(repo.Calls, c => c.Class == "operator_audit");
    }

    [Fact]
    public async Task Each_configured_class_calls_the_repository_with_now_minus_retention()
    {
        var repo = new RecordingRepository
        {
            TelemetryDeleted = 5,
            CommandsDeleted = 7,
            SchedulesDeleted = 1,
            OperatorAuditDeleted = 3,
        };
        var useCase = new RetentionRunUseCase(repo, new FixedClock(Now));

        var policy = new RetentionPolicy(
            TelemetryRetention: TimeSpan.FromDays(30),
            CommandsRetention: TimeSpan.FromDays(60),
            SchedulesRetention: TimeSpan.FromDays(90),
            OperatorAuditRetention: TimeSpan.FromDays(365));

        var result = await useCase.ExecuteAsync(policy, CancellationToken.None);

        Assert.Equal(5, result.TelemetryDeleted);
        Assert.Equal(7, result.CommandsDeleted);
        Assert.Equal(1, result.SchedulesDeleted);
        Assert.Equal(3, result.OperatorAuditDeleted);
        Assert.Equal(16, result.TotalDeleted);
        Assert.False(result.OperatorAuditPreserved);

        // Cutoffs are now - retention. Walk the recorded calls and verify
        // the offset matches what the policy carries.
        Assert.Equal(Now - TimeSpan.FromDays(30), repo.CutoffFor("telemetry"));
        Assert.Equal(Now - TimeSpan.FromDays(60), repo.CutoffFor("commands"));
        Assert.Equal(Now - TimeSpan.FromDays(90), repo.CutoffFor("schedules"));
        Assert.Equal(Now - TimeSpan.FromDays(365), repo.CutoffFor("operator_audit"));
    }

    [Fact]
    public async Task Negative_retention_in_policy_is_rejected_before_repository_is_touched()
    {
        var repo = new RecordingRepository();
        var useCase = new RetentionRunUseCase(repo, new FixedClock(Now));

        var policy = new RetentionPolicy(
            TelemetryRetention: TimeSpan.FromSeconds(-1),
            CommandsRetention: null,
            SchedulesRetention: null,
            OperatorAuditRetention: null);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => useCase.ExecuteAsync(policy, CancellationToken.None));
        Assert.Empty(repo.Calls);
    }

    private sealed class RecordingRepository : IRetentionRepository
    {
        public List<(string Class, DateTimeOffset Cutoff)> Calls { get; } = new();
        public long TelemetryDeleted { get; set; }
        public long CommandsDeleted { get; set; }
        public long SchedulesDeleted { get; set; }
        public long OperatorAuditDeleted { get; set; }

        public Task<long> DeleteTelemetryOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct)
        {
            Calls.Add(("telemetry", cutoff));
            return Task.FromResult(TelemetryDeleted);
        }

        public Task<long> DeleteCommandsOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct)
        {
            Calls.Add(("commands", cutoff));
            return Task.FromResult(CommandsDeleted);
        }

        public Task<long> DeleteSchedulesOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct)
        {
            Calls.Add(("schedules", cutoff));
            return Task.FromResult(SchedulesDeleted);
        }

        public Task<long> DeleteOperatorAuditOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct)
        {
            Calls.Add(("operator_audit", cutoff));
            return Task.FromResult(OperatorAuditDeleted);
        }

        public DateTimeOffset CutoffFor(string @class) =>
            Calls.First(c => c.Class == @class).Cutoff;
    }

    private sealed class FixedClock : BatteryEms.Application.Time.IClock
    {
        public FixedClock(DateTimeOffset now) { UtcNow = now; }
        public DateTimeOffset UtcNow { get; }
    }
}
