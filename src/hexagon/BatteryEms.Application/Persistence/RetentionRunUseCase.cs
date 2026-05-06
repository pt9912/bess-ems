using BatteryEms.Application.Time;
using BatteryEms.Domain;

namespace BatteryEms.Application.Persistence;

// LH-PERSIST-006 retention orchestration. For each non-null retention in
// the policy, the use case computes a cutoff = now - retention and calls
// the corresponding delete method on the repository. Null retention
// entries are skipped without contacting the repository at all — that
// way a misconfigured / not-yet-set policy never deletes anything by
// accident, and audit specifically stays untouched until the operator
// explicitly opts in.
//
// The use case is intentionally stateless and synchronous-ish: it does
// not own a timer or scheduler. The Worker (RM-M1-19) will arrange the
// periodic invocation; M1-14 ships the orchestration plus tests.
public sealed class RetentionRunUseCase
{
    private readonly IRetentionRepository _repository;
    private readonly IClock _clock;

    public RetentionRunUseCase(IRetentionRepository repository, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);
        _repository = repository;
        _clock = clock;
    }

    public async Task<RetentionRunResult> ExecuteAsync(RetentionPolicy policy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        policy.EnsureValid();

        var now = _clock.UtcNow;
        long telemetry = 0;
        long commands = 0;
        long schedules = 0;
        long audit = 0;

        if (policy.TelemetryRetention is { } telemetryRetention)
        {
            telemetry = await _repository
                .DeleteTelemetryOlderThanAsync(now - telemetryRetention, cancellationToken)
                .ConfigureAwait(false);
        }

        if (policy.CommandsRetention is { } commandsRetention)
        {
            commands = await _repository
                .DeleteCommandsOlderThanAsync(now - commandsRetention, cancellationToken)
                .ConfigureAwait(false);
        }

        if (policy.SchedulesRetention is { } schedulesRetention)
        {
            schedules = await _repository
                .DeleteSchedulesOlderThanAsync(now - schedulesRetention, cancellationToken)
                .ConfigureAwait(false);
        }

        var auditPreserved = policy.OperatorAuditRetention is null;
        if (policy.OperatorAuditRetention is { } auditRetention)
        {
            audit = await _repository
                .DeleteOperatorAuditOlderThanAsync(now - auditRetention, cancellationToken)
                .ConfigureAwait(false);
        }

        return new RetentionRunResult(telemetry, commands, schedules, audit, auditPreserved);
    }
}
