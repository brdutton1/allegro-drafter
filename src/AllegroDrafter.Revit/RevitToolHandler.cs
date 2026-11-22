using System.Text.Json.Nodes;
using AllegroDrafter.Core.Mcp;
using AllegroDrafter.Core.Proposals;
using AllegroDrafter.Core.Standards;
using AllegroDrafter.Revit.Model;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AllegroDrafter.Revit;

/// <summary>Answers MCP tool calls against the open Revit project.</summary>
internal sealed class RevitToolHandler : IToolHandler
{
    private readonly RevitDispatcher _dispatcher;
    private readonly ProposalStore _proposals;
    private readonly StandardsStore _standards;
    private readonly Action _onNewProposal;

    public RevitToolHandler(RevitDispatcher dispatcher, ProposalStore proposals, StandardsStore standards, Action onNewProposal)
    {
        _dispatcher = dispatcher;
        _proposals = proposals;
        _standards = standards;
        _onNewProposal = onNewProposal;
    }

    public async Task<ToolCallResult> CallAsync(string name, JsonObject args, CancellationToken ct)
    {
        try
        {
            switch (name)
            {
                case ToolNames.GetStandards: return CoreTools.GetStandards(_standards, args);
                case ToolNames.ListProposals: return CoreTools.ListProposals(_proposals, args);
                case ToolNames.GetProposal: return CoreTools.GetProposal(_proposals, args);

                case ToolNames.GetProjectInfo: return await Read((app, doc) => ModelReader.ProjectInfo(app, doc));
                case ToolNames.ListLevels: return await Read((_, doc) => ModelReader.Levels(doc));
                case ToolNames.ListSheets: return await Read((_, doc) => ModelReader.Sheets(doc));
                case ToolNames.CountElements: return await Read((_, doc) => ModelReader.CountElements(doc));
                case ToolNames.ListViews:
                {
                    var viewType = Args.OptionalString(args, "viewType", "arguments");
                    var unplaced = Args.OptionalBool(args, "unplacedOnly", "arguments");
                    return await Read((_, doc) => ModelReader.Views(doc, viewType, unplaced));
                }
                case ToolNames.ListSchedules: return await Read((_, doc) => ScheduleReader.Schedules(doc));
                case ToolNames.ListElements:
                {
                    var category = Args.RequiredString(args, "category", "arguments");
                    var level = Args.OptionalString(args, "level", "arguments");
                    var parameters = Args.OptionalStringArray(args, "parameters", "arguments");
                    var withList = Args.OptionalBool(args, "includeParameterList", "arguments");
                    var finishParameters = _standards.Current?.Manual.FinishParameters ?? new Core.Standards.ManualSettings().FinishParameters;
                    return await Read((_, doc) => ScheduleReader.Elements(doc, category, level, parameters, withList, finishParameters));
                }
                case ToolNames.ReadLegend:
                {
                    var legend = Args.OptionalString(args, "name", "arguments");
                    return await Read((_, doc) => ScheduleReader.ReadLegend(doc, legend));
                }
                case ToolNames.ListRooms:
                {
                    var level = Args.OptionalString(args, "level", "arguments");
                    return await Read((_, doc) => ModelReader.Rooms(doc, level));
                }

                default:
                    if (ToolNames.ProposalTools.Contains(name)) return await Propose(name, args);
                    return ToolCallResult.Error($"Tool '{name}' is not implemented in this add-in version.");
            }
        }
        catch (NoProjectException ex)
        {
            return ToolCallResult.Error(ex.Message);
        }
        catch (TimeoutException ex)
        {
            return ToolCallResult.Error(ex.Message);
        }
    }

    private async Task<ToolCallResult> Read(Func<UIApplication, Document, object> query)
    {
        var payload = await _dispatcher.InvokeAsync(app => query(app, RequireProject(app)));
        return ToolCallResult.Ok(payload);
    }

    private async Task<ToolCallResult> Propose(string tool, JsonObject args)
    {
        var request = ProposalRequestParser.Parse(tool, args);

        var standardsErrors = ProposalValidator.Validate(request, _standards.Current);
        if (standardsErrors.Count > 0)
            return ToolCallResult.Errors("Proposal refused before reaching the designer. Fix these and propose again:", standardsErrors);

        var (documentTitle, modelErrors, labeled) = await _dispatcher.InvokeAsync(app =>
        {
            var doc = RequireProject(app);
            return (doc.Title, ModelChecker.Check(doc, request.Changes), ModelChecker.WithModelLabels(doc, request.Changes));
        });
        if (modelErrors.Count > 0)
            return ToolCallResult.Errors("Proposal refused before reaching the designer. Fix these and propose again:", modelErrors);
        request = request with { Changes = labeled };

        var proposal = _proposals.Add(documentTitle, request);
        _onNewProposal();
        return ToolCallResult.Ok(new
        {
            proposalId = proposal.Id,
            status = proposal.Status,
            changes = proposal.Changes.Count,
            message = "Queued. Nothing has changed in the model yet. The designer approves or rejects it in the Allegro Drafter pane in Revit. Use get_proposal to check the result.",
        });
    }

    private static Document RequireProject(UIApplication app)
    {
        var doc = app.ActiveUIDocument?.Document;
        if (doc is null) throw new NoProjectException("No project is open in Revit.");
        if (doc.IsFamilyDocument) throw new NoProjectException("The active Revit document is a family, not a project. Switch to the project.");
        return doc;
    }

    private sealed class NoProjectException : Exception
    {
        public NoProjectException(string message) : base(message) { }
    }
}
