using System.Text.Json;
using System.Text.Json.Nodes;

namespace AllegroDrafter.Core.Proposals;

/// <summary>Strict readers for tool arguments. Every failure names the exact path that was wrong.</summary>
public static class Args
{
    public static string RequiredString(JsonObject obj, string key, string path)
    {
        var value = OptionalString(obj, key, path);
        if (string.IsNullOrWhiteSpace(value)) throw new ToolArgumentException($"{path}.{key} is required and must be a non-empty string.");
        return value;
    }

    public static string? OptionalString(JsonObject obj, string key, string path)
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node is null) return null;
        if (node is JsonValue v && v.GetValueKind() == JsonValueKind.String) return v.GetValue<string>().Trim();
        throw new ToolArgumentException($"{path}.{key} must be a string.");
    }

    public static double? OptionalDouble(JsonObject obj, string key, string path)
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node is null) return null;
        if (node is JsonValue v && v.GetValueKind() == JsonValueKind.Number)
        {
            var d = v.GetValue<double>();
            if (double.IsFinite(d)) return d;
        }
        throw new ToolArgumentException($"{path}.{key} must be a number.");
    }

    public static bool OptionalBool(JsonObject obj, string key, string path, bool fallback = false)
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node is null) return fallback;
        if (node is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False) return v.GetValue<bool>();
        throw new ToolArgumentException($"{path}.{key} must be true or false.");
    }

    public static JsonArray RequiredArray(JsonObject obj, string key, string path)
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node is not JsonArray array)
            throw new ToolArgumentException($"{path}.{key} is required and must be an array.");
        if (array.Count == 0) throw new ToolArgumentException($"{path}.{key} must contain at least one item.");
        return array;
    }

    public static JsonArray? OptionalArray(JsonObject obj, string key, string path)
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node is null) return null;
        return node as JsonArray ?? throw new ToolArgumentException($"{path}.{key} must be an array.");
    }

    public static JsonObject ObjectAt(JsonArray array, int index, string path) =>
        array[index] as JsonObject ?? throw new ToolArgumentException($"{path}[{index}] must be an object.");

    public static long RequiredLong(JsonObject obj, string key, string path)
    {
        if (obj.TryGetPropertyValue(key, out var node) && node is JsonValue v && v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<long>(out var l))
            return l;
        throw new ToolArgumentException($"{path}.{key} is required and must be a whole number (the element id from list_elements).");
    }

    public static IReadOnlyList<string>? OptionalStringArray(JsonObject obj, string key, string path)
    {
        var array = OptionalArray(obj, key, path);
        if (array is null) return null;
        var list = new List<string>();
        for (var i = 0; i < array.Count; i++)
        {
            if (array[i] is JsonValue v && v.GetValueKind() == JsonValueKind.String) list.Add(v.GetValue<string>().Trim());
            else throw new ToolArgumentException($"{path}.{key}[{i}] must be a string.");
        }
        return list;
    }

    /// <summary>
    /// A { "name": value } object. Strings, numbers and true/false are accepted and kept as text
    /// (AI clients often send a door mark as 101 instead of "101").
    /// </summary>
    public static IReadOnlyList<(string Name, string Value)> RequiredStringMap(JsonObject obj, string key, string path)
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node is not JsonObject map)
            throw new ToolArgumentException($"{path}.{key} is required and must be an object like {{ \"Mark\": \"101\" }}.");
        if (map.Count == 0) throw new ToolArgumentException($"{path}.{key} must set at least one parameter.");

        var result = new List<(string, string)>();
        foreach (var (name, value) in map)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ToolArgumentException($"{path}.{key} has an empty parameter name.");
            var text = value switch
            {
                null => "",
                JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>().Trim(),
                JsonValue v when v.GetValueKind() == JsonValueKind.Number => v.ToJsonString(),
                JsonValue v when v.GetValueKind() is JsonValueKind.True or JsonValueKind.False => v.GetValue<bool>() ? "Yes" : "No",
                _ => throw new ToolArgumentException($"{path}.{key}.{name} must be text, a number, or true/false."),
            };
            result.Add((name.Trim(), text));
        }
        return result;
    }
}
