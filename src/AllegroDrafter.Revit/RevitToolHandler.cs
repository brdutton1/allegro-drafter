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
    private readonly DrafterService _drafter;

    public RevitToolHandler(RevitDispatcher dispatcher, ProposalStore proposals, StandardsStore standards, DrafterService drafter)
    {
        _dispatcher = dispatcher;
        _proposals = proposals;
        _standards = standards;
        _drafter = drafter;
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
                case ToolNames.AuditSet:
                {
                    var only = Args.OptionalStringArray(args, "checks", "arguments");
                    var (_, report) = await _drafter.RunAuditAsync(only);
                    return ToolCallResult.Ok(report);
                }
                case ToolNames.GetFinishPlan:
                {
                    var (facts, report) = await _drafter.RunAuditAsync();
                    var state = App.Instance?.Finish is { } finish ? await finish.StateForActiveProjectAsync() : new Core.Finishing.FinishState();
                    var step = Core.Finishing.FinishPlanner.Next(facts, report, _standards.Current, state);
                    return ToolCallResult.Ok(step switch
                    {
                        Core.Finishing.FinishBatch b => (object)new
                        {
                            next = "batch", step = b.StepId, title = b.StepTitle, number = b.StepNumber, of = Core.Finishing.FinishPlanner.Steps.Count,
                            batch = b.Request.Title, rationale = b.Request.Rationale, changes = b.Request.Changes.Select(c => c.Describe()).ToList(),
                            waitsForDesigner = Core.Finishing.FinishPlanner.WaitReason(b, _standards.Current?.Manual.FinishDrafting) ?? "no: the office rule applies it without a click",
                        },
                        Core.Finishing.FinishQuestions q => new
                        {
                            next = "questions", step = q.StepId, title = q.StepTitle, number = q.StepNumber, of = Core.Finishing.FinishPlanner.Steps.Count,
                            questions = q.Cards.Select(c => new { c.Id, c.Prompt, c.Options, c.Recommended, c.ElementId, c.AllowText }).ToList(),
                        },
                        Core.Finishing.FinishDone d => new { next = "done", remaining = d.Remaining },
                        _ => new { next = "unknown" },
                    });
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
        var outcome = await _drafter.ProposeAsync(ProposalRequestParser.Parse(tool, args));
        if (!outcome.Queued) return ToolCallResult.Errors(ProposeOutcome.RefusedHeading, outcome.Errors);
        var proposal = outcome.Proposal!;
        return ToolCallResult.Ok(new
        {
            proposalId = proposal.Id,
            status = proposal.Status,
            changes = proposal.Changes.Count,
            message = "Queued. Nothing has changed in the model yet. The designer approves or rejects it in the Allegro Drafter pane in Revit. Use get_proposal to check the result.",
        });
    }

    private static Document RequireProject(UIApplication app) => DrafterService.RequireProject(app);
}
