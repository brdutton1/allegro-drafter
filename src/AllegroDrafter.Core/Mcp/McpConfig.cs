using System.Security.Cryptography;
using System.Text.Json;

namespace AllegroDrafter.Core.Mcp;

/// <summary>
/// Port and access token for the local MCP endpoint. Created once and reused,
/// so the bridge and any saved Claude config keep working across Revit restarts.
/// </summary>
public sealed record McpConfig(int Port, string Token)
{
    public const int DefaultPort = 48884;

    public string Endpoint => $"http://localhost:{Port}/mcp";

    public static McpConfig LoadOrCreate(AppPaths paths)
    {
        var existing = TryLoad(paths);
        if (existing is not null) return existing;

        var created = new McpConfig(DefaultPort, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant());
        paths.EnsureCreated();
        File.WriteAllText(paths.McpConfigFile, JsonSerializer.Serialize(created, Json.Indented));
        return created;
    }

    public static McpConfig? TryLoad(AppPaths paths)
    {
        try
        {
            if (!File.Exists(paths.McpConfigFile)) return null;
            var config = JsonSerializer.Deserialize<McpConfig>(File.ReadAllText(paths.McpConfigFile), Json.Indented);
            if (config is null || config.Port is < 1 or > 65535 || string.IsNullOrWhiteSpace(config.Token)) return null;
            return config;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
