using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AllegroDrafter.Core.Mcp;

/// <summary>
/// MCP "streamable HTTP" endpoint on localhost only, JSON responses, no SSE.
/// Every request must carry the bearer token from mcp.json. Requests from web
/// pages (any Origin header that isn't localhost) are refused.
/// </summary>
public sealed class McpHttpServer : IDisposable
{
    private const int MaxBodyBytes = 4 * 1024 * 1024;

    private readonly McpConfig _config;
    private readonly McpProtocol _protocol;
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;

    public McpHttpServer(McpConfig config, McpProtocol protocol)
    {
        _config = config;
        _protocol = protocol;
        _listener.Prefixes.Add($"http://localhost:{config.Port}/");
    }

    public bool IsRunning => _listener.IsListening;

    /// <summary>Throws HttpListenerException if the port is taken or not permitted.</summary>
    public void Start()
    {
        _listener.Start();
        _loop = Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (_stop.IsCancellationRequested || !_listener.IsListening)
            {
                return;
            }
            catch (HttpListenerException)
            {
                continue;
            }
            _ = Task.Run(() => HandleAsync(context));
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        try
        {
            var path = request.Url?.AbsolutePath.TrimEnd('/') ?? "";
            if (!string.Equals(path, "/mcp", StringComparison.OrdinalIgnoreCase))
            {
                await WriteText(response, 404, "Not found. The MCP endpoint is /mcp.").ConfigureAwait(false);
                return;
            }

            if (!IsAllowedOrigin(request.Headers["Origin"]))
            {
                await WriteText(response, 403, "Requests from web pages are not allowed.").ConfigureAwait(false);
                return;
            }

            if (!HasValidToken(request.Headers["Authorization"]))
            {
                response.AddHeader("WWW-Authenticate", "Bearer");
                await WriteText(response, 401, "Missing or wrong access token. See %APPDATA%\\AllegroDrafter\\mcp.json.").ConfigureAwait(false);
                return;
            }

            if (request.HttpMethod != "POST")
            {
                response.AddHeader("Allow", "POST");
                await WriteText(response, 405, "Only POST is supported (no SSE stream).").ConfigureAwait(false);
                return;
            }

            if (request.ContentLength64 > MaxBodyBytes)
            {
                await WriteText(response, 413, "Request too large.").ConfigureAwait(false);
                return;
            }

            string body;
            using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
            {
                body = await reader.ReadToEndAsync().ConfigureAwait(false);
            }
            if (body.Length > MaxBodyBytes)
            {
                await WriteText(response, 413, "Request too large.").ConfigureAwait(false);
                return;
            }

            JsonNode? message;
            try
            {
                message = JsonNode.Parse(body);
            }
            catch (JsonException)
            {
                await WriteJson(response, 400, McpProtocol.Error(null, -32700, "Parse error.")).ConfigureAwait(false);
                return;
            }

            var reply = await _protocol.HandleAsync(message, _stop.Token).ConfigureAwait(false);
            if (reply is null)
            {
                response.StatusCode = 202;
                response.Close();
                return;
            }
            await WriteJson(response, 200, reply).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or IOException)
        {
            // Client went away. Nothing to do.
        }
        catch (Exception ex)
        {
            try
            {
                await WriteJson(response, 500, McpProtocol.Error(null, -32603, $"Internal error: {ex.Message}")).ConfigureAwait(false);
            }
            catch
            {
                // Response already broken.
            }
        }
    }

    internal static bool IsAllowedOrigin(string? origin)
    {
        if (string.IsNullOrEmpty(origin)) return true; // Non-browser clients send no Origin.
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        return uri.IsLoopback;
    }

    private bool HasValidToken(string? header)
    {
        const string prefix = "Bearer ";
        if (header is null || !header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var supplied = Encoding.UTF8.GetBytes(header[prefix.Length..].Trim());
        var expected = Encoding.UTF8.GetBytes(_config.Token);
        return CryptographicOperations.FixedTimeEquals(supplied, expected);
    }

    private static async Task WriteJson(HttpListenerResponse response, int status, JsonNode body)
    {
        var bytes = Encoding.UTF8.GetBytes(body.ToJsonString(Json.Compact));
        response.StatusCode = status;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        response.Close();
    }

    private static async Task WriteText(HttpListenerResponse response, int status, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        response.StatusCode = status;
        response.ContentType = "text/plain; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        response.Close();
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            if (_listener.IsListening) _listener.Stop();
            _listener.Close();
        }
        catch (ObjectDisposedException)
        {
        }
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }
        _stop.Dispose();
    }
}
