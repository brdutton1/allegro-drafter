using AllegroDrafter.Core.Proposals;

namespace AllegroDrafter.Core.Tests;

public class ProposalStoreTests
{
    private static ProposalRequest Req() => new("Batch", "why", new ProposalChange[] { new CreateSheetChange("A1", "n", "tb") });

    [Fact]
    public void New_proposal_is_pending_with_unique_id()
    {
        var store = new ProposalStore();
        var a = store.Add("Smith House", Req());
        var b = store.Add("Smith House", Req());
        Assert.Equal(ProposalStatus.Pending, a.Status);
        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(2, store.PendingCount);
        Assert.Same(a, store.Get(a.Id.ToLowerInvariant()));
    }

    [Fact]
    public void Double_click_approve_only_applies_once()
    {
        var store = new ProposalStore();
        var p = store.Add("doc", Req());
        Assert.True(store.TryBeginApply(p.Id));
        Assert.False(store.TryBeginApply(p.Id));
        Assert.Equal(1, p.Attempts);
    }

    [Fact]
    public void Concurrent_approvals_only_one_wins()
    {
        var store = new ProposalStore();
        var p = store.Add("doc", Req());
        var wins = 0;
        Parallel.For(0, 64, _ =>
        {
            if (store.TryBeginApply(p.Id)) Interlocked.Increment(ref wins);
        });
        Assert.Equal(1, wins);
    }

    [Fact]
    public void Applied_proposal_cannot_be_rejected_or_reapplied()
    {
        var store = new ProposalStore();
        var p = store.Add("doc", Req());
        store.TryBeginApply(p.Id);
        store.CompleteApply(p.Id, true, "Created 1 sheet.", new long[] { 42 }, "bryan");
        Assert.Equal(ProposalStatus.Applied, p.Status);
        Assert.Equal(new long[] { 42 }, p.CreatedElementIds);
        Assert.Equal("bryan", p.ResolvedBy);
        Assert.False(store.Reject(p.Id, "bryan"));
        Assert.False(store.TryBeginApply(p.Id));
    }

    [Fact]
    public void Failed_proposal_can_be_retried_and_keeps_no_element_ids()
    {
        var store = new ProposalStore();
        var p = store.Add("doc", Req());
        store.TryBeginApply(p.Id);
        store.CompleteApply(p.Id, false, "Sheet A1 already exists.", new long[] { 1 }, "bryan");
        Assert.Equal(ProposalStatus.Failed, p.Status);
        Assert.Empty(p.CreatedElementIds);
        Assert.True(store.TryBeginApply(p.Id));
        Assert.Equal(2, p.Attempts);
    }

    [Fact]
    public void Reject_while_applying_is_refused()
    {
        var store = new ProposalStore();
        var p = store.Add("doc", Req());
        store.TryBeginApply(p.Id);
        Assert.False(store.Reject(p.Id, "bryan"));
    }

    [Fact]
    public void Complete_without_begin_throws()
    {
        var store = new ProposalStore();
        var p = store.Add("doc", Req());
        Assert.Throws<InvalidOperationException>(() => store.CompleteApply(p.Id, true, "", Array.Empty<long>(), "u"));
    }

    [Fact]
    public void Unknown_ids_are_safe()
    {
        var store = new ProposalStore();
        Assert.False(store.TryBeginApply("nope"));
        Assert.False(store.Reject("nope", "u"));
        Assert.Null(store.Get("nope"));
    }

    [Fact]
    public void ClearResolved_keeps_pending_and_failed()
    {
        var store = new ProposalStore();
        var pending = store.Add("doc", Req());
        var rejected = store.Add("doc", Req());
        var failed = store.Add("doc", Req());
        store.Reject(rejected.Id, "u");
        store.TryBeginApply(failed.Id);
        store.CompleteApply(failed.Id, false, "x", Array.Empty<long>(), "u");

        Assert.Equal(1, store.ClearResolved());
        Assert.Equal(new[] { pending.Id, failed.Id }, store.List().Select(p => p.Id));
    }

    [Fact]
    public void Changed_event_fires_on_every_transition()
    {
        var store = new ProposalStore();
        var count = 0;
        store.Changed += (_, _) => count++;
        var p = store.Add("doc", Req());
        store.TryBeginApply(p.Id);
        store.CompleteApply(p.Id, true, "ok", Array.Empty<long>(), "u");
        Assert.Equal(3, count);
    }
}
