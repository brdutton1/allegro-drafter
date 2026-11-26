using System.Text.Json;
using AllegroDrafter.Core.Checks;
using AllegroDrafter.Core.Finishing;
using AllegroDrafter.Core.Proposals;
using AllegroDrafter.Core.Standards;

namespace AllegroDrafter.Core.Tests;

/// <summary>Step order, the office auto-apply rule, and the "what changed" report.</summary>
public class FinishReportAndAutoApplyTests
{
    // ------------------------------------------------------------------ order

    [Fact]
    public void Views_get_placed_then_templated_then_schedules_fixed()
    {
        var ids = FinishPlanner.Steps.Select(s => s.Id).ToList();
        // A schedule template replaces the schedule's columns and sorting, so templates go first.
        Assert.True(ids.IndexOf("view-templates") < ids.IndexOf("schedules"));
        // The cover sheet places the sheet index; it must exist before templates are handed out.
        Assert.True(ids.IndexOf("cover-sheet") < ids.IndexOf("view-templates"));
        Assert.Equal("gate", ids[0]);
        Assert.Equal(11, ids.Distinct().Count());
    }

    [Fact]
    public void Run_on_25_0083_order_needs_no_repeat_steps()
    {
        // Index not on the cover, an untemplated schedule and plan, door schedule missing columns.
        var facts = new SetFacts
        {
            Sheets = { new SheetFact("CS", "COVER", 4), new SheetFact("A1", "MAIN FLOOR PLAN", 1) },
            Schedules =
            {
                new ScheduleFact("DOOR SCHEDULE", "doors", false, true, new[] { "Type Mark", "Width" }.Select(f => new ScheduleFieldFact(f, false)).ToList(), new[] { "Type Mark" }, new[] { "CS" }),
                new ScheduleFact("DRAWING INDEX", "sheets", false, true, new List<ScheduleFieldFact>(), Array.Empty<string>(), Array.Empty<string>()),
            },
            PlacedViews = { new ViewFact("MAIN FLOOR PLAN", "FloorPlan", 48, null, new[] { "A1" }) },
            Texts = { new TextFact("DO NOT SCALE DRAWINGS.", "GENERAL NOTES", new[] { "CS" }) },
        };
        var profile = new StandardsProfile { ViewTemplates = { new ViewTemplateInfo("ADC - Arch Floor Plan", "FloorPlan"), new ViewTemplateInfo("ADC - Schedule", "Schedule") } };
        profile.Manual.Audit.ViewTemplateRules.Add(new ViewTemplateRule("FloorPlan", null, "ADC - Arch Floor Plan"));
        var state = new FinishState();

        FinishStep Next() => FinishPlanner.Next(facts, SetAuditor.Run(facts, profile.Manual.Audit, profile.ViewTemplates.Select(t => t.Name).ToList()), profile, state);

        var first = Assert.IsType<FinishBatch>(Next());
        Assert.Equal("cover-sheet", first.StepId);
        Assert.Equal(new PlaceViewChange("CS", "DRAWING INDEX", "Schedule"), first.Request.Changes.Single());
        Assert.True(FinishPlanner.Steps.Select(s => s.Id).ToList().IndexOf(first.StepId) < FinishPlanner.Steps.Select(s => s.Id).ToList().IndexOf("schedules"));
    }

    [Fact]
    public void View_type_with_no_office_template_is_said_once_not_skipped_silently()
    {
        var facts = new SetFacts
        {
            Sheets = { new SheetFact("A6", "ROOF", 1) },
            PlacedViews = { new ViewFact("HOUSE ROOF PLAN", "EngineeringPlan", 48, null, new[] { "A6" }), new ViewFact("ROOF FRAMING PLAN", "EngineeringPlan", 48, null, new[] { "S4" }) },
        };
        var profile = new StandardsProfile { ViewTemplates = { new ViewTemplateInfo("ADC - Arch Floor Plan", "FloorPlan") } };
        profile.Manual.Audit.TemplateRequiredViewTypes = new List<string> { "EngineeringPlan" };
        var state = new FinishState { SkippedSteps = { "cover-sheet" } };
        FinishStep Next() => FinishPlanner.Next(facts, SetAuditor.Run(facts, profile.Manual.Audit, profile.ViewTemplates.Select(t => t.Name).ToList()), profile, state);

        var q = Assert.IsType<FinishQuestions>(Next());
        var card = Assert.Single(q.Cards);
        Assert.Equal("view-templates", card.Step);
        Assert.Contains("HOUSE ROOF PLAN", card.Prompt);
        Assert.Contains("no view template for EngineeringPlan", card.Prompt);
        Assert.Equal(new[] { FinishState.DoneByHand, FinishState.Skip }, card.Options);

        state.Answers[card.Id] = FinishState.DoneByHand;
        Assert.IsNotType<FinishQuestions>(Next());
    }

    // ------------------------------------------------------------- auto-apply

    private static FinishBatch Batch(string step, params ProposalChange[] changes) =>
        new(step, step, 1, new ProposalRequest(step, "", changes));

    private static readonly ProposalChange Mark = new SetTypeMarkChange("doors", "Supa 300: 3080", "3080");

    [Theory]
    [InlineData("gate", true)]
    [InlineData("parameters", true)]
    [InlineData("cover-sheet", false)]
    [InlineData("view-templates", false)]
    [InlineData("schedules", false)]
    [InlineData("tags", false)]
    [InlineData("type-marks", false)]
    [InlineData("tempered-egress", false)]
    [InlineData("rooms", false)]
    [InlineData("door-swing", false)]
    public void Default_rule_applies_the_mechanical_steps_and_stops_for_the_rest(string step, bool auto)
    {
        Assert.Equal(auto, FinishPlanner.WaitReason(Batch(step, Mark), new FinishDraftingSettings()) is null);
        Assert.Equal(auto, FinishPlanner.WaitReason(Batch(step, Mark), null) is null); // no standards loaded: same defaults
    }

    [Fact]
    public void Anything_that_changes_how_a_sheet_looks_waits_even_if_the_office_lists_it()
    {
        var everything = new FinishDraftingSettings { AutoApplySteps = FinishPlanner.Steps.Select(s => s.Id).ToList() };
        // 25-0083: 33 templates by rule moved viewports and turned analytical and reference lines back on.
        Assert.Contains("view template", FinishPlanner.WaitReason(Batch("view-templates", new ApplyViewTemplateChange("MAIN FLOOR PLAN", "ADC - Arch Floor Plan", "FloorPlan")), everything));
        Assert.NotNull(FinishPlanner.WaitReason(Batch("cover-sheet", new PlaceViewChange("CS", "DRAWING INDEX", "Schedule")), everything));
        Assert.NotNull(FinishPlanner.WaitReason(Batch("tags", new TagViewChange("RIGHT ELEVATION", "doors")), everything));
        // Model data only: the office list decides.
        Assert.Null(FinishPlanner.WaitReason(Batch("schedules", new ScheduleSortChange("DOOR SCHEDULE", new[] { "Type Mark" })), everything));
        Assert.Null(FinishPlanner.WaitReason(Batch("door-swing", Mark), everything));
    }

    [Fact]
    public void Detail_sections_are_asked_apart_from_building_sections()
    {
        var facts = new SetFacts
        {
            PlacedViews =
            {
                new ViewFact("Section 1/A4", "Section", 48, null, new[] { "A4" }),
                new ViewFact("DETAIL 1/S1", "Section", 12, null, new[] { "S1" }),
                new ViewFact("TIMBER TRUSS DETAILS", "Section", 12, null, new[] { "S2" }),
            },
        };
        var profile = new StandardsProfile { ViewTemplates = { new ViewTemplateInfo("ADC - Building Section", "Section"), new ViewTemplateInfo("Structural Section", "Section") } };
        var state = new FinishState { SkippedSteps = { "cover-sheet" } };
        var q = Assert.IsType<FinishQuestions>(FinishPlanner.Next(facts, SetAuditor.Run(facts, profile.Manual.Audit, profile.ViewTemplates.Select(t => t.Name).ToList()), profile, state));
        Assert.Equal(new[] { "template:Section:", "template:Section:DETAIL" }, q.Cards.Select(c => c.Id).OrderBy(i => i));
        Assert.Contains("TIMBER TRUSS DETAILS", q.Cards.Single(c => c.Id == "template:Section:DETAIL").Prompt);
    }

    [Fact]
    public void A_batch_that_deletes_always_waits_even_if_the_office_lists_it()
    {
        var settings = new FinishDraftingSettings { AutoApplySteps = { "room-cleanup" } };
        var delete = Batch("room-cleanup", new DeleteRoomsChange(new long[] { 1, 2 }));
        Assert.Equal("it deletes", FinishPlanner.WaitReason(delete, settings));
    }

    [Fact]
    public void Office_can_change_the_list()
    {
        var settings = new FinishDraftingSettings { AutoApplySteps = new List<string> { "DOOR-SWING" } };
        Assert.Null(FinishPlanner.WaitReason(Batch("door-swing", Mark), settings));
        Assert.NotNull(FinishPlanner.WaitReason(Batch("gate", Mark), settings));
        Assert.NotNull(FinishPlanner.WaitReason(Batch("gate", Mark), new FinishDraftingSettings { AutoApplySteps = new() }));
    }

    [Fact]
    public void Standards_without_the_section_get_the_defaults_and_with_it_keep_the_office_list()
    {
        var old = JsonSerializer.Deserialize<StandardsProfile>("""{ "manual": { "rules": [] } }""", Json.Indented)!;
        Assert.Equal(new[] { "gate", "parameters" }, old.Manual.FinishDrafting.AutoApplySteps);
        Assert.Contains("Room Size", old.Manual.FinishDrafting.TagLabelParameters);

        var custom = JsonSerializer.Deserialize<StandardsProfile>("""{ "manual": { "finishDrafting": { "autoApplySteps": ["tags"] } } }""", Json.Indented)!;
        Assert.Equal(new[] { "tags" }, custom.Manual.FinishDrafting.AutoApplySteps);
    }

    // ----------------------------------------------------------------- report

    private static ChangeImpact On(string proposal, string sheet, string where, string what, string? noun = null, params long[] ids) =>
        new(proposal, sheet, sheet == "CS" ? "COVER" : "MAIN FLOOR PLAN", sheet == "CS" ? 9001 : 9002, where, what, ids, noun);

    [Fact]
    public void Report_groups_by_sheet_merges_batches_and_counts_each_element_once()
    {
        var impacts = new[]
        {
            On("P007", "CS", "DOOR SCHEDULE", "Door Swing", "door", 360, 368, 414),
            On("P003", "CS", "DOOR SCHEDULE", "columns added: From Room: Name"),
            On("P009", "CS", "DOOR SCHEDULE", "columns added: From Room: Name"),
            On("P006", "A1", "MAIN FLOOR PLAN", "room tags show Number", "room", 1, 2, 3),
            On("P012", "A1", "MAIN FLOOR PLAN", "room tags show Number", "room", 3, 4),
            new ChangeImpact("P001", null, null, null, "Wall type \"Basic Wall: Glass\"", "Function Interior", Array.Empty<long>()),
        };
        var report = ChangeReport.Build(impacts, _ => ProposalStatus.Applied);

        Assert.Equal(new[] { "CS", "A1" }, report.Sheets.Select(s => s.Number)); // cover first
        var cs = report.Sheets[0];
        Assert.Equal(9001, cs.SheetId);
        Assert.Equal(2, cs.Lines.Count);
        Assert.Equal(new[] { "P003", "P009" }, cs.Lines.Single(l => l.What.StartsWith("columns")).ProposalIds);
        Assert.Equal("DOOR SCHEDULE: Door Swing on 3 doors", cs.Lines.Single(l => l.What == "Door Swing").Text);

        var rooms = Assert.Single(report.Sheets[1].Lines);
        Assert.Equal(new long[] { 1, 2, 3, 4 }, rooms.ElementIds); // room 3 changed twice, counted once
        Assert.Equal("MAIN FLOOR PLAN: room tags show Number on 4 rooms", rooms.Text);

        Assert.Equal("Wall type \"Basic Wall: Glass\": Function Interior", Assert.Single(report.ModelOnly).Text);
        Assert.Equal(6, report.AppliedBatches);
    }

    [Fact]
    public void Undone_and_unknown_batches_drop_out()
    {
        var impacts = new[]
        {
            On("P001", "A1", "MAIN FLOOR PLAN", "tags placed", "tag", 5),
            On("P002", "CS", "DOOR SCHEDULE", "Door Swing", "door", 6),
            On("P003", "CS", "WINDOW SCHEDULE", "Tempered", "window", 7),
        };
        var status = new Dictionary<string, ProposalStatus> { ["P001"] = ProposalStatus.Applied, ["P002"] = ProposalStatus.Undone };
        var report = ChangeReport.Build(impacts, id => status.TryGetValue(id, out var s) ? s : null);

        Assert.Equal(new[] { "A1" }, report.Sheets.Select(s => s.Number));
        Assert.Equal(new[] { "P002" }, report.UndoneProposals);
        Assert.Contains("Undone with Ctrl+Z", report.ToMarkdown("25-0083", new DateTime(2026, 11, 26, 9, 0, 0)));
    }

    [Theory]
    [InlineData(new[] { "A10", "A2", "CS", "A1.1", "A1", "S1", "F1" }, new[] { "CS", "A1", "A1.1", "A2", "A10", "F1", "S1" })]
    public void Sheets_sort_like_a_sheet_index(string[] given, string[] expected)
    {
        Assert.Equal(expected, given.OrderBy(ChangeReport.SortKey, StringComparer.Ordinal));
    }

    [Fact]
    public void Markdown_lists_sheets_model_only_and_what_is_still_open()
    {
        var report = ChangeReport.Build(new[]
        {
            On("P007", "CS", "DOOR SCHEDULE", "Door Swing", "door", 1),
            new ChangeImpact("P002", null, null, null, "Add text instance parameter \"Room Size\" to Rooms", "", Array.Empty<long>()),
        }, _ => ProposalStatus.Applied);
        var md = report.ToMarkdown("25-0083 Javier Cervera Claude test", new DateTime(2026, 11, 26, 21, 5, 0), new[] { "Cover sheet: 1 problem." });

        Assert.Contains("# Finish Drafting: what changed", md);
        Assert.Contains("25-0083 Javier Cervera Claude test · 2026-11-26 9:05 PM · 2 batches applied", md);
        Assert.Contains("## Sheets to check (1)", md);
        Assert.Contains("### CS COVER", md);
        Assert.Contains("- DOOR SCHEDULE: Door Swing on 1 door (P007)", md);
        Assert.Contains("## In the model only", md);
        Assert.Contains("- Add text instance parameter \"Room Size\" to Rooms (P002)", md);
        Assert.Contains("## Still open", md);
        Assert.Contains("- Cover sheet: 1 problem.", md);
    }

    [Fact]
    public void Empty_report_says_so()
    {
        var report = ChangeReport.Build(Array.Empty<ChangeImpact>(), _ => null);
        Assert.True(report.IsEmpty);
        Assert.Contains("Nothing in the model changed.", report.ToMarkdown("doc", DateTime.Now));
    }

    // ----------------------------------------------------------- changes store

    [Fact]
    public void Resume_keeps_adding_to_the_same_report_across_a_restart()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "finish-changes.json");
        var t0 = new DateTime(2026, 11, 26, 4, 58, 0, DateTimeKind.Utc);

        var store = new FinishChangesStore(path);
        Assert.Equal(t0, store.BeginOrResume("25-0083", t0));
        store.Record("25-0083", "P001", new[] { On("P001", "A1", "MAIN FLOOR PLAN", "tags placed", "tag", 5) });

        // Revit closes, comes back, run resumes.
        var after = new FinishChangesStore(path);
        Assert.Equal(t0, after.BeginOrResume("25-0083", t0.AddHours(1)));
        after.Record("25-0083", "P002", new[] { On("P002", "CS", "DOOR SCHEDULE", "Door Swing", "door", 6) });
        var (started, impacts, finished) = after.For("25-0083");
        Assert.Equal(t0, started);
        Assert.Null(finished);
        Assert.Equal(new[] { "P001", "P002" }, impacts.Select(i => i.ProposalId));
        Assert.Equal(new long[] { 5 }, impacts[0].ElementIds);
        Assert.Equal(9002, impacts[0].SheetId);
    }

    [Fact]
    public void A_finished_run_means_the_next_one_starts_clean()
    {
        var store = new FinishChangesStore();
        var t0 = new DateTime(2026, 11, 26, 4, 58, 0, DateTimeKind.Utc);
        store.BeginOrResume("doc", t0);
        store.Record("doc", "P001", new[] { On("P001", "A1", "PLAN", "tags placed", "tag", 5) });
        store.MarkFinished("doc", t0.AddMinutes(5));
        Assert.NotNull(store.For("doc").FinishedUtc);

        Assert.Equal(t0.AddDays(1), store.BeginOrResume("doc", t0.AddDays(1)));
        Assert.Empty(store.For("doc").Impacts);
        Assert.Null(store.For("doc").FinishedUtc);
    }

    [Fact]
    public void Recording_a_batch_again_replaces_it_and_projects_stay_apart()
    {
        var store = new FinishChangesStore();
        store.BeginOrResume("a", DateTime.UtcNow);
        store.Record("a", "P001", new[] { On("P001", "A1", "PLAN", "tags placed", "tag", 5) });
        store.Record("a", "P001", new[] { On("P001", "A1", "PLAN", "tags placed", "tag", 5, 6) });
        Assert.Equal(new long[] { 5, 6 }, Assert.Single(store.For("a").Impacts).ElementIds);
        Assert.Empty(store.For("b").Impacts);
    }

    [Fact]
    public void Corrupt_changes_file_is_set_aside()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "finish-changes.json");
        File.WriteAllText(path, "[ nope");
        var store = new FinishChangesStore(path);
        Assert.Contains("couldn't be read", store.Warning);
        Assert.Single(Directory.GetFiles(dir.Path, "finish-changes.json.broken-*"));
        store.BeginOrResume("doc", DateTime.UtcNow);
        Assert.Null(store.Warning);
    }
}
