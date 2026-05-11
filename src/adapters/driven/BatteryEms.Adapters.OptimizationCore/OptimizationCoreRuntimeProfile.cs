namespace BatteryEms.Adapters.OptimizationCore;

// Plan-RM-M5-01-A + ADR 0005 §2: adapter-lokales RuntimeProfile-Field
// analog zur OPC-UA-Linie aus M4-05. `Production` ist der Default —
// jeder Operator, der nicht produktiv fährt (HIL-TestSidecar, lokale
// Entwicklung gegen einen plaintext-Endpoint), muss explizit umstellen.
//
// Production-Profile macht plaintext-TCP-Endpoints unwirksam (D-02
// aus plan-RM-M5-01) — `EnsureValid` wirft
// `optimization-core-not-hardened-in-production` bei Production +
// `http://`-Endpoint.
public enum OptimizationCoreRuntimeProfile
{
    // Lokale Entwicklung gegen ein plaintext-TCP-Sidecar (z.B.
    // `localhost:5001` ohne TLS) oder ein In-Process-TestSidecar im
    // Unit-Test-Pfad.
    Development,

    // HIL-Pfad gegen den Embedded TestSidecar (Sub-Slice B liefert
    // den Grpc.AspNetCore-WebApplicationFactory-basierten
    // TestSidecar-Host). Identisches Verhalten wie `Development`,
    // semantisch markiert für die Integration-Test-Linie.
    HilSimulator,

    // Produktiv-Default. `unix://...` oder `https://...` Pflicht;
    // plaintext-`http://`-Endpoints fail-closed im EnsureValid.
    Production,
}
