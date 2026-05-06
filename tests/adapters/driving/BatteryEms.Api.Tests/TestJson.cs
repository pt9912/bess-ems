using System.Text.Json;
using System.Text.Json.Serialization;

namespace BatteryEms.Api.Tests;

internal static class TestJson
{
    // Mirror the Program-side serializer policy so tests deserialise the
    // same wire format the API emits (snake_case, enum-as-string).
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };
}
