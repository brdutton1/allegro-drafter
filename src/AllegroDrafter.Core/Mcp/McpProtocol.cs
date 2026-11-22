using System.Text.Json;
using System.Text.Json.Nodes;
using AllegroDrafter.Core.Proposals;

namespace AllegroDrafter.Core.Mcp;

/// <summary>
/// Minimal MCP (JSON-RPC 2.0) server logic: initialize, ping, tools/list, tools/call.
/// Transport-agnostic: the HTTP server and the stdio bridge both feed it.
/// </summary>
public sealed class McpProtocol
{
    public const string ServerName = "allegro-drafter";
    public static readonly string ServerVersion = typeof(McpProtocol).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
    public static readonly IReadOnlyList<string> SupportedVersions = new[] { "2025-06-18", "2025-03-26", "2024-11-05" };

    private readonly IToolHandler _handler;

    public McpProtocol(IToolHandler handler) => _handler = handler;

    /// <summary>Returns the response to send, or null for notifications and client responses.</summary>
    public async Task<JsonNode?> HandleAsync(JsonNode? message, CancellationToken ct)
    {
        if (message is JsonArray batch)
        {
            if (batch.Count == 0) return Error(null, -32600, "Empty batch.");
            var responses = new JsonArray();
            foreach (var item in batch)
            {
                var response = await HandleAsync(item, ct).ConfigureAwait(false);
                if (response is not null) responses.Add(response);
            }
            return responses.Count == 0 ? null : responses;
        }

        if (message is not JsonObject request) return Error(null, -32600, "Request must be a JSON object.");

        var id = request["id"]?.DeepClone();
        var method = (request["method"] as JsonValue)?.TryGetValue<string>(out var m) == true ? m : null;
        if (method is null)
        {
            // A response from the client (we never send requests) or garbage. Either way, no reply.
            return request.ContainsKey("result") || request.ContainsKey("error") ? null : Error(id, -32600, "Missing method.");
        }

        var isNotification = !request.ContainsKey("id");
        var parameters = request["params"] as JsonObject ?? new JsonObject();

        try
        {
            JsonNode? result = method switch
            {
                "initialize" => Initialize(parameters),
                "ping" => new JsonObject(),
                "tools/list" => ToolsList(),
                "tools/call" => await ToolsCallAsync(parameters, ct).ConfigureAwait(false),
                _ when method.StartsWith("notifications/", StringComparison.Ordinal) => null,
                _ => throw new McpException(-32601, $"Method not found: {method}"),
            };
            if (isNotification) return null;
            return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result ?? new JsonObject() };
        }
        catch (McpException ex)
        {
            return isNotification ? null : Error(id, ex.Code, ex.Message);
        }
    }

    public static JsonObject Error(JsonNode? id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
    };

    private static JsonObject Initialize(JsonObject parameters)
    {
        var requested = (parameters["protocolVersion"] as JsonValue)?.TryGetValue<string>(out var v) == true ? v : null;
        var version = requested is not null && SupportedVersions.Contains(requested) ? requested : SupportedVersions[0];
        return new JsonObject
        {
            ["protocolVersion"] = version,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
            ["serverInfo"] = new JsonObject { ["name"] = ServerName, ["title"] = "Allegro Drafter (Revit)", ["version"] = ServerVersion },
            ["instructions"] = ToolCatalog.ServerInstructions,
        };
    }

    private static JsonObject ToolsList() => new()
    {
        ["tools"] = new JsonArray(ToolCatalog.All.Select(t => (JsonNode)t.ToJson()).ToArray()),
    };

    private async Task<JsonNode> ToolsCallAsync(JsonObject parameters, CancellationToken ct)
    {
        var name = (parameters["name"] as JsonValue)?.TryGetValue<string>(out var n) == true ? n : null;
        if (string.IsNullOrEmpty(name)) throw new McpException(-32602, "tools/call needs params.name.");
        if (ToolCatalog.Find(name) is null) throw new McpException(-32602, $"Unknown tool: {name}");

        var arguments = parameters["arguments"] switch
        {
            null => new JsonObject(),
            JsonObject o => o,
            _ => throw new McpException(-32602, "params.arguments must be an object."),
        };

        ToolCallResult result;
        try
        {
            result = await _handler.CallAsync(name, arguments, ct).ConfigureAwait(false);
        }
        catch (ToolArgumentException ex)
        {
            result = ToolCallResult.Error(ex.Message);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            result = ToolCallResult.Error($"{name} failed: {ex.Message}");
        }
        return result.ToMcpResult();
    }
}

public sealed class McpException : Exception
{
    public McpException(int code, string message) : base(message) => Code = code;
    public int Code { get; }
}
