namespace BatteryEms.Domain;

// One labelled component of the solver's objective value (LH-OPT-009
// "aufgeschlüsselte Kosten- und Erlöskomponenten"). Cost components
// are positive, revenue components are negative — sign convention
// matches the EMS's "discharge = positive" rule (LH §4.1) so the
// breakdown reads consistently with the rest of the domain.
//
// Unit is free-form per LH-OPT-008: it must spell out the engineering
// unit including the denominator (e.g. "EUR", "EUR/MWh", "EUR/kWh").
public sealed record OptimizationObjectiveComponent(
    string Name,
    double Value,
    string Unit)
{
    public OptimizationObjectiveComponent EnsureValid()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(Unit);
        if (!double.IsFinite(Value))
        {
            throw new ArgumentException(
                $"Component '{Name}' has non-finite value '{Value}'.",
                nameof(Value));
        }
        return this;
    }
}

// Breakdown of the solver's objective into named components. The total
// is materialised as Sum so callers don't have to recompute it; the
// constructor enforces that the breakdown is at least internally
// consistent (every component is finite, names are unique).
public sealed class OptimizationObjectiveBreakdown
{
    public IReadOnlyList<OptimizationObjectiveComponent> Components { get; }
    public double Sum { get; }

    public OptimizationObjectiveBreakdown(IReadOnlyList<OptimizationObjectiveComponent> components)
    {
        ArgumentNullException.ThrowIfNull(components);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var sum = 0.0;
        foreach (var component in components)
        {
            ArgumentNullException.ThrowIfNull(component);
            component.EnsureValid();
            if (!seen.Add(component.Name))
            {
                throw new ArgumentException(
                    $"Duplicate objective component '{component.Name}'.",
                    nameof(components));
            }
            sum += component.Value;
        }

        Components = components;
        Sum = sum;
    }

    public static OptimizationObjectiveBreakdown Empty { get; } =
        new(Array.Empty<OptimizationObjectiveComponent>());
}
