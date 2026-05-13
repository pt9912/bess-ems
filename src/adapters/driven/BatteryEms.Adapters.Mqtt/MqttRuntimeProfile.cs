namespace BatteryEms.Adapters.Mqtt;

// Adapter-local runtime profile. Production is the default so a
// configuration drift toward plaintext MQTT fails closed unless a test
// or simulator deployment explicitly opts into Development/HilSimulator.
public enum MqttRuntimeProfile
{
    Development,
    HilSimulator,
    Production,
}
