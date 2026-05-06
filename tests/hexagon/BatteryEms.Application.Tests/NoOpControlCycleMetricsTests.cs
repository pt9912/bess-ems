using BatteryEms.Application.Observability;
using Xunit;

namespace BatteryEms.Application.Tests;

public sealed class NoOpControlCycleMetricsTests
{
    [Fact]
    public void Every_method_is_a_no_op_and_does_not_throw_for_typical_inputs()
    {
        var metrics = NoOpControlCycleMetrics.Instance;

        // Calls should be cheap, idempotent and side-effect-free —
        // exercising every method just so the dispatch table stays
        // covered for the next refactor.
        metrics.RecordCycleDuration("asset-1", TimeSpan.FromMilliseconds(5));
        metrics.IncrementInvalidSnapshot("asset-1", "no-snapshot");
        metrics.IncrementCommunicationError("asset-1", "modbus");
        metrics.RecordCommandLatency("asset-1", TimeSpan.FromMilliseconds(120));
        metrics.SetActivePowerKw("asset-1", 25);
        metrics.SetSocPercent("asset-1", 55);
        metrics.RecordSafeStop("asset-1", "no-snapshot");

        Assert.Same(NoOpControlCycleMetrics.Instance, NoOpControlCycleMetrics.Instance);
    }
}
