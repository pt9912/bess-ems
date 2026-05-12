using System.Globalization;
using System.Text.Json;

namespace BatteryEms.Application.Tests.Replay;

internal sealed class ReplayJsonReader
{
    private readonly JsonElement _element;
    private readonly string _path;

    public ReplayJsonReader(JsonElement element, string path)
    {
        if (element.ValueKind is not JsonValueKind.Object)
        {
            throw new ReplayJsonException("not_object", path, "Expected JSON object.");
        }

        _element = element;
        _path = path;
    }

    public string Path => _path;

    public void RejectUnknownProperties(IReadOnlySet<string> known)
    {
        foreach (var property in _element.EnumerateObject())
        {
            if (!known.Contains(property.Name))
            {
                throw new ReplayJsonException(
                    "unknown_field",
                    ChildPath(property.Name),
                    $"Unknown field '{property.Name}'.");
            }
        }
    }

    public string RequiredString(string name)
    {
        var value = Required(name);
        if (value.ValueKind is not JsonValueKind.String)
        {
            throw TypeError(name, "string");
        }

        return value.GetString() ?? string.Empty;
    }

    public int? OptionalInt32(string name)
    {
        if (!_element.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null)
        {
            return null;
        }

        if (!value.TryGetInt32(out var result))
        {
            throw TypeError(name, "integer");
        }

        return result;
    }

    public double RequiredFiniteDouble(string name)
    {
        var value = Required(name);
        if (!value.TryGetDouble(out var result) || !double.IsFinite(result))
        {
            throw TypeError(name, "finite number");
        }

        return result;
    }

    public bool RequiredBoolean(string name)
    {
        var value = Required(name);
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw TypeError(name, "boolean"),
        };
    }

    public DateTimeOffset RequiredDateTimeOffset(string name)
    {
        var raw = RequiredString(name);
        if (!DateTimeOffset.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var result))
        {
            throw TypeError(name, "UTC timestamp");
        }

        return result;
    }

    public DateTimeOffset? OptionalDateTimeOffset(string name)
    {
        if (!_element.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind is not JsonValueKind.String)
        {
            throw TypeError(name, "UTC timestamp");
        }

        var raw = value.GetString() ?? string.Empty;
        if (!DateTimeOffset.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var result))
        {
            throw TypeError(name, "UTC timestamp");
        }

        return result;
    }

    public ReplayJsonReader RequiredObject(string name)
    {
        var value = Required(name);
        return new ReplayJsonReader(value, ChildPath(name));
    }

    public IReadOnlyList<T> RequiredArray<T>(string name, Func<JsonElement, string, T> map)
    {
        var value = Required(name);
        if (value.ValueKind is not JsonValueKind.Array)
        {
            throw TypeError(name, "array");
        }

        var result = new List<T>();
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            result.Add(map(item, $"{ChildPath(name)}[{index}]"));
            index++;
        }

        return result;
    }

    public ReplayJsonReader? OptionalObject(string name)
    {
        if (!_element.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null)
        {
            return null;
        }

        return new ReplayJsonReader(value, ChildPath(name));
    }

    private JsonElement Required(string name)
    {
        if (!_element.TryGetProperty(name, out var value))
        {
            throw new ReplayJsonException(
                "missing_required_field",
                ChildPath(name),
                $"Missing required field '{name}'.");
        }

        return value;
    }

    private ReplayJsonException TypeError(string name, string expected) =>
        new("invalid_type", ChildPath(name), $"Expected {expected}.");

    private string ChildPath(string name) => $"{_path}.{name}";
}

internal sealed class ReplayJsonException : Exception
{
    public ReplayJsonException()
        : this("replay_json_error", "$", "Replay JSON error.")
    {
    }

    public ReplayJsonException(string message)
        : this("replay_json_error", "$", message)
    {
    }

    public ReplayJsonException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = "replay_json_error";
        Path = "$";
        Detail = message;
    }

    public ReplayJsonException(string code, string path, string detail)
        : base(detail)
    {
        Code = code;
        Path = path;
        Detail = detail;
    }

    public string Code { get; }
    public string Path { get; }
    public string Detail { get; }
}
