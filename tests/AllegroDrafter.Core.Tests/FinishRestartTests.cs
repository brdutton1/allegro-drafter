using AllegroDrafter.Core.Finishing;
using AllegroDrafter.Core.Proposals;

namespace AllegroDrafter.Core.Tests;

/// <summary>
/// Finish Drafting across Stop/Resume and Revit restarts: one open batch per project,
/// never two copies of the same work, and answers that stick.
/// </summary>
public class FinishRestartTests
{
    private static readonly DateTime Now = new(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);

    private static ProposalRequest Marks(string title = "Type Marks") => new(title, "size codes", new ProposalChange[]
    {
        new SetTypeMarkChange("doors", "Single-Flush: 36x80", "3080", "376", "3'-0\" x 8'-0\"", 4),
    });

    [Fact]
    public void New_batch_replaces_the_open_one_left_by_a_stop()
    {
        var store = new ProposalStore(() => Now);
        var first = store.AddFinishBatch("25-0083", Marks(), "type-marks").Added;

        var (second, replaced) = store.AddFinishBatch("25-0083", Marks(), "type-marks");

        Assert.Equal(new[] { first.Id }, replaced.Select(p => p.Id));
        Assert.Equal(ProposalStatus.Rejected, store.Get(first.Id)!.Status);
        Assert.Contains($"Replaced by {second.Id}", store.Get(first.Id)!.ResultMessage);
        Assert.Equal(ProposalStore.FinishDraftingUser, store.Get(first.Id)!.ResolvedBy);
        Assert.False(store.Get(first.Id)!.CanApprove); // the old copy can never be approved
        Assert.Equal(new[] { second.Id }, store.OpenFinishBatches("25-0083").Select(p => p.Id));
        Assert.Equal(1, store.PendingCount);
    }

    [Fact]
    public void Batch_left_from_before_a_restart_is_replaced_not_duplicated()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "proposals.json");
        var before = new ProposalStore(() => Now, path).AddFinishBatch("25-0083", Marks(), "type-marks").Added;

        var after = new ProposalStore(() => Now.AddHours(1), path);
        var restored = after.Get(before.Id)!;
        Assert.Equal("type-marks", restored.FinishStep); // the step tag survives the restart
        Assert.Single(after.OpenFinishBatches("25-0083"));

        var (fresh, replaced) = after.AddFinishBatch("25-0083", Marks(), "type-marks");
        Assert.Equal(new[] { before.Id }, replaced.Select(p => p.Id));
        Assert.Equal(new[] { fresh.Id }, after.OpenFinishBatches("25-0083").Select(p => p.Id));

        // And that's what's on disk too.
        var reread = new ProposalStore(() => Now.AddHours(1), path);
        Assert.Equal(ProposalStatus.Rejected, reread.Get(before.Id)!.Status);
        Assert.Equal(new[] { fresh.Id }, reread.OpenFinishBatches("25-0083").Select(p => p.Id));
    }

    [Fact]
    public void Batch_interrupted_mid_apply_is_replaced_when_the_run_resumes()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "proposals.json");
        var store = new ProposalStore(() => Now, path);
        var p = store.AddFinishBatch("25-0083", Marks(), "type-marks").Added;
        store.TryBeginApply(p.Id); // Revit dies here and rolls the transaction back.

        var after = new ProposalStore(() => Now, path);
        Assert.Equal(ProposalStatus.Failed, after.Get(p.Id)!.Status);
        Assert.True(after.Get(p.Id)!.IsOpen); // still retryable on its own...

        var (_, replaced) = after.AddFinishBatch("25-0083", Marks(), "type-marks");
        Assert.Single(replaced); // ...but the resumed run's fresh plan takes its place
        Assert.False(after.Get(p.Id)!.CanApprove);
    }

    [Fact]
    public void Replacing_moves_to_a_later_step_too()
    {
        var store = new ProposalStore(() => Now);
        var marks = store.AddFinishBatch("25-0083", Marks(), "type-marks").Added;
        var (_, replaced) = store.AddFinishBatch("25-0083", Marks("Schedules"), "schedules");
        Assert.Equal(new[] { marks.Id }, replaced.Select(p => p.Id));
    }

    [Fact]
    public void Other_projects_and_other_proposals_are_never_touched()
    {
        var store = new ProposalStore(() => Now);
        var otherProject = store.AddFinishBatch("25-0090", Marks(), "type-marks").Added;
        var fromClaude = store.Add("25-0083", Marks("Claude's proposal"));

        var (_, replaced) = store.AddFinishBatch("25-0083", Marks(), "type-marks");

        Assert.Empty(replaced);
        Assert.Equal(ProposalStatus.Pending, store.Get(otherProject.Id)!.Status);
        Assert.Equal(ProposalStatus.Pending, store.Get(fromClaude.Id)!.Status);
        Assert.Null(store.Get(fromClaude.Id)!.FinishStep);
    }

    [Fact]
    public void Applied_applying_and_undone_batches_are_never_replaced()
    {
        var store = new ProposalStore(() => Now);
        var applied = store.AddFinishBatch("doc", Marks(), "gate").Added;
        store.TryBeginApply(applied.Id);
        store.CompleteApply(applied.Id, true, "ok", Array.Empty<long>(), "bryan");
        var undone = store.AddFinishBatch("doc", Marks(), "parameters").Added;
        store.TryBeginApply(undone.Id);
        store.CompleteApply(undone.Id, true, "ok", Array.Empty<long>(), "bryan");
        store.MarkUndone(undone.Id);
        var applying = store.AddFinishBatch("doc", Marks(), "type-marks").Added;
        store.TryBeginApply(applying.Id);

        var (_, replaced) = store.AddFinishBatch("doc", Marks(), "schedules");

        Assert.Empty(replaced);
        Assert.Equal(ProposalStatus.Applied, store.Get(applied.Id)!.Status);
        Assert.Equal(ProposalStatus.Undone, store.Get(undone.Id)!.Status);
        Assert.Equal(ProposalStatus.Applying, store.Get(applying.Id)!.Status);
    }

    [Fact]
    public void Proposals_saved_before_this_change_still_load()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "proposals.json");
        // Shape written by the add-in before FinishStep existed.
        File.WriteAllText(path, """
            [{"id":"P007-55ac","documentTitle":"25-0083","title":"Door schedule sort","rationale":"",
              "changes":[],"createdUtc":"2026-11-22T20:00:00Z","status":"pending","createdElementIds":[],"attempts":0}]
            """);

        var store = new ProposalStore(() => Now, path);
        var p = Assert.Single(store.List());
        Assert.Null(p.FinishStep);
        Assert.Null(store.PersistenceWarning);
        Assert.Empty(store.OpenFinishBatches("25-0083"));
    }

    [Fact]
    public void Step_id_is_required_for_a_finish_batch()
    {
        var store = new ProposalStore(() => Now);
        Assert.Throws<ArgumentException>(() => store.AddFinishBatch("doc", Marks(), " "));
    }

    // ------------------------------------------------------------------ answers

    [Fact]
    public void Answers_survive_a_restart()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "finish-answers.json");
        var state = new FinishState();
        state.Answers["swing:5124872"] = "LH";
        state.Answers["tempered:670"] = "No: closet 3' deep or less";
        new FinishAnswersStore(path).Save("25-0083", state);

        var back = new FinishAnswersStore(path).StateFor("25-0083");
        Assert.Equal("LH", back.Answer("swing:5124872"));
        Assert.Equal("No: closet 3' deep or less", back.Answer("TEMPERED:670")); // ids stay case-insensitive
        Assert.Empty(back.SkippedSteps); // skips get a fresh chance every run
    }

    [Fact]
    public void Answers_are_kept_apart_by_project()
    {
        var store = new FinishAnswersStore();
        var a = new FinishState();
        a.Answers["swing:100"] = "LH";
        store.Save("25-0083", a);

        // Element 100 in another model is a different door.
        Assert.False(store.StateFor("25-0090").Answered("swing:100"));
        Assert.True(store.StateFor("25-0083").Answered("swing:100"));
    }

    [Fact]
    public void State_handed_out_is_a_copy()
    {
        var store = new FinishAnswersStore();
        var a = new FinishState();
        a.Answers["x"] = "1";
        store.Save("doc", a);
        store.StateFor("doc").Answers["y"] = "2"; // not saved
        Assert.False(store.StateFor("doc").Answered("y"));
    }

    [Fact]
    public void Corrupt_answers_file_is_set_aside()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "finish-answers.json");
        File.WriteAllText(path, "{ nope");

        var store = new FinishAnswersStore(path);
        Assert.Contains("couldn't be read", store.Warning);
        Assert.Single(Directory.GetFiles(dir.Path, "finish-answers.json.broken-*"));
        Assert.False(store.StateFor("doc").Answered("x"));

        var s = new FinishState();
        s.Answers["x"] = "1";
        store.Save("doc", s);
        Assert.Null(store.Warning);
        Assert.True(new FinishAnswersStore(path).StateFor("doc").Answered("x"));
    }
}
