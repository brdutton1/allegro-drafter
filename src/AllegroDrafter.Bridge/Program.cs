using System.Text;
using AllegroDrafter.Core;
using AllegroDrafter.Core.Mcp;

// stdout carries the MCP protocol. Anything diagnostic goes to stderr.
var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
using var input = new StreamReader(Console.OpenStandardInput(), utf8);
using var output = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = false, NewLine = "\n" };

var paths = new AppPaths(Environment.GetEnvironmentVariable("ALLEGRO_DRAFTER_HOME"));
using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromMinutes(2) };

using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancel.Cancel();
};

try
{
    await new BridgeRelay(paths, http).RunAsync(input, output, cancel.Token);
    return 0;
}
catch (OperationCanceledException)
{
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"allegro-drafter bridge stopped: {ex}");
    return 1;
}
