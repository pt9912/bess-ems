namespace BatteryEms.Adapters.OpcUa;

// Plan-RM-M4-05 §3 + D-04: hard-coded Allowlist für OPC-UA-Security-
// Policies. Die Liste ist absichtlich keine konfigurierbare Slot —
// jede Erweiterung verlangt einen Plan-Slice (F-17), eine Policy-
// Konstante hier, einen erweiterten IsAllowed-Pin und eine Test-
// Server-Erweiterung. Der hart codierte Pfad ist die Kontrolle gegen
// silent Policy-Schwenks im Operator-Lager.
//
// Bewusst draussen (Trigger F-17, in `note-RM-M4-followups.md`):
// - `Aes128Sha256RsaOaep`, `Aes256Sha256RsaPss` — modernere RSA-
//   Policies; Server-Side-Support in der Praxis variabel.
// - `Basic256` (deprecated), `Basic128Rsa15` (deprecated) — bewusst
//   ausgeschlossen, würden im Code-Review explizit zurückgewiesen.
// - ECC-Policies (`ECC_nistP256` etc.) — ECC-Cert-Provisioning ist
//   nicht Teil von M4-05.
public static class OpcUaSecurityPolicies
{
    public const string Basic256Sha256 =
        "http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256";

    // Statische Allowlist. Wer eine Policy hinzufügt, muss den
    // Adapter-Code anfassen, mit F-17-Plan-Slice landen und Tests
    // schreiben (D-04).
    private static readonly HashSet<string> Allowlist = new(StringComparer.Ordinal)
    {
        Basic256Sha256,
    };

    public static bool IsAllowed(string policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return Allowlist.Contains(policy);
    }
}
