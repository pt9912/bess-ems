using BatteryEms.Domain;

namespace BatteryEms.Application.Optimization;

// Output of IScheduleOptimizer.OptimizeAsync. The Run carries the full
// LH-OPT-009 payload (RunId, solver status, objective breakdown, …);
// ProducedSchedule is the live Schedule the use case persists into the
// IScheduleRepository when the run is solution-bearing.
//
// Invariants (enforced at construction):
//   - Run.HasUsableSolution ⇔ ProducedSchedule != null
//   - Run.ProducedSchedule (the reference) matches the actual produced
//     Schedule's (AssetId, Type, Version) — the use case relies on this
//     to link OptimizationRun → Schedule version.
public sealed class ScheduleOptimizationResult
{
    public OptimizationRun Run { get; }
    public Schedule? ProducedSchedule { get; }

    public ScheduleOptimizationResult(OptimizationRun run, Schedule? producedSchedule)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (run.HasUsableSolution)
        {
            if (producedSchedule is null)
            {
                throw new ArgumentException(
                    $"Run status '{run.Status}' requires a ProducedSchedule.",
                    nameof(producedSchedule));
            }
            if (run.ProducedSchedule is null)
            {
                throw new ArgumentException(
                    $"Run status '{run.Status}' requires Run.ProducedSchedule to point at the produced schedule.",
                    nameof(run));
            }
            if (run.ProducedSchedule.AssetId != producedSchedule.AssetId
                || run.ProducedSchedule.Type != producedSchedule.Type
                || run.ProducedSchedule.Version != producedSchedule.Version)
            {
                throw new ArgumentException(
                    "Run.ProducedSchedule reference does not match the supplied Schedule's (AssetId, Type, Version).",
                    nameof(producedSchedule));
            }
        }
        else if (producedSchedule is not null)
        {
            throw new ArgumentException(
                $"Run status '{run.Status}' must not carry a ProducedSchedule.",
                nameof(producedSchedule));
        }

        Run = run;
        ProducedSchedule = producedSchedule;
    }
}
