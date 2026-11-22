using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AllegroDrafter.Core.Mcp;

/// <summary>
/// The stdio side of the bridge exe that Claude Desktop / Claude Code launch.
/// Answers initialize and tools/list itself (so Claude always sees the tools)
/// and forwards tools/call to the Revit add-in over localhost HTTP.
/// </summary>
public sealed class BridgeRelay
{
    private readonly McpProtocol _protocol;

    public BridgeRelay(AppPaths paths, HttpClient http)
    {
        _protocol = new McpProtocol(new RemoteToolHandler(paths, http));
    }

    public async Task RunAsync(TextReader input, TextWriter output, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var line = await input.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) return; // Client closed stdin: exit.
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonNode? reply;
            try
            {
                reply = await _protocol.HandleAsync(JsonNode.Parse(line), ct).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                reply = McpProtocol.Error(null, -32700, "Parse error.");
            }

            if (reply is null) continue;
            await output.WriteLineAsync(reply.ToJsonString(Json.Compact)).ConfigureAwait(false);
            await output.FlushAsync(ct).ConfigureAwait(false);
        }
    }
}

/// <summary>Forwards a tool call to the running Revit add-in.</summary>
public sealed class RemoteToolHandler : IToolHandler
{
    public const string RevitNotRunning =
        "Revit is not running, or the Allegro Drafter add-in is not loaded. Open Revit 2025/2026 with the project, then try again.";

    private readonly AppPaths _paths;
    private readonly HttpClient _http;
    private int _nextId;

    public RemoteToolHandler(AppPaths paths, HttpClient http)
    {
        _paths = paths;
        _http = http;
    }

    public async Task<ToolCallResult> CallAsync(string name, JsonObject arguments, CancellationToken ct)
    {
        // Re-read every call so a Revit restart or a new token is picked up.
        var config = McpConfig.TryLoad(_paths);
        if (config is null) return ToolCallResult.Error(RevitNotRunning + $" (No {_paths.McpConfigFile} yet.)");

        var body = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = Interlocked.Increment(ref _nextId),
            ["method"] = "tools/call",
            ["params"] = new JsonObject { ["name"] = name, ["arguments"] = arguments.DeepClone() },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, config.Endpoint)
        {
            Content = new StringContent(body.ToJsonString(Json.Compact), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.InnerException is SocketException || ex.StatusCode is null)
        {
            return ToolCallResult.Error(RevitNotRunning);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return ToolCallResult.Error("Revit did not answer in time. It may be busy with a dialog or long command. Finish that and try again.");
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return ToolCallResult.Error($"Revit add-in returned HTTP {(int)response.StatusCode}: {text}");
            }

            try
            {
                var json = JsonNode.Parse(text) as JsonObject;
                if (json?["error"] is JsonObject error) return ToolCallResult.Error(error["message"]?.GetValue<string>() ?? "Unknown error.");
                var result = json?["result"] as JsonObject ?? throw new JsonException("No result.");
                var content = (result["content"] as JsonArray)?.OfType<JsonObject>()
                    .Where(c => c["type"]?.GetValue<string>() == "text")
                    .Select(c => c["text"]?.GetValue<string>() ?? "");
                var isError = result["isError"]?.GetValue<bool>() ?? false;
                return new ToolCallResult(string.Join(Environment.NewLine, content ?? Array.Empty<string>()), isError);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                return ToolCallResult.Error($"Unexpected reply from the Revit add-in: {ex.Message}");
            }
        }
    }
}
