using System.IO;
using System.Runtime.InteropServices;
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
        var assembly = typeof(App).Assembly.Location;
        var bridge = Path.Combine(Path.GetDirectoryName(assembly)!, "AllegroDrafter.Bridge.exe");
        var build = File.GetLastWriteTime(assembly);

        var server = app.Server?.IsRunning == true
            ? $"Running at {app.Config!.Endpoint}"
            : $"NOT running. {app.ServerError}";
        var profile = standards is null
            ? $"None loaded. Run Export Standards on the office template. {app.Standards.LoadError}"
            : $"From \"{standards.SourceDocument}\", exported {standards.ExportedUtc.ToLocalTime():g}" +
              (app.Standards.LoadError is null ? "" : $"\nWARNING: {app.Standards.LoadError}");

        var status =
            $"Add-in build: {build:g} (Revit {commandData.Application.Application.VersionNumber})\n" +
            $"MCP server: {server}\n" +
            $"Office standards: {profile}\n" +
            $"Pending proposals: {app.Proposals.PendingCount}" +
            (app.Proposals.RestoredCount > 0 ? $" ({app.Proposals.RestoredCount} restored from last session)" : "") + "\n" +
            $"Proposals saved to: {app.Proposals.SavePath}\n" +
            (app.Proposals.PersistenceWarning is null ? "" : $"WARNING: {app.Proposals.PersistenceWarning}\n") +
            $"Audit log: {app.Paths.AuditDir}";
        var claudeCode = $"claude mcp add --scope user allegro-drafter -- \"{bridge}\"";
        var claudeDesktop = $"\"allegro-drafter\": {{ \"command\": \"{bridge.Replace("\\", "\\\\")}\" }}";

        string? copied = null;
        while (true)
        {
            var dialog = new TaskDialog("Allegro Drafter: Status")
            {
                MainInstruction = "Allegro Drafter",
                MainContent = status,
                ExpandedContent =
                    $"Connect Claude Code (recommended), in PowerShell:\n  {claudeCode}\n\n" +
                    $"Connect Claude Desktop: Settings > Developer > Edit Config, add under \"mcpServers\":\n  {claudeDesktop}\n\n" +
                    $"The bridge reads the port and token from {app.Paths.McpConfigFile}. Keep that file private.",
                FooterText = copied is null ? "Click a copy button, then paste with Ctrl+V." : $"Copied {copied}. Paste with Ctrl+V.",
                CommonButtons = TaskDialogCommonButtons.Close,
                DefaultButton = TaskDialogResult.Close,
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Copy status", "Everything above, to paste into a message.");
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Copy Claude Code command", "Paste into PowerShell to connect Claude Code.");
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Copy Claude Desktop config line", "Paste inside \"mcpServers\" in claude_desktop_config.json.");

            var (label, text) = dialog.Show() switch
            {
                TaskDialogResult.CommandLink1 => ("the status", status),
                TaskDialogResult.CommandLink2 => ("the Claude Code command", claudeCode),
                TaskDialogResult.CommandLink3 => ("the Claude Desktop config line", claudeDesktop),
                _ => (null, null),
            };
            if (text is null) return Result.Succeeded;

            copied = TryCopy(text) ? label : null;
            if (copied is null) TaskDialog.Show("Allegro Drafter", "Windows wouldn't let go of the clipboard (another app is using it). Try again.");
        }
    }

    /// <summary>The clipboard is shared and sometimes briefly locked by another app. Retry a few times.</summary>
    private static bool TryCopy(string text)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetText(text);
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(100);
            }
            catch (ExternalException)
            {
                Thread.Sleep(100);
            }
        }
        return false;
    }
}
