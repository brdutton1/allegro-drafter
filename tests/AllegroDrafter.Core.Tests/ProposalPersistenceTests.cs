using AllegroDrafter.Core.Proposals;

namespace AllegroDrafter.Core.Tests;

public class ProposalPersistenceTests
{
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);

    private static ProposalRequest EveryKind() => new("Everything", "round trip", new ProposalChange[]
    {
        new CreateSheetChange("A1", "PLAN", "Allegro 24x36: Standard"),
        new PlaceViewChange("A1", "Level 1", "FloorPlan", 1, 2),
        new ApplyViewTemplateChange("Level 1", "A - Plan", "FloorPlan"),
        new CreateScheduleChange("DOOR SCHEDULE", SourceSchedule: "OFFICE DOOR"),
        new SetParametersChange(42, "Room 104 KITCHEN", new[] { new ParameterValue("Floor Finish", "T1") }),
        new TagViewChange("Level 1", "doors", "Door Tag: Standard", ExpectedCount: 12),
        new CalloutChange("Level 1", new CalloutArea(ElementIds: new long[] { 1, 2 }), Name: "ENLARGED", ViewFamilyType: "Detail View", Scale: 24),
        new CalloutChange("Level 1", new CalloutArea(MinX: 0, MinY: 0, MaxX: 5, MaxY: 5), ReferenceView: "DETAIL 1"),
        new TextNoteChange("Level 1", "NOTE\nTWO", "3/32\" Arial", 1, 2, LeaderToElementId: 7),
        new DimensionViewChange("Level 1", new[] { "openings", "overall" }, "Linear: Arch", ExpectedStrings: 6),
        new ScheduleFieldsChange("DOOR SCHEDULE", new[] { new FieldToAdd("Mark", "Type") }, new[] { "Type Comments" }),
        new SetTypeMarkChange("doors", "Single-Flush: 36x80", "3080", "376", "3'-0\" x 8'-0\"", 4),
        new CreateSharedParameterChange("Tempered", "yesno", new[] { "doors", "windows" }),
        new ScheduleSortChange("DOOR SCHEDULE", new[] { "Type Mark", "Door Swing" }, false),
        new DeleteRoomsChange(new long[] { 900, 901 }, Summary: "2 unplaced"),
    });

    [Fact]
    public void Every_change_kind_survives_a_restart()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "proposals.json");
        var original = new ProposalStore(() => Now, path).Add("Smith House", EveryKind());

        var restored = new ProposalStore(() => Now, path);
        var p = Assert.Single(restored.List());
        Assert.Equal(original.Id, p.Id);
        Assert.Equal(ProposalStatus.Pending, p.Status);
        Assert.Equal("Smith House", p.DocumentTitle);
        Assert.Equal(original.Changes, p.Changes);
        Assert.Equal(original.Changes.Select(c => c.Describe()), p.Changes.Select(c => c.Describe()));
        Assert.Equal(1, restored.RestoredCount);
        Assert.True(p.Changes.OfType<DeleteRoomsChange>().Single().IsDestructive);
        Assert.Null(restored.PersistenceWarning);
    }

    [Fact]
    public void Crash_mid_apply_comes_back_failed_never_reapplied()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "proposals.json");
        var store = new ProposalStore(() => Now, path);
        var p = store.Add("doc", EveryKind());
        store.TryBeginApply(p.Id); // Saved as Applying, then "Revit dies".

        var restored = new ProposalStore(() => Now, path).Get(p.Id)!;
        Assert.Equal(ProposalStatus.Failed, restored.Status);
        Assert.Equal(ProposalStore.InterruptedMessage, restored.ResultMessage);
        Assert.True(restored.CanApprove); // Designer can Retry after checking.
    }

    [Fact]
    public void Old_resolved_proposals_are_pruned_but_pending_ones_never_are()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "proposals.json");
        var then = Now.AddDays(-30);
        var store = new ProposalStore(() => then, path);
        var pending = store.Add("doc", EveryKind());
        var rejected = store.Add("doc", EveryKind());
        store.Reject(rejected.Id, "bryan");

        var restored = new ProposalStore(() => Now, path);
        Assert.Equal(new[] { pending.Id }, restored.List().Select(x => x.Id));
    }

    [Fact]
    public void Recent_resolved_proposals_are_kept()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "proposals.json");
        var store = new ProposalStore(() => Now.AddDays(-2), path);
        var p = store.Add("doc", EveryKind());
        store.TryBeginApply(p.Id);
        store.CompleteApply(p.Id, true, "done", new long[] { 5 }, "bryan");

        var restored = new ProposalStore(() => Now, path).Get(p.Id)!;
        Assert.Equal(ProposalStatus.Applied, restored.Status);
        Assert.Equal(new long[] { 5 }, restored.CreatedElementIds);
        Assert.Equal("bryan", restored.ResolvedBy);
    }

    [Fact]
    public void Ids_keep_counting_after_restart()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "proposals.json");
        var store = new ProposalStore(() => Now, path);
        store.Add("doc", EveryKind());
        store.Add("doc", EveryKind());

        var next = new ProposalStore(() => Now, path).Add("doc", EveryKind());
        Assert.StartsWith("P003-", next.Id);
    }

    [Fact]
    public void Corrupt_file_is_set_aside_and_store_starts_empty()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "proposals.json");
        File.WriteAllText(path, "{ broken");

        var store = new ProposalStore(() => Now, path);
        Assert.Empty(store.List());
        Assert.Contains("couldn't be read", store.PersistenceWarning);
        Assert.Single(Directory.GetFiles(dir.Path, "proposals.json.broken-*"));

        store.Add("doc", EveryKind()); // And it can save again.
        Assert.Single(new ProposalStore(() => Now, path).List());
    }

    [Fact]
    public void Clearing_resolved_is_saved_too()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "proposals.json");
        var store = new ProposalStore(() => Now, path);
        var p = store.Add("doc", EveryKind());
        store.Reject(p.Id, "bryan");
        store.ClearResolved();
        Assert.Empty(new ProposalStore(() => Now, path).List());
    }

    [Fact]
    public void No_path_means_memory_only()
    {
        var store = new ProposalStore(() => Now);
        store.Add("doc", EveryKind());
        Assert.Null(store.SavePath);
        Assert.Equal(0, store.RestoredCount);
    }
}
