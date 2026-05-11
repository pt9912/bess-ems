namespace BatteryEms.Adapters.OpcUa;

// Plan-RM-M4-05 §3 + D-01: adapter-lokales RuntimeProfile-Field.
// `Production` ist der Default — ein Operator, der nicht produktiv
// fährt (HIL-Simulator, lokale Entwicklung gegen einen None-Endpoint),
// muss explizit umstellen. Damit wird der Production-Code-Pfad
// fail-closed gegen Konfigurations-Drift.
//
// Cross-Adapter-RuntimeProfile-Source (F-12 aus M4-03-Followups) bleibt
// offen; sobald sie zündet, feedet sie dieses adapter-lokale Field aus
// einer globalen Quelle — ohne Schema-Bruch (D-01-Konsequenz).
public enum OpcUaRuntimeProfile
{
    // Lokale Entwicklung gegen ein None-Endpoint (z.B. ein
    // Vendor-Simulator ohne Cert-Support). Der AllowUnsecured-Bool-
    // Guard bleibt aktiv (LH-OPCUA-005 Bool-Achse).
    Development,

    // HIL-Pfad gegen den Embedded TestServer oder das
    // `bess-hil-simulator`-Image. Identisches Verhalten wie
    // `Development`, semantisch markiert für Test-Linien.
    HilSimulator,

    // Produktiv-Default. SecurityMode=None ist hier ein harter
    // Startup-Fehler **unabhängig** von AllowUnsecured
    // (plan-RM-M4-05 D-02).
    Production,
}
