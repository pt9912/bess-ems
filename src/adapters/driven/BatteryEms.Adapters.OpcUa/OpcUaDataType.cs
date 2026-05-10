namespace BatteryEms.Adapters.OpcUa;

// Adapter-side enum for the v1 OPC-UA-Mapping-Schema's `data_type`
// strings (RM-M4-07). The Application-side OpcUaNodeMapping carries
// the raw string; the adapter translates to this enum at construction
// time and uses it to drive Variant-decoding (siehe plan-RM-M4-04 §7
// Variant-Decoding-Risiko) und Write-side type-coercion. Strukturen,
// Arrays und Enums sind bewusst nicht abgedeckt — F-15 (siehe
// plan-RM-M4-04 §9).
//
// CA1720 wäre auf UInt32/UInt64 aktiv, weil diese Identifier den
// .NET-Primitivtypnamen entsprechen. Hier ist die Spec-Konformität
// (OPC-UA-Daten-Typen heißen nun mal so) wichtiger als das
// Naming-Lint — die Suppression ist intentional.
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming", "CA1720",
    Justification = "Names mirror the OPC-UA data-type spec; matching the .NET primitive type names is intentional.")]
public enum OpcUaDataType
{
    Bool,
    Int16,
    Int32,
    Int64,
    UInt16,
    UInt32,
    UInt64,
    Float,
    Double,
    String,
}
