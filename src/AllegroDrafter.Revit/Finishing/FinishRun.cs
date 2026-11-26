using AllegroDrafter.Core.Audit;
using AllegroDrafter.Core.Finishing;
using AllegroDrafter.Core.Proposals;

namespace AllegroDrafter.Revit.Finishing;

/// <summary>
/// Drives one Finish Drafting run: audit, plan the next step, queue it or ask, wait for the
/// designer, repeat. Event driven: an approve, reject, undo or answer moves it forward.
/// Every step re-reads the model, so nothing here caches what the model looks like.
/// </summary>
internal sealed class FinishRun
{
    private readonly App _app;
    private readonly object _gate = new();
    private bool _advancing;
    private bool _again;
    private string? _project;

    public FinishRun(App app)
    {
        _app = app;
        _app.Proposals.Changed += (_, _) => OnProposalsChanged();
    }

    /// <summary>Raised after any change the panel should show. May fire on any thread.</summary>
    public event EventHandler? Changed;

    public bool IsRunning { get; private set; }
    public FinishState State { get; private set; } = new();
    public FinishStep? Current { get; private set; }
    public string? CurrentProposalId { get; private set; }
    public string Status { get; private set; } = "";
    public DateTime? StartedUtc { get; private set; }

    /// <summary>Steps the run skipped and why (refused batch, rejected batch, nothing to do).</summary>
    public List<string> Notes { get; } = new();

    public bool HasRun => StartedUtc is not null;

    /// <summary>The designer's per-run override: stop at every batch, even ones the office rule applies on its own.</summary>
    public bool ReviewEveryBatch { get; set; }

    /// <summary>Batches applied this session under the office rule, without a click.</summary>
    public int AppliedByRule { get; private set; }

    /// <summary>Where the last finished run's change report was written, or null.</summary>
    public string? ReportPath { get; private set; }

    /// <summary>The project the run is on (from the last model read), or null before the first.</summary>
    public string? Project { get { lock (_gate) return _project; } }

    /// <summary>Every sheet this run changed and what changed on it. Undone batches drop out.</summary>
    public ChangeReport Report()
    {
        var project = Project;
        if (project is null) return ChangeReport.Build(Array.Empty<ChangeImpact>(), _ => null);
        return ChangeReport.Build(_app.FinishChanges.For(project).Impacts, id => _app.Proposals.Get(id)?.Status);
    }

    public void Start()
    {
        lock (_gate)
        {
            if (IsRunning) { Raise(); return; }
            IsRunning = true;
            // A new run starts from the model as it is. Answers from the last run are kept, so
            // the designer isn't asked twice in one session; skipped steps get a fresh chance.
            State.SkippedSteps.Clear();
            Notes.Clear();
            CurrentProposalId = null;
            StartedUtc ??= DateTime.UtcNow;
            Status = "Reading the model...";
        }
        Raise();
        _ = AdvanceAsync();
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!IsRunning) return;
            IsRunning = false;
            Status = "Stopped. Click Finish Drafting to pick up where you left off.";
            CurrentProposalId = null;
        }
        Raise();
    }

    /// <summary>The designer answered a card. When every card on screen has an answer, the run moves on.</summary>
    public void Answer(string cardId, string answer)
    {
        string? project;
        FinishState state;
        lock (_gate)
        {
            State.Answers[cardId] = answer;
            project = _project;
            state = State;
        }
        // Saved right away, so a crash or restart never asks this card again.
        if (project is not null) _app.FinishAnswers.Save(project, state);
        Log("answer", $"{cardId} = {answer}");
        var allAnswered = Current is FinishQuestions q && q.Cards.All(c => State.Answered(c.Id));
        Raise();
        if (allAnswered && IsRunning) _ = AdvanceAsync();
    }

    private void OnProposalsChanged()
    {
        string? id;
        FinishStep? step;
        lock (_gate) { id = CurrentProposalId; step = Current; }
        if (!IsRunning || id is null || _app.Proposals.Get(id) is not { } p) return;
        switch (p.Status)
        {
            case ProposalStatus.Applied:
                lock (_gate) CurrentProposalId = null;
                _ = AfterAppliedAsync(p);
                break;
            case ProposalStatus.Undone:
                lock (_gate) CurrentProposalId = null;
                _ = AdvanceAsync();
                break;
            case ProposalStatus.Rejected:
                lock (_gate)
                {
                    CurrentProposalId = null;
                    if (step is not null) State.SkippedSteps.Add(step.StepId);
                    Notes.Add($"{step?.StepTitle}: you rejected the batch, so the run skipped this step.");
                }
                _ = AdvanceAsync();
                break;
            case ProposalStatus.Failed:
                lock (_gate) Status = "The batch failed and nothing changed. Read why on its card, then Retry or Reject it.";
                Raise();
                break;
        }
    }

    /// <summary>Records which sheets the batch changed, then plans the next step.</summary>
    private async Task AfterAppliedAsync(Proposal applied)
    {
        var project = Project;
        if (project is not null)
        {
            try
            {
                var settings = _app.Standards.Current?.Manual.FinishDrafting ?? new Core.Standards.FinishDraftingSettings();
                var impacts = await _app.Dispatcher.InvokeAsync(app =>
                    Model.ImpactReader.Read(DrafterService.RequireProject(app), applied, settings));
                _app.FinishChanges.Record(project, applied.Id, impacts);
            }
            catch (Exception ex)
            {
                // The batch is in; only the report is short. Say so rather than stop the run.
                lock (_gate) Notes.Add($"{applied.Title}: applied, but the change report couldn't read where it shows ({ex.Message}).");
            }
        }
        Raise();
        await AdvanceAsync();
    }

    private async Task AdvanceAsync()
    {
        lock (_gate)
        {
            if (_advancing) { _again = true; return; }
            _advancing = true;
        }
        try
        {
            do
            {
                lock (_gate) _again = false;
                await StepOnceAsync();
            }
            while (IsRunning && _again);
        }
        finally
        {
            lock (_gate) _advancing = false;
        }
    }

    private async Task StepOnceAsync()
    {
        if (!IsRunning) return;
        SetStatus("Reading the model...");
        try
        {
            var project = await _app.Drafter.ActiveProjectTitleAsync();
            UseProject(project);
            _app.FinishChanges.BeginOrResume(project, DateTime.UtcNow);
            var (facts, report) = await _app.Drafter.RunAuditAsync();
            FinishState state;
            lock (_gate) state = State;
            var step = FinishPlanner.Next(facts, report, _app.Standards.Current, state);
            lock (_gate) Current = step;

            switch (step)
            {
                case FinishBatch batch:
                {
                    var request = batch.Request with { Title = $"Finish Drafting {batch.StepNumber}/{FinishPlanner.Steps.Count}: {batch.Request.Title}" };
                    var outcome = await _app.Drafter.ProposeAsync(request, dropEmpty: true, finishStep: batch.StepId);
                    if (outcome.Queued)
                    {
                        var queuedId = outcome.Proposal!.Id;
                        lock (_gate) CurrentProposalId = queuedId;
                        var replaced = outcome.Replaced ?? Array.Empty<Proposal>();
                        foreach (var old in replaced)
                        {
                            Log("replaced", $"{old.Id} ({old.FinishStep}) by {outcome.Proposal.Id}");
                            try { _app.Audit.Write(AuditEntry.From(old, ProposalStore.FinishDraftingUser)); }
                            catch { /* never let logging stop a run */ }
                        }
                        var replacedNote = replaced.Count == 0 ? ""
                            : $" It replaces {string.Join(", ", replaced.Select(r => r.Id))}, left open from before and planned against an older model.";

                        var wait = FinishPlanner.WaitReason(batch, _app.Standards.Current?.Manual.FinishDrafting);
                        if (wait is null && !ReviewEveryBatch)
                        {
                            // Office rule: this step applies without a click. Still its own Ctrl+Z, still in the
                            // log as applied by rule, and the change report lists every sheet it touched.
                            lock (_gate) AppliedByRule++;
                            SetStatus($"Applying \"{batch.Request.Title}\" by office rule...{replacedNote}");
                            Log("auto-apply", $"{queuedId} {batch.StepId}");
                            _ = _app.ApproveAsync(queuedId, byRule: true);
                            return;
                        }

                        var ask = batch.Request.Changes.Any(c => c.IsDestructive)
                            ? "This batch deletes. Read the list below, then approve or reject."
                            : "Check the batch below, then approve or reject. Rejecting skips this step.";
                        SetStatus(ask + replacedNote);
                        return;
                    }
                    // Refused (the model disagrees with the rule) or nothing left after pruning: note it and move on.
                    lock (_gate)
                    {
                        State.SkippedSteps.Add(batch.StepId);
                        Notes.Add(outcome.Errors.Count == 0
                            ? $"{batch.StepTitle}: nothing the tool can do here. Check it by hand."
                            : $"{batch.StepTitle}: skipped. {string.Join(" ", outcome.Errors.Take(3))}");
                        _again = true;
                    }
                    Raise();
                    return;
                }
                case FinishQuestions questions:
                    SetStatus($"{questions.Cards.Count} question{(questions.Cards.Count == 1 ? "" : "s")} the rules can't settle. Answer to continue.");
                    return;
                case FinishDone done:
                    lock (_gate)
                    {
                        IsRunning = false;
                        Status = done.Remaining.Count == 0
                            ? "Done. The audit has nothing left that fails."
                            : "Done with everything the button can do. What's left needs you or the office template:";
                    }
                    Log("done", string.Join(" | ", done.Remaining));
                    WriteReport(project, done.Remaining);
                    Raise();
                    return;
            }
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                IsRunning = false;
                Status = $"Stopped: {ex.Message}";
            }
            Raise();
        }
    }

    /// <summary>
    /// Writes the change report to reports\ as markdown and closes this run's list, so the next
    /// Finish Drafting starts a fresh one. A report that can't be written never stops the run.
    /// </summary>
    private void WriteReport(string project, IReadOnlyList<string> remaining)
    {
        try
        {
            var report = Report();
            var dir = _app.Paths.ReportsDir;
            Directory.CreateDirectory(dir);
            var safe = string.Concat(project.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
            var path = Path.Combine(dir, $"{safe} - Finish Drafting {DateTime.Now:yyyy-MM-dd HHmm}.md");
            File.WriteAllText(path, report.ToMarkdown(project, DateTime.Now, remaining));
            lock (_gate) ReportPath = path;
            Log("report", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lock (_gate) Notes.Add($"The change report couldn't be saved ({ex.Message}). It's still shown here.");
        }
        _app.FinishChanges.MarkFinished(project, DateTime.UtcNow);
    }

    /// <summary>
    /// Points the run at the project open in Revit. Card ids carry element ids, which only mean
    /// something inside one model, so each project gets its own saved answers.
    /// </summary>
    private void UseProject(string title)
    {
        lock (_gate)
        {
            if (string.Equals(_project, title, StringComparison.Ordinal)) return;
            _project = title;
            // Skips belong to the project they were made in, so a different project starts with none.
            State = _app.FinishAnswers.StateFor(title);
        }
    }

    /// <summary>Answers for the open project, for the get_finish_plan preview.</summary>
    public async Task<FinishState> StateForActiveProjectAsync()
    {
        var title = await _app.Drafter.ActiveProjectTitleAsync();
        lock (_gate)
            return string.Equals(_project, title, StringComparison.Ordinal) ? State : _app.FinishAnswers.StateFor(title);
    }

    private void SetStatus(string text)
    {
        lock (_gate) Status = text;
        Raise();
    }

    private void Log(string what, string detail)
    {
        try { _app.Audit.WriteNote($"finish-drafting {what}", detail, Environment.UserName); }
        catch { /* never let logging stop a run */ }
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
