using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using AllegroDrafter.Core.Mcp;
using AllegroDrafter.Core.Proposals;
using static AllegroDrafter.Core.Tests.Fixtures;

namespace AllegroDrafter.Core.Tests;

public class McpProtocolTests
{
    private static async Task<JsonObject?> Send(McpProtocol protocol, string json) =>
        (JsonObject?)await protocol.HandleAsync(JsonNode.Parse(json), CancellationToken.None);

    [Fact]
    public async Task Initialize_echoes_supported_version_and_sends_instructions()
    {
        var protocol = new McpProtocol(new FakeToolHandler());
        var reply = await Send(protocol, """{ "jsonrpc": "2.0", "id": 1, "method": "initialize", "params": { "protocolVersion": "2025-03-26" } }""");
        Assert.Equal("2025-03-26", reply!["result"]!["protocolVersion"]!.GetValue<string>());
        Assert.Contains("get_standards", reply["result"]!["instructions"]!.GetValue<string>());
        Assert.Equal(1, reply["id"]!.GetValue<int>());
    }

    [Fact]
    public async Task Initialize_with_unknown_version_falls_back_to_latest()
    {
        var reply = await Send(new McpProtocol(new FakeToolHandler()), """{ "jsonrpc": "2.0", "id": "a", "method": "initialize", "params": { "protocolVersion": "1999-01-01" } }""");
        Assert.Equal(McpProtocol.SupportedVersions[0], reply!["result"]!["protocolVersion"]!.GetValue<string>());
        Assert.Equal("a", reply["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task Notifications_get_no_reply()
    {
        var protocol = new McpProtocol(new FakeToolHandler());
        Assert.Null(await protocol.HandleAsync(JsonNode.Parse("""{ "jsonrpc": "2.0", "method": "notifications/initialized" }"""), default));
        Assert.Null(await protocol.HandleAsync(JsonNode.Parse("""{ "jsonrpc": "2.0", "id": 5, "result": {} }"""), default));
    }

    [Fact]
    public async Task Tools_list_matches_catalog_and_schemas_parse()
    {
        var reply = await Send(new McpProtocol(new FakeToolHandler()), """{ "jsonrpc": "2.0", "id": 2, "method": "tools/list" }""");
        var tools = reply!["result"]!["tools"]!.AsArray();
        Assert.Equal(ToolCatalog.All.Count, tools.Count);
        foreach (var tool in tools)
        {
            Assert.Equal("object", tool!["inputSchema"]!["type"]!.GetValue<string>());
        }
        var sheetSet = tools.Single(t => t!["name"]!.GetValue<string>() == ToolNames.ProposeSheetSet)!;
        Assert.False(sheetSet["annotations"]!["readOnlyHint"]!.GetValue<bool>());
        Assert.NotNull(sheetSet["inputSchema"]!["properties"]!["sheets"]!["items"]!["properties"]!["views"]);
    }

    [Fact]
    public void Every_catalog_tool_has_a_unique_name()
    {
        Assert.Equal(ToolCatalog.All.Count, ToolCatalog.All.Select(t => t.Name).Distinct().Count());
        Assert.All(ToolNames.ProposalTools, n => Assert.False(ToolCatalog.Find(n)!.ReadOnly));
    }

    [Fact]
    public async Task Tools_call_routes_to_handler()
    {
        var handler = new FakeToolHandler();
        var reply = await Send(new McpProtocol(handler), """{ "jsonrpc": "2.0", "id": 3, "method": "tools/call", "params": { "name": "list_sheets", "arguments": { } } }""");
        Assert.False(reply!["result"]!["isError"]!.GetValue<bool>());
        Assert.Equal("list_sheets", Assert.Single(handler.Calls).Name);
    }

    [Fact]
    public async Task Tools_call_without_arguments_passes_empty_object()
    {
        var handler = new FakeToolHandler();
        await Send(new McpProtocol(handler), """{ "jsonrpc": "2.0", "id": 3, "method": "tools/call", "params": { "name": "list_sheets" } }""");
        Assert.Empty(Assert.Single(handler.Calls).Args);
    }

    [Fact]
    public async Task Unknown_tool_and_method_are_protocol_errors()
    {
        var protocol = new McpProtocol(new FakeToolHandler());
        var unknownTool = await Send(protocol, """{ "jsonrpc": "2.0", "id": 4, "method": "tools/call", "params": { "name": "delete_everything" } }""");
        Assert.Equal(-32602, unknownTool!["error"]!["code"]!.GetValue<int>());
        var unknownMethod = await Send(protocol, """{ "jsonrpc": "2.0", "id": 5, "method": "resources/list" }""");
        Assert.Equal(-32601, unknownMethod!["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task Handler_exceptions_become_tool_errors_not_crashes()
    {
        var handler = new FakeToolHandler
        {
            Respond = (name, _) => name == "list_sheets" ? throw new InvalidOperationException("boom") : throw new ToolArgumentException("bad arg"),
        };
        var protocol = new McpProtocol(handler);
        var crash = await Send(protocol, """{ "jsonrpc": "2.0", "id": 6, "method": "tools/call", "params": { "name": "list_sheets" } }""");
        Assert.True(crash!["result"]!["isError"]!.GetValue<bool>());
        Assert.Contains("boom", crash["result"]!["content"]![0]!["text"]!.GetValue<string>());

        var bad = await Send(protocol, """{ "jsonrpc": "2.0", "id": 7, "method": "tools/call", "params": { "name": "list_views" } }""");
        Assert.Equal("bad arg", bad!["result"]!["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Batches_are_supported()
    {
        var reply = await new McpProtocol(new FakeToolHandler()).HandleAsync(JsonNode.Parse("""
            [ { "jsonrpc": "2.0", "id": 1, "method": "ping" }, { "jsonrpc": "2.0", "method": "notifications/initialized" } ]
            """), default);
        Assert.Single(Assert.IsType<JsonArray>(reply));
    }

    [Fact]
    public async Task Garbage_shapes_are_rejected()
    {
        var protocol = new McpProtocol(new FakeToolHandler());
        var notObject = (JsonObject?)await protocol.HandleAsync(JsonNode.Parse("42"), default);
        Assert.Equal(-32600, notObject!["error"]!["code"]!.GetValue<int>());
        var noMethod = await Send(protocol, """{ "jsonrpc": "2.0", "id": 1 }""");
        Assert.Equal(-32600, noMethod!["error"]!["code"]!.GetValue<int>());
    }
}

public sealed class McpHttpAndBridgeTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FakeToolHandler _handler = new();
    private readonly McpConfig _config;
    private readonly McpHttpServer _server;
    private readonly HttpClient _http = new(new HttpClientHandler { UseProxy = false });

    public McpHttpAndBridgeTests()
    {
        _config = new McpConfig(FreePort(), "test-token");
        _server = new McpHttpServer(_config, new McpProtocol(_handler));
        _server.Start();
    }

    public void Dispose()
    {
        _server.Dispose();
        _http.Dispose();
        _dir.Dispose();
    }

    private async Task<HttpResponseMessage> Post(string body, string? token = "test-token", string? origin = null, string path = "/mcp")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{_config.Port}{path}")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (origin is not null) request.Headers.Add("Origin", origin);
        return await _http.SendAsync(request);
    }

    private const string Ping = """{ "jsonrpc": "2.0", "id": 1, "method": "ping" }""";

    [Fact]
    public async Task Valid_request_gets_json_reply()
    {
        using var response = await Post(Ping);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.NotNull(json["result"]);
    }

    [Fact]
    public async Task Trailing_slash_path_works()
    {
        using var response = await Post(Ping, path: "/mcp/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Missing_or_wrong_token_is_401()
    {
        using var none = await Post(Ping, token: null);
        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        using var wrong = await Post(Ping, token: "guess");
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
    }

    [Fact]
    public async Task Web_page_origin_is_403_but_localhost_origin_is_ok()
    {
        using var evil = await Post(Ping, origin: "https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, evil.StatusCode);
        using var local = await Post(Ping, origin: "http://localhost:3000");
        Assert.Equal(HttpStatusCode.OK, local.StatusCode);
    }

    [Fact]
    public async Task Notification_is_202_with_no_body()
    {
        using var response = await Post("""{ "jsonrpc": "2.0", "method": "notifications/initialized" }""");
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task Bad_json_is_400_parse_error()
    {
        using var response = await Post("{ nope");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("-32700", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Get_is_405_and_other_paths_404()
    {
        var get = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{_config.Port}/mcp");
        get.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        using var getResponse = await _http.SendAsync(get);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, getResponse.StatusCode);

        using var other = await Post(Ping, path: "/admin");
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
    }

    [Fact]
    public void Origin_rules()
    {
        Assert.True(McpHttpServer.IsAllowedOrigin(null));
        Assert.True(McpHttpServer.IsAllowedOrigin("http://127.0.0.1:5173"));
        Assert.False(McpHttpServer.IsAllowedOrigin("null"));
        Assert.False(McpHttpServer.IsAllowedOrigin("http://localhost.evil.com"));
    }

    private async Task<List<JsonObject>> RunBridge(params string[] lines)
    {
        var relay = new BridgeRelay(_dir.AppPaths, _http);
        var input = new StringReader(string.Join("\n", lines) + "\n");
        var output = new StringWriter();
        await relay.RunAsync(input, output, CancellationToken.None);
        return output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => JsonNode.Parse(l)!.AsObject()).ToList();
    }

    [Fact]
    public async Task Bridge_serves_tools_even_when_revit_is_closed()
    {
        // No mcp.json in this temp dir: as if Revit never started.
        var replies = await RunBridge(
            """{ "jsonrpc": "2.0", "id": 1, "method": "initialize", "params": { "protocolVersion": "2025-06-18" } }""",
            """{ "jsonrpc": "2.0", "method": "notifications/initialized" }""",
            """{ "jsonrpc": "2.0", "id": 2, "method": "tools/list" }""",
            """{ "jsonrpc": "2.0", "id": 3, "method": "tools/call", "params": { "name": "list_sheets" } }""");

        Assert.Equal(3, replies.Count);
        Assert.Equal(ToolCatalog.All.Count, replies[1]["result"]!["tools"]!.AsArray().Count);
        Assert.True(replies[2]["result"]!["isError"]!.GetValue<bool>());
        Assert.Contains("Revit is not running", replies[2]["result"]!["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Bridge_forwards_tool_calls_to_revit_with_token()
    {
        File.WriteAllText(_dir.AppPaths.McpConfigFile, System.Text.Json.JsonSerializer.Serialize(_config, Json.Indented));
        _handler.Respond = (name, args) => ToolCallResult.Ok(new { echoed = name, sheet = args["number"]?.GetValue<string>() });

        var replies = await RunBridge("""{ "jsonrpc": "2.0", "id": 9, "method": "tools/call", "params": { "name": "list_sheets", "arguments": { "number": "A1.0" } } }""");

        var result = Assert.Single(replies)["result"]!;
        Assert.False(result["isError"]!.GetValue<bool>());
        var payload = JsonNode.Parse(result["content"]![0]!["text"]!.GetValue<string>())!;
        Assert.Equal("list_sheets", payload["echoed"]!.GetValue<string>());
        Assert.Equal("A1.0", payload["sheet"]!.GetValue<string>());
    }

    [Fact]
    public async Task Bridge_passes_tool_errors_through()
    {
        File.WriteAllText(_dir.AppPaths.McpConfigFile, System.Text.Json.JsonSerializer.Serialize(_config, Json.Indented));
        _handler.Respond = (_, _) => ToolCallResult.Errors("Proposal rejected:", new[] { "Title block 'X' is not in the office standards profile." });

        var replies = await RunBridge("""{ "jsonrpc": "2.0", "id": 1, "method": "tools/call", "params": { "name": "propose_sheets", "arguments": {} } }""");
        var result = Assert.Single(replies)["result"]!;
        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains("- Title block 'X'", result["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Bridge_reports_wrong_token_clearly()
    {
        File.WriteAllText(_dir.AppPaths.McpConfigFile, System.Text.Json.JsonSerializer.Serialize(_config with { Token = "stale" }, Json.Indented));
        var replies = await RunBridge("""{ "jsonrpc": "2.0", "id": 1, "method": "tools/call", "params": { "name": "list_sheets" } }""");
        Assert.Contains("HTTP 401", replies[0]["result"]!["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Bridge_survives_garbage_input()
    {
        var replies = await RunBridge("not json", "", """{ "jsonrpc": "2.0", "id": 1, "method": "ping" }""");
        Assert.Equal(2, replies.Count);
        Assert.Equal(-32700, replies[0]["error"]!["code"]!.GetValue<int>());
    }
}

public class McpConfigTests
{
    [Fact]
    public void LoadOrCreate_is_stable_across_calls()
    {
        using var dir = new TempDir();
        var a = McpConfig.LoadOrCreate(dir.AppPaths);
        var b = McpConfig.LoadOrCreate(dir.AppPaths);
        Assert.Equal(a, b);
        Assert.Equal(64, a.Token.Length);
        Assert.Equal(McpConfig.DefaultPort, a.Port);
    }

    [Fact]
    public void Corrupt_config_is_regenerated()
    {
        using var dir = new TempDir();
        dir.AppPaths.EnsureCreated();
        File.WriteAllText(dir.AppPaths.McpConfigFile, "{ \"port\": 0, \"token\": \"\" }");
        Assert.Null(McpConfig.TryLoad(dir.AppPaths));
        Assert.NotEmpty(McpConfig.LoadOrCreate(dir.AppPaths).Token);
    }
}
