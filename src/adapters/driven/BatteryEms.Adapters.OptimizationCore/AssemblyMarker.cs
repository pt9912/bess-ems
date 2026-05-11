namespace BatteryEms.Adapters.OptimizationCore;

// Trockenlink-Type für Architektur-Boundary-Tests (analog zu den
// anderen Adapter-Projekten unter src/adapters/driven/). Greift den
// Adapter-Namespace per `typeof(AssemblyMarker).Assembly` ab, ohne
// auf Production-Typen anzeweisen.
internal static class AssemblyMarker { }
