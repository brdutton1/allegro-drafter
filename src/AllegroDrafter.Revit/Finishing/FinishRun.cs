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
        lock (_gate) State.Answers[cardId] = answer;
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
            var (facts, report) = await _app.Drafter.RunAuditAsync();
            var step = FinishPlanner.Next(facts, report, _app.Standards.Current, State);
            lock (_gate) Current = step;

            switch (step)
            {
                case FinishBatch batch:
                {
                    var request = batch.Request with { Title = $"Finish Drafting {batch.StepNumber}/{FinishPlanner.Steps.Count}: {batch.Request.Title}" };
                    var outcome = await _app.Drafter.ProposeAsync(request, dropEmpty: true);
                    if (outcome.Queued)
                    {
                        lock (_gate) CurrentProposalId = outcome.Proposal!.Id;
                        SetStatus(batch.Request.Changes.Any(c => c.IsDestructive)
                            ? "This batch deletes. Read the list below, then approve or reject."
                            : "Check the batch below, then approve or reject. Rejecting skips this step.");
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
