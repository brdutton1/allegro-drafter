using AllegroDrafter.Core.Checks;
using AllegroDrafter.Core.Proposals;
using AllegroDrafter.Core.Standards;
using AllegroDrafter.Revit.Model;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AllegroDrafter.Revit;

/// <summary>What happened when something asked to queue a proposal.</summary>
internal sealed record ProposeOutcome(Proposal? Proposal, IReadOnlyList<string> Errors)
{
    public const string RefusedHeading = "Proposal refused before reaching the designer. Fix these and propose again:";
    public bool Queued => Proposal is not null;
}

/// <summary>
/// The audit and proposal paths shared by the MCP tools and the in-Revit buttons.
/// Every model touch goes through the dispatcher.
/// </summary>
internal sealed class DrafterService
{
    private readonly RevitDispatcher _dispatcher;
    private readonly ProposalStore _proposals;
    private readonly StandardsStore _standards;
    private readonly Action _onNewProposal;

    public DrafterService(RevitDispatcher dispatcher, ProposalStore proposals, StandardsStore standards, Action onNewProposal)
    {
        _dispatcher = dispatcher;
        _proposals = proposals;
        _standards = standards;
        _onNewProposal = onNewProposal;
    }

    public AuditSettings AuditSettings => _standards.Current?.Manual.Audit ?? new AuditSettings();

    public IReadOnlyList<string> OfficeTemplates => _standards.Current?.ViewTemplates.Select(t => t.Name).ToList() ?? new List<string>();

    /// <summary>Reads the model and runs the audit. Unknown check ids throw before Revit is touched.</summary>
    public async Task<(SetFacts Facts, AuditReport Report)> RunAuditAsync(IReadOnlyCollection<string>? only = null)
    {
        var settings = AuditSettings;
        var templates = OfficeTemplates;
        if (only is { Count: > 0 }) SetAuditor.Run(new SetFacts(), settings, templates, only);
        var facts = await _dispatcher.InvokeAsync(app => FactsReader.Read(RequireProject(app), settings));
        if (_standards.Current is null)
            facts.ReadWarnings.Add("No standards profile loaded, so default audit rules were used and view templates weren't checked against the office list.");
        return (facts, SetAuditor.Run(facts, settings, templates, only));
    }

    /// <summary>
    /// Validates against standards and the live model, then queues it for the designer.
    /// dropEmpty (Finish Drafting): tag and dimension changes with nothing to place are dropped
    /// instead of refusing the batch. A batch left empty comes back unqueued with no errors.
    /// </summary>
    public async Task<ProposeOutcome> ProposeAsync(ProposalRequest request, bool dropEmpty = false)
    {
        var standardsErrors = ProposalValidator.Validate(request, _standards.Current);
        if (standardsErrors.Count > 0) return new ProposeOutcome(null, standardsErrors);

        var (documentTitle, modelErrors, labeled) = await _dispatcher.InvokeAsync(app =>
        {
            var doc = RequireProject(app);
            var changes = request.Changes;
            if (dropEmpty)
            {
                changes = ModelChecker.Annotate(doc, changes)
                    .Where(c => c is not TagViewChange { ExpectedCount: 0 } && c is not DimensionViewChange { ExpectedStrings: 0 })
                    .ToList();
                if (changes.Count == 0) return (doc.Title, new List<string>(), (IReadOnlyList<ProposalChange>)changes);
            }
            return (doc.Title, ModelChecker.Check(doc, changes, _standards.Current?.Manual.Audit), ModelChecker.Annotate(doc, changes));
        });
        if (modelErrors.Count > 0) return new ProposeOutcome(null, modelErrors);
        if (labeled.Count == 0) return new ProposeOutcome(null, Array.Empty<string>());

        var proposal = _proposals.Add(documentTitle, request with { Changes = labeled });
        _onNewProposal();
        return new ProposeOutcome(proposal, Array.Empty<string>());
    }

    public static Document RequireProject(UIApplication app)
    {
        var doc = app.ActiveUIDocument?.Document;
        if (doc is null) throw new NoProjectException("No project is open in Revit.");
        if (doc.IsFamilyDocument) throw new NoProjectException("The active Revit document is a family, not a project. Switch to the project.");
        return doc;
    }
}

internal sealed class NoProjectException : Exception
{
    public NoProjectException(string message) : base(message) { }
}
