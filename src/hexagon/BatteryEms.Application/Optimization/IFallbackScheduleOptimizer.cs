namespace BatteryEms.Application.Optimization;

// Plan-RM-M5 §Fallback-Matrix Korrektur-Pass: dedizierter Driven-
// Port für den **lokalen Optimierer-Fallback** (z. B. OR-Tools)
// hinter dem Sidecar-Adapter. Vertrag ist identisch zu
// `IScheduleOptimizer` — der Marker existiert ausschließlich zur
// DI-Disambiguierung, damit der `OptimizationCoreScheduleOptimizer`
// einen optionalen Fallback-Slot bekommen kann ohne sich selbst
// rekursiv über den primary `IScheduleOptimizer`-Slot anzurufen.
//
// Plan-§Fallback-Matrix-Default: `fallback_source=local_optimizer`
// wird gesetzt wenn der primary Sidecar-Pfad an Deadline /
// Unavailable / Crash scheitert und ein Fallback registriert ist.
// Ohne Registrierung gilt `fallback_reason=no_valid_plan` +
// Safe-Stop (plan-RM-M5 §Fallback-Matrix Zeile „Timeout/Deadline
// oder Unavailable vor Ergebnis").
public interface IFallbackScheduleOptimizer : IScheduleOptimizer
{
}
