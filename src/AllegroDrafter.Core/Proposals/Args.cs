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
}
