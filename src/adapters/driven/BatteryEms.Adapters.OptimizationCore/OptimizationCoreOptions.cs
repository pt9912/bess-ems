using Microsoft.Extensions.Logging;

namespace BatteryEms.Adapters.OptimizationCore;

// Operator-tunable + Security-slot-bearing Options für den
// optimization-core-Sidecar-Adapter (plan-RM-M5-01-A + ADR 0005).
//
// EnsureValid macht den Production-Pfad fail-closed (D-02):
//
// * `RuntimeProfile=Production` + Endpoint-Scheme=`http` (plaintext) ⇒
//   `optimization-core-not-hardened-in-production`. UDS (`unix://`) und
//   `https://` sind die zwei akzeptierten Production-Topologien
//   (ADR 0005 §4 — UDS für Loopback-Default, mTLS für Cross-Host).
// * `RuntimeProfile=HilSimulator|Development` lässt alle Schemes
//   durch (lokale Test-Endpoints brauchen kein TLS).
//
// UDS-Filesystem-Permission-Check ist runtime (beim Connect), nicht
// Options-level — der Endpoint-String alleine kann die Mode-Bits
// nicht prüfen. Sub-Slice C ergänzt den Runtime-Check
// `optimization-core-uds-permissions-not-locked` im Client-
// Connect-Pfad.
public sealed record OptimizationCoreOptions
{
    // gRPC-Sidecar-Endpoint. Drei Forms akzeptiert:
    //   - `unix:///var/run/bess-ems/optimization-core.sock` (Loopback-
    //     Default, ADR 0005 §4)
    //   - `https://optimization-core.internal:8443` (Cross-Host mit
    //     mTLS-Pflicht, ADR 0005 §4)
    //   - `http://localhost:5001` (NUR Development/HilSimulator)
    public required Uri SidecarEndpoint { get; init; }

    // Plan-RM-M5-01-A §3: adapter-lokales RuntimeProfile-Field
    // analog zur OPC-UA-Linie. Default `Production` macht den
    // Production-Code-Pfad fail-closed gegen Konfigurations-Drift.
    public OptimizationCoreRuntimeProfile RuntimeProfile { get; init; }
        = OptimizationCoreRuntimeProfile.Production;

    // gRPC-`CallOptions.Deadline` für jeden Optimize-Aufruf. Sidecar
    // sieht den Deadline-Timestamp und kann seinen Solver mit einem
    // analogen Time-Limit konfigurieren. Default 60s — lang genug für
    // typische LP-Läufe, kurz genug damit ein hängender Sidecar nicht
    // den Control-Cycle blockiert (Fallback-Matrix `deadline_exceeded`-
    // Pfad zündet stattdessen).
    public TimeSpan RequestDeadline { get; init; } = TimeSpan.FromSeconds(60);

    // Connect-Timeout für die initiale gRPC-Channel-Etablierung
    // (Health-Probe + Version-Negotiation). Default 10s — kurzer
    // Fail-Loud bevor der Sidecar mit `sidecar_unavailable` aus der
    // Fallback-Matrix mark't wird.
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);

    // Plan-RM-M5 §Contract-Versionen-Und-Rollout: erwartete Contract-
    // Major-Version. Sidecar-Version-Response muss
    // `min_compatible_version <= 1.0 <= max_compatible_version` melden,
    // sonst `contract_incompatible`-Fallback.
    public string ExpectedContractVersion { get; init; } = "1.0.0";

    // Pflicht-Features die der Sidecar im Version-Response melden muss.
    // Fehlt eines → kein Optimize-Call, lokaler Fallback. Worker
    // verlangt heute `has-usable-solution` (struktureller Vertrag für
    // plan-RM-M5 §Sidecar-Status-Taxonomie).
    public IReadOnlyList<string> RequiredFeatures { get; init; }
        = new[] { "has-usable-solution" };

    // Plan-RM-M5 §Fallback-Plan-Gueltigkeit: maximales Alter für
    // einen bestehenden Schedule, der als Fallback wiederverwendet
    // werden darf. Pro Asset/Schedule-Type. Default 0 ⇒ Plan-Gültigkeit
    // muss explizit konfiguriert werden (sonst gilt jeder Fallback-
    // Kandidat als `no_valid_plan` → Safe-Stop). Sub-Slice C verdrahtet
    // diesen Slot in den Fallback-Pfad.
    public TimeSpan MaxFallbackScheduleAge { get; init; } = TimeSpan.Zero;

    // Operator-Override-Slot für mTLS-Cross-Host-Pfad. Pfad zum
    // Client-Cert (PEM oder PKCS#12). Default null ⇒ kein Client-Cert
    // gesetzt; bei `https://`-Endpoint im Production-Profile pflicht
    // (Runtime-Check in Sub-Slice C).
    public string? ClientCertificatePath { get; init; }

    // Operator-Override-Slot für die Trusted-CA-Cert-Datei.
    public string? TrustedServerCertificatesPath { get; init; }

    // Operator-Pfad zu einem Bearer-Token (zweite AuthZ-Schicht, ADR
    // 0005 §4). Default null ⇒ kein Bearer-Token (mTLS oder UDS-Perms
    // sind dann die einzige Schicht). Sub-Slice C zündet diesen Pfad
    // wenn Multi-Tenant-Trigger zündet.
    public string? BearerTokenPath { get; init; }

    public OptimizationCoreOptions EnsureValid(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(SidecarEndpoint);

        if (ConnectTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"ConnectTimeout must be positive (got {ConnectTimeout}).");
        }
        if (RequestDeadline <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"RequestDeadline must be positive (got {RequestDeadline}).");
        }
        if (MaxFallbackScheduleAge < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "MaxFallbackScheduleAge must be non-negative (got "
                + $"{MaxFallbackScheduleAge}). Zero means 'no fallback "
                + "candidate accepted; missing schedule ⇒ no_valid_plan'.");
        }
        if (string.IsNullOrWhiteSpace(ExpectedContractVersion))
        {
            throw new InvalidOperationException(
                "optimization-core-contract-incompatible: "
                + "ExpectedContractVersion must be set (e.g. '1.0.0').");
        }
        // Plan-RM-M5-01 §6 Akzeptanzkriterium: EnsureValid wirft
        // `optimization-core-contract-incompatible` wenn die
        // Operator-Konfiguration der erwarteten Contract-Major-Version
        // nicht semver-parsbar ist. Das ist die Boot-Zeit-Validierung;
        // der Runtime-Check (Sidecar-reportet eine inkompatible Version)
        // sitzt in `OptimizationCoreScheduleOptimizer.
        // EnsureContractCompatibleAsync` und wirft dort eine separate
        // ContractIncompatibleException mit demselben kebab-case-
        // Reason. Beide Pfade landen via Status-Mapper auf demselben
        // `FallbackReason.ContractIncompatible`.
        if (!Version.TryParse(StripSemVerSuffix(ExpectedContractVersion), out _))
        {
            throw new InvalidOperationException(
                $"optimization-core-contract-incompatible: "
                + $"ExpectedContractVersion `{ExpectedContractVersion}` is "
                + "not a parseable semver (expected e.g. '1.0.0'). The "
                + "version is compared against the sidecar's reported "
                + "[min_compatible_version, max_compatible_version] range "
                + "at runtime — an unparseable value would deadlock the "
                + "compatibility gate at first Optimize-call.");
        }

        // Plan-RM-M5-01 D-02: Production-Profile macht plaintext-TCP
        // unwirksam. Akzeptierte Schemes: `unix` (UDS-Default) und
        // `https` (Cross-Host mTLS). `http` und sonstige Schemes ⇒
        // harter Startup-Fehler.
        if (RuntimeProfile == OptimizationCoreRuntimeProfile.Production)
        {
            var scheme = SidecarEndpoint.Scheme;
            var isSecureScheme = string.Equals(scheme, "unix", StringComparison.OrdinalIgnoreCase)
                || string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase);
            if (!isSecureScheme)
            {
                throw new InvalidOperationException(
                    $"optimization-core-not-hardened-in-production: "
                    + $"RuntimeProfile=Production requires a `unix://` or "
                    + $"`https://` SidecarEndpoint; got `{scheme}://`. "
                    + "Switch RuntimeProfile to Development or HilSimulator "
                    + "if you must run against a plaintext endpoint (e.g. "
                    + "an In-Process TestSidecar).");
            }
            OptimizationCoreOptionsLog.LogProductionEndpointAccepted(
                logger, SidecarEndpoint);
        }
        else
        {
            // Pre-Production-Profile: alle Schemes durch, plus
            // operator-sichtbare Warning damit die Test-Konfiguration
            // im stdout-Log markiert ist.
            OptimizationCoreOptionsLog.LogTestProfileEndpoint(
                logger, RuntimeProfile, SidecarEndpoint);
        }

        return this;
    }

    // SemVer-Pre-Release-Suffixe (z. B. `1.0.0-rc.1`) auf der Wire-
    // Seite akzeptiert die Range-Logik nach `-`-Strip; EnsureValid
    // schneidet hier analog ab damit Operator-Configs mit Pre-Release-
    // Tags nicht stillschweigend rejected werden.
    private static string StripSemVerSuffix(string s)
    {
        var dash = s.IndexOf('-', StringComparison.Ordinal);
        return dash >= 0 ? s[..dash] : s;
    }
}

internal static partial class OptimizationCoreOptionsLog
{
    [LoggerMessage(EventId = 5100, Level = LogLevel.Information,
        Message = "optimization-core sidecar configured for production against {SidecarEndpoint}")]
    public static partial void LogProductionEndpointAccepted(
        ILogger logger, Uri sidecarEndpoint);

    [LoggerMessage(EventId = 5101, Level = LogLevel.Warning,
        Message = "optimization-core sidecar running in test profile {RuntimeProfile} against {SidecarEndpoint}; not production-hardened")]
    public static partial void LogTestProfileEndpoint(
        ILogger logger,
        OptimizationCoreRuntimeProfile runtimeProfile,
        Uri sidecarEndpoint);
}
