using System.Text.Json;
using System.Text.Json.Nodes;

namespace AllegroDrafter.Core.Mcp;

public sealed record ToolCallResult(string Text, bool IsError)
{
    public static ToolCallResult Ok(object payload) =>
        new(payload as string ?? JsonSerializer.Serialize(payload, Json.Indented), false);

    public static ToolCallResult Error(string message) => new(message, true);

    public static ToolCallResult Errors(string heading, IEnumerable<string> errors) =>
        new(heading + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(e => "- " + e)), true);

    public JsonObject ToMcpResult() => new()
    {
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = Text }),
        ["isError"] = IsError,
    };
}

public interface IToolHandler
{
    Task<ToolCallResult> CallAsync(string name, JsonObject arguments, CancellationToken cancellationToken);
}
