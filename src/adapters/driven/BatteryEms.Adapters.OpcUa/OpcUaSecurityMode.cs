namespace BatteryEms.Adapters.OpcUa;

// Pre-RM-M4-05 enum für die Security-Mode-Slot auf OpcUaAdapterOptions
// (plan-RM-M4-04 D-04). M4-05 erweitert dies typischerweise um die
// volle OPC-UA-Spec-Liste (Sign, SignAndEncrypt). Heute lebt der
// Adapter mit `None` plus dem AllowUnsecured-Startup-Guard auf der
// bool-Achse; M4-05 layert die RuntimeProfile-Awareness drauf.
public enum OpcUaSecurityMode
{
    None,
    Sign,
    SignAndEncrypt,
}
