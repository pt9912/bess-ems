using BatteryEms.Application.Configuration;

namespace BatteryEms.Adapters.Mqtt;

// Resolves topic templates from the MQTT mapping into broker-side
// addresses. Direction is from the .NET-EMS adapter's perspective (see
// config/schema/mqtt-mapping.schema.json): "subscribe" means the EMS
// subscribes (the simulator/field device publishes), "publish" means
// the EMS publishes.
public static class TopicResolver
{
    public static MqttTopicMapping Require(MqttMappingConfiguration mapping, string name, string direction)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        foreach (var topic in mapping.Topics)
        {
            if (topic.Name == name && topic.Direction == direction)
            {
                return topic;
            }
        }
        throw new InvalidOperationException(
            $"MQTT mapping '{mapping.ProfileName}' has no '{name}' topic with direction '{direction}'.");
    }

    public static string SubstituteAssetId(string template, string assetId)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(assetId);
        return template.Replace("{assetId}", assetId, StringComparison.Ordinal);
    }
}
