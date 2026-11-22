using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using AllegroDrafter.Core;
using AllegroDrafter.Core.Mcp;
using AllegroDrafter.Core.Standards;

namespace AllegroDrafter.Core.Tests;

public sealed class TempDir : IDisposable
{
    public TempDir() => Directory.CreateDirectory(Path);
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "allegro-tests-" + Guid.NewGuid().ToString("N"));
    public AppPaths AppPaths => new(Path);
    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
    }
}

public static class Fixtures
{
    public static StandardsProfile Profile() => new()
    {
        SourceDocument = "Allegro Residential Template",
        ExportedUtc = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc),
        RevitVersion = "2025",
        TitleBlocks = { "Allegro 24x36: Standard", "Allegro 11x17: Half Size" },
        ViewTemplates =
        {
            new ViewTemplateInfo("A - Floor Plan 1/4\"", "FloorPlan"),
            new ViewTemplateInfo("A - Exterior Elevation", "Elevation"),
        },
        Manual =
        {
            Rules = { "Architectural sheets start at A1.0." },
        },
    };

    public static JsonObject Obj(string json) => JsonNode.Parse(json)!.AsObject();

    public static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

/// <summary>Records calls and returns a canned result.</summary>
public sealed class FakeToolHandler : IToolHandler
{
    public List<(string Name, JsonObject Args)> Calls { get; } = new();
    public Func<string, JsonObject, ToolCallResult> Respond { get; set; } = (n, _) => ToolCallResult.Ok(new { tool = n });

    public Task<ToolCallResult> CallAsync(string name, JsonObject arguments, CancellationToken cancellationToken)
    {
        lock (Calls) Calls.Add((name, arguments));
        return Task.FromResult(Respond(name, arguments));
    }
}
