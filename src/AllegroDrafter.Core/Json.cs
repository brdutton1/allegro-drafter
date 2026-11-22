using System.Text.Json;
using System.Text.Json.Serialization;

namespace AllegroDrafter.Core;

public static class Json
{
    public static readonly JsonSerializerOptions Indented = Create(writeIndented: true);
    public static readonly JsonSerializerOptions Compact = Create(writeIndented: false);

    private static JsonSerializerOptions Create(bool writeIndented) => new(JsonSerializerDefaults.Web)
    {
        WriteIndented = writeIndented,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
