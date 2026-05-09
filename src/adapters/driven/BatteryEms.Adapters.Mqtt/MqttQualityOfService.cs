namespace BatteryEms.Adapters.Mqtt;

// Adapter-side QoS enum (RM-M4-06) — keeps the hexagon and the
// adapter contracts free of MQTTnet-specific symbols. The
// MqttNetClient maps these values to MQTTnet's QoS levels at the
// wire boundary.
//
// Numeric values match the MQTT spec so a configuration file's
// integer (0/1/2) deserialises directly into the right enum case.
//
//   AtMostOnce  (0): fire-and-forget, no broker confirmation.
//   AtLeastOnce (1): broker PUBACK, possible duplicates that the
//                    application layer must dedupe (e.g. via
//                    BatteryCommand.CommandId for the command
//                    channel).
//   ExactlyOnce (2): four-way handshake (PUBREC/PUBREL/PUBCOMP).
//                    RM-M4-06 D-03 explicitly does NOT recommend
//                    this as a default — the app-level ACK already
//                    provides idempotency on top of AtLeastOnce.
public enum MqttQualityOfService
{
    AtMostOnce = 0,
    AtLeastOnce = 1,
    ExactlyOnce = 2,
}
