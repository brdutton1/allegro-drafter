using System.Net;
using System.Reflection;
using AllegroDrafter.Core;
using AllegroDrafter.Core.Audit;
using AllegroDrafter.Core.Finishing;
using AllegroDrafter.Core.Mcp;
using AllegroDrafter.Core.Proposals;
using AllegroDrafter.Core.Standards;
using AllegroDrafter.Revit.Commands;
using AllegroDrafter.Revit.Model;
using AllegroDrafter.Revit.UI;
using Autodesk.Revit.UI;

namespace AllegroDrafter.Revit;

public sealed class App : IExternalApplication
{
    internal static readonly DockablePaneId PaneId = new(new Guid("B3E1A0E4-6C3B-4F7A-8C61-2C0F7D3E9A15"));

    internal static App Instance { get; private set; } = null!;

    internal AppPaths Paths { get; private set; } = null!;
    internal ProposalStore Proposals { get; private set; } = null!;
    internal StandardsStore Standards { get; private set; } = null!;
    internal AuditLog Audit { get; private set; } = null!;
    internal RevitDispatcher Dispatcher { get; private set; } = null!;
    internal DrafterService Drafter { get; private set; } = null!;
    internal Finishing.FinishRun Finish { get; private set; } = null!;
    internal FinishAnswersStore FinishAnswers { get; private set; } = null!;
    internal FinishChangesStore FinishChanges { get; private set; } = null!;
    internal McpConfig? Config { get; private set; }
    internal McpHttpServer? Server { get; private set; }
    internal string? ServerError { get; private set; }

    public Result OnStartup(UIControlledApplication application)
    {
        Instance = this;
        Paths = new AppPaths(Environment.GetEnvironmentVariable("ALLEGRO_DRAFTER_HOME"));
        Paths.EnsureCreated();
        Proposals = new ProposalStore(savePath: Paths.ProposalsFile);
        Standards = new StandardsStore(Paths);
        Audit = new AuditLog(Paths);
        Dispatcher = new RevitDispatcher();
        Drafter = new DrafterService(Dispatcher, Proposals, Standards, ShowPaneSoon);
        FinishAnswers = new FinishAnswersStore(Paths.FinishAnswersFile);
        FinishChanges = new FinishChangesStore(Paths.FinishChangesFile);
        Finish = new Finishing.FinishRun(this);

        var pane = new ApprovalPane(this);
        application.RegisterDockablePane(PaneId, "Allegro Drafter", new ApprovalPaneProvider(pane));
        BuildRibbon(application);
        application.ControlledApplication.DocumentChanged += OnDocumentChanged;
        StartServer();
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        application.ControlledApplication.DocumentChanged -= OnDocumentChanged;
        Server?.Dispose();
        return Result.Succeeded;
    }

    private void StartServer()
    {
        try
        {
            Config = McpConfig.LoadOrCreate(Paths);
            var handler = new RevitToolHandler(Dispatcher, Proposals, Standards, Drafter);
            Server = new McpHttpServer(Config, new McpProtocol(handler));
            Server.Start();
            ServerError = null;
        }
        catch (HttpListenerException ex)
        {
            Server?.Dispose();
            Server = null;
            ServerError = $"Could not listen on port {Config?.Port}: {ex.Message}. Another Revit session may already be running Allegro Drafter, or the port is blocked. Change \"port\" in {Paths.McpConfigFile} and restart Revit.";
        }
        catch (Exception ex)
        {
            Server?.Dispose();
            Server = null;
            ServerError = $"MCP server failed to start: {ex.Message}";
        }
    }

    /// <summary>Pops the approval pane open when the AI queues something.</summary>
    private void ShowPaneSoon()
    {
        _ = Dispatcher.InvokeAsync(app =>
        {
            app.GetDockablePane(PaneId)?.Show();
            return true;
        }).ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>
    /// Approve button. Safe to call twice: the store only lets one through.
    /// byRule: Finish Drafting applied it under the office's auto-apply rule; the log says so.
    /// </summary>
    internal async Task ApproveAsync(string proposalId, bool byRule = false)
    {
        if (!Proposals.TryBeginApply(proposalId)) return;
        var proposal = Proposals.Get(proposalId)!;
        var user = byRule ? $"{Environment.UserName} (office rule)" : Environment.UserName;

        ApplyResult result;
        try
        {
            result = await Dispatcher.InvokeAsync(app =>
            {
                var doc = app.ActiveUIDocument?.Document;
                return doc is null
                    ? ApplyResult.Fail("No project is open.")
                    : ProposalApplier.Apply(doc, proposal, Standards.Current);
            }, TimeSpan.FromMinutes(5));
        }
        catch (Exception ex)
        {
            result = ApplyResult.Fail(ex.Message);
        }

        Proposals.CompleteApply(proposalId, result.Success, result.Message, result.CreatedElementIds, user);
        Audit.Write(AuditEntry.From(proposal, user));
    }

    /// <summary>
    /// Ctrl+Z / Ctrl+Y on one of our transactions. Only parses names and updates the
    /// store: no model reads, so it's safe inside Revit's event.
    /// </summary>
    private void OnDocumentChanged(object? sender, Autodesk.Revit.DB.Events.DocumentChangedEventArgs e)
    {
        var op = e.Operation;
        var undone = op == Autodesk.Revit.DB.Events.UndoOperation.TransactionUndone;
        if (!undone && op != Autodesk.Revit.DB.Events.UndoOperation.TransactionRedone) return;
        var user = Environment.UserName;
        foreach (var name in e.GetTransactionNames())
        {
            if (UndoNames.ParseId(name) is not { } id) continue;
            var changed = undone ? Proposals.MarkUndone(id) : Proposals.MarkRedone(id);
            if (!changed || Proposals.Get(id) is not { } p) continue;
            try { Audit.Write(AuditEntry.From(p, user)); }
            catch { /* Logging must never break undo. */ }
        }
    }

    internal void Reject(string proposalId)
    {
        var user = Environment.UserName;
        if (!Proposals.Reject(proposalId, user)) return;
        Audit.Write(AuditEntry.From(Proposals.Get(proposalId)!, user));
    }

    private static void BuildRibbon(UIControlledApplication application)
    {
        const string tab = "Allegro";
        try
        {
            application.CreateRibbonTab(tab);
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            // Tab already exists (another Allegro add-in made it).
        }

        var panel = application.CreateRibbonPanel(tab, "Drafter");
        var assembly = Assembly.GetExecutingAssembly().Location;

        PushButtonData Button(string name, string text, Type command, string tooltip) =>
            new(name, text, assembly, command.FullName) { ToolTip = tooltip };

        panel.AddItem(Button("AllegroFinishDrafting", "Finish\nDrafting", typeof(FinishDraftingCommand),
            "Run the office rules on this set, one batch at a time. You approve every batch and answer what the rules can't settle."));
        panel.AddItem(Button("AllegroApprovals", "Approvals", typeof(ShowApprovalsCommand),
            "Open the Allegro Drafter pane to approve or reject what the AI proposed."));
        panel.AddItem(Button("AllegroExportStandards", "Export\nStandards", typeof(ExportStandardsCommand),
            "Export title blocks, view templates, schedules, details and types from this file into the office standards profile."));
        panel.AddItem(Button("AllegroStatus", "Status", typeof(StatusCommand),
            "Connection status and how to connect Claude."));
    }
}
