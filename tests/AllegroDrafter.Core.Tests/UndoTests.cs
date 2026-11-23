using AllegroDrafter.Core.Proposals;

namespace AllegroDrafter.Core.Tests;

public class UndoTests
{
    private static ProposalRequest Req(string title = "Batch") => new(title, "why", new ProposalChange[] { new CreateSheetChange("A1", "n", "tb") });

    private static Proposal Applied(ProposalStore store)
    {
        var p = store.Add("doc", Req());
        store.TryBeginApply(p.Id);
        store.CompleteApply(p.Id, true, "1 sheet(s) created.", new long[] { 5 }, "bryan");
        return p;
    }

    [Fact]
    public void Undo_then_redo_round_trips_the_status_and_message()
    {
        var store = new ProposalStore();
        var p = Applied(store);
        var raised = 0;
        store.Changed += (_, _) => raised++;

        Assert.True(store.MarkUndone(p.Id));
        Assert.Equal(ProposalStatus.Undone, p.Status);
        Assert.StartsWith("Undone in Revit", p.ResultMessage);
        Assert.True(p.IsResolved);
        Assert.False(p.CanApprove);

        Assert.True(store.MarkRedone(p.Id));
        Assert.Equal(ProposalStatus.Applied, p.Status);
        Assert.Equal("1 sheet(s) created.", p.ResultMessage);
        Assert.Equal(2, raised);
    }

    [Fact]
    public void Only_applied_proposals_can_be_undone()
    {
        var store = new ProposalStore();
        var pending = store.Add("doc", Req());
        Assert.False(store.MarkUndone(pending.Id));
        Assert.False(store.MarkRedone(pending.Id));
        Assert.False(store.MarkUndone("P999-ffff"));

        var p = Applied(store);
        Assert.True(store.MarkUndone(p.Id));
        Assert.False(store.MarkUndone(p.Id)); // already undone
        Assert.Equal(ProposalStatus.Pending, pending.Status);
    }

    [Fact]
    public void Clear_resolved_removes_undone_proposals()
    {
        var store = new ProposalStore();
        var p = Applied(store);
        store.MarkUndone(p.Id);
        Assert.Equal(1, store.ClearResolved());
    }

    [Theory]
    [InlineData("Door Type Marks")]
    [InlineData("A very long proposal title that goes on and on past what fits in Revit's undo list, really")]
    [InlineData("")]
    public void Transaction_names_carry_the_id_and_fit(string title)
    {
        var name = UndoNames.Format("P003-ab12", title);
        Assert.StartsWith("Allegro Drafter [P003-ab12]", name);
        Assert.True(name.Length <= UndoNames.MaxLength);
        Assert.Equal("P003-ab12", UndoNames.ParseId(name));
    }

    [Fact]
    public void Long_titles_are_cut_with_an_ellipsis_never_through_the_id()
    {
        var name = UndoNames.Format("P1234-0a9f", new string('x', 200));
        Assert.EndsWith("...", name);
        Assert.Equal("P1234-0a9f", UndoNames.ParseId(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Wall")]
    [InlineData("Allegro Drafter: old-style name")] // proposals applied before this change
    [InlineData("Something Allegro Drafter [P003-ab12]: not at the start")]
    public void Other_transactions_are_ignored(string? name) => Assert.Null(UndoNames.ParseId(name));

    [Fact]
    public void Store_ids_parse_back()
    {
        var store = new ProposalStore();
        var p = store.Add("doc", Req("Sheets"));
        Assert.Equal(p.Id, UndoNames.ParseId(UndoNames.Format(p.Id, p.Title)));
    }
}
