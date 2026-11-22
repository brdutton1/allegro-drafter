using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AllegroDrafter.Revit.Commands;

[Transaction(TransactionMode.ReadOnly)]
public sealed class StatusCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var app = App.Instance;
        var standards = app.Standards.Current;
        var bridge = Path.Combine(Path.GetDirectoryName(typeof(App).Assembly.Location)!, "AllegroDrafter.Bridge.exe");

        var server = app.Server?.IsRunning == true
            ? $"Running at {app.Config!.Endpoint}"
            : $"NOT running. {app.ServerError}";
        var profile = standards is null
            ? $"None loaded. Run Export Standards on the office template. {app.Standards.LoadError}"
            : $"From \"{standards.SourceDocument}\", exported {standards.ExportedUtc.ToLocalTime():g}" +
              (app.Standards.LoadError is null ? "" : $"\nWARNING: {app.Standards.LoadError}");

        var dialog = new TaskDialog("Allegro Drafter: Status")
        {
            MainInstruction = "Allegro Drafter",
            MainContent =
                $"MCP server: {server}\n" +
                $"Office standards: {profile}\n" +
                $"Pending proposals: {app.Proposals.PendingCount}\n" +
                $"Audit log: {app.Paths.AuditDir}",
            ExpandedContent =
                "Connect Claude Desktop: Settings > Developer > Edit Config, add under \"mcpServers\":\n" +
                $"  \"allegro-drafter\": {{ \"command\": \"{bridge.Replace("\\", "\\\\")}\" }}\n\n" +
                "Connect Claude Code:\n" +
                $"  claude mcp add allegro-drafter -- \"{bridge}\"\n\n" +
                $"The bridge reads the port and token from {app.Paths.McpConfigFile}. Keep that file private.",
        };
        dialog.Show();
        return Result.Succeeded;
    }
}
