using BatteryEms.Application.Optimization;
using Microsoft.Extensions.Logging;

namespace BatteryEms.Adapters.OptimizationCore;

// `IScheduleOptimizer`-Adapter, der die M2-`ScheduleOptimizationRequest`
// in gRPC-Protobuf-Form übersetzt, gegen den optimization-core-Sidecar
// fährt und das Result via `OptimizationCoreStatusMapper` in M2-
// `ScheduleOptimizationResult` zurückübersetzt (plan-RM-M5-01-A D-01).
//
// **Sub-Slice-A-Skelett:** Konstruktor + EnsureValid-Aufruf für die
// Options sind hier; die konkrete Health+Version-Probe + Optimize-
// Streaming-Schleife + Translation-Logic landet in **RM-M5-01-B**
// (TestSidecar + erster Wire-Roundtrip-Pin). Der Skelett-`OptimizeAsync`
// wirft `NotImplementedException` mit einem klaren Sub-Slice-B-Marker
// — damit kein silenter Optimierer-Pfad in Production zündet bevor
// der Wire-Test grün ist.
internal sealed class OptimizationCoreScheduleOptimizer : IScheduleOptimizer
{
    private readonly OptimizationCoreClient _client;
    private readonly OptimizationCoreOptions _options;
    private readonly ILogger<OptimizationCoreScheduleOptimizer> _logger;

    public OptimizationCoreScheduleOptimizer(
        OptimizationCoreClient client,
        OptimizationCoreOptions options,
        ILogger<OptimizationCoreScheduleOptimizer> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        // Plan-RM-M5-01 D-02 Startup-Guard fires here if the operator
        // hasn't configured a hardened endpoint for Production.
        options.EnsureValid(logger);

        _client = client;
        _options = options;
        _logger = logger;
    }

    public Task<ScheduleOptimizationResult> OptimizeAsync(
        ScheduleOptimizationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // RM-M5-01-A Hand-off: Wire-Integration (Health-Probe →
        // Version-Negotiation → Optimize-Streaming → Mapper-Lookup →
        // ScheduleOptimizationResult-Konstruktion) lebt in RM-M5-01-B.
        // Sub-Slice A liefert nur die strukturelle Surface (Options,
        // Mapper, Registration). Tests in A pinnen die testable
        // Subsets; Wire-Tests sind B.
        throw new NotImplementedException(
            "optimization-core-wire-pending: OptimizeAsync wire integration "
            + "lands in RM-M5-01-B (TestSidecar + erste Roundtrip-Pins). "
            + "Bis dahin darf der Adapter nicht in Production registriert "
            + "werden — `BessHostOptions.OpcUaCoreEnabled`-Slot bleibt "
            + "opt-in und ist im Default-Compose-Profile false.");
    }
}
