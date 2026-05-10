namespace BatteryEms.Application.IO;

// Composition-root helper that turns a "which I/O families has the
// operator configured" answer into a single typed family decision —
// or fails fast with `multiple-io-adapters-configured` when the
// operator set up more than one (plan-RM-M4-04 §4 Sub-Slice C).
//
// The helper is intentionally configuration-agnostic: it takes
// boolean flags, not the runtime config + host options, so it can
// be unit-tested without booting a WebApplicationFactory. The
// host's BuildApp computes the booleans from its own config and
// then switch-dispatches on the returned family.
public static class IoAdapterTriage
{
    public enum Family
    {
        None,
        Modbus,
        Mqtt,
        OpcUa,
    }

    public static Family SelectConfiguredFamily(
        bool modbusConfigured,
        bool mqttConfigured,
        bool opcUaConfigured)
    {
        var configured = new List<Family>();
        if (modbusConfigured) { configured.Add(Family.Modbus); }
        if (mqttConfigured) { configured.Add(Family.Mqtt); }
        if (opcUaConfigured) { configured.Add(Family.OpcUa); }
        if (configured.Count > 1)
        {
            // CA1308 wants ToUpperInvariant; we want kebab-case-style
            // tags (modbus / mqtt / opcua) so the operator's stdout
            // matches the BessHostOptions JSON keys casing exactly.
            // Use a small static lookup instead of a culture-sensitive
            // .ToLower call.
            var names = new string[configured.Count];
            for (var i = 0; i < configured.Count; i++)
            {
                names[i] = ToTag(configured[i]);
            }
            throw new InvalidOperationException(
                "multiple-io-adapters-configured: pick exactly one of "
                + $"[{string.Join(", ", names)}]. The composition root refuses "
                + "to silently choose between simultaneously-configured I/O families.");
        }
        return configured.Count == 0 ? Family.None : configured[0];
    }

    private static string ToTag(Family family) => family switch
    {
        Family.Modbus => "modbus",
        Family.Mqtt => "mqtt",
        Family.OpcUa => "opcua",
        _ => "none",
    };
}
