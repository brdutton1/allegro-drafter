using System.Text.Json;
using System.Text.Json.Nodes;
using AllegroDrafter.Core.Audit;
using AllegroDrafter.Core.Mcp;
using AllegroDrafter.Core.Proposals;
using AllegroDrafter.Core.Standards;
using static AllegroDrafter.Core.Tests.Fixtures;

namespace AllegroDrafter.Core.Tests;

public class StandardsStoreTests
{
    [Fact]
    public void No_file_means_no_profile()
    {
        using var dir = new TempDir();
        var store = new StandardsStore(dir.AppPaths);
        Assert.Null(store.Current);
        Assert.Null(store.LoadError);
    }

    [Fact]
    public void SetActive_round_trips()
    {
        using var dir = new TempDir();
        var store = new StandardsStore(dir.AppPaths);
        store.SetActive(Profile());
        var loaded = store.Current!;
        Assert.Equal("Allegro Residential Template", loaded.SourceDocument);
        Assert.Equal(2, loaded.TitleBlocks.Count);
        Assert.Equal("FloorPlan", loaded.ViewTemplates[0].ViewType);
    }

    [Fact]
    public void Reexport_keeps_hand_edited_manual_section()
    {
        using var dir = new TempDir();
        var store = new StandardsStore(dir.AppPaths);
        var first = Profile();
        first.Manual.Rules.Add("Use 1/4\" plans for homes under 3,000 sf.");
        first.Manual.SheetIndexByProjectType["new-home"] = new() { new SheetInfo("A1.0", "MAIN FLOOR PLAN") };
        store.SetActive(first);

        var reexport = Profile();
        reexport.Manual = new ManualSettings(); // Export never fills this in.
        reexport.TitleBlocks.Add("Allegro 30x42: Large");
        store.SetActive(reexport);

        var loaded = store.Current!;
        Assert.Equal(3, loaded.TitleBlocks.Count);
        Assert.Contains("Use 1/4\" plans for homes under 3,000 sf.", loaded.Manual.Rules);
        Assert.Single(loaded.Manual.SheetIndexByProjectType["new-home"]);
    }

    [Fact]
    public void Hand_edit_is_picked_up_without_restart()
    {
        using var dir = new TempDir();
        var store = new StandardsStore(dir.AppPaths);
        store.SetActive(Profile());
        Assert.Equal(2, store.Current!.TitleBlocks.Count);

        var edited = Profile();
        edited.TitleBlocks.Add("Allegro 30x42: Large");
        File.WriteAllText(dir.AppPaths.ActiveStandardsFile, JsonSerializer.Serialize(edited, Json.Indented));
        File.SetLastWriteTimeUtc(dir.AppPaths.ActiveStandardsFile, DateTime.UtcNow.AddSeconds(5));

        Assert.Equal(3, store.Current!.TitleBlocks.Count);
    }

    [Fact]
    public void Broken_hand_edit_keeps_last_good_profile_and_reports_error()
    {
        using var dir = new TempDir();
        var store = new StandardsStore(dir.AppPaths);
        store.SetActive(Profile());
        Assert.NotNull(store.Current);

        File.WriteAllText(dir.AppPaths.ActiveStandardsFile, "{ not json");
        File.SetLastWriteTimeUtc(dir.AppPaths.ActiveStandardsFile, DateTime.UtcNow.AddSeconds(5));

        Assert.NotNull(store.Current);
        Assert.NotNull(store.LoadError);
    }

    [Fact]
    public void Broken_active_file_is_backed_up_on_reexport()
    {
        using var dir = new TempDir();
        dir.AppPaths.EnsureCreated();
        File.WriteAllText(dir.AppPaths.ActiveStandardsFile, "{ not json");
        new StandardsStore(dir.AppPaths).SetActive(Profile());
        Assert.Single(Directory.GetFiles(dir.Path, "standards.json.broken-*"));
    }

    [Fact]
    public void Newer_schema_is_refused()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "p.json");
        File.WriteAllText(path, """{ "schemaVersion": 99 }""");
        Assert.Throws<InvalidDataException>(() => StandardsStore.Load(path));
    }

    [Fact]
    public void SaveExport_writes_timestamped_copy()
    {
        using var dir = new TempDir();
        var path = new StandardsStore(dir.AppPaths).SaveExport(Profile());
        Assert.True(File.Exists(path));
        Assert.Contains("Allegro Residential Template-20261101-120000", path);
    }

    [Fact]
    public void GetStandards_tool_sections()
    {
        using var dir = new TempDir();
        var store = new StandardsStore(dir.AppPaths);
        Assert.True(CoreTools.GetStandards(store, new JsonObject()).IsError);

        store.SetActive(Profile());
        var all = JsonNode.Parse(CoreTools.GetStandards(store, new JsonObject()).Text)!;
        Assert.Equal(2, all["titleBlocks"]!.AsArray().Count);

        var summary = JsonNode.Parse(CoreTools.GetStandards(store, Obj("""{ "section": "summary" }""")).Text)!;
        Assert.Equal(2, summary["counts"]!["viewTemplates"]!.GetValue<int>());
        Assert.Equal("Architectural sheets start at A1.0.", summary["manual"]!["rules"]![0]!.GetValue<string>());

        var titleBlocks = JsonNode.Parse(CoreTools.GetStandards(store, Obj("""{ "section": "titleBlocks" }""")).Text)!;
        Assert.Null(titleBlocks["viewTemplates"]);
        Assert.NotNull(titleBlocks["titleBlocks"]);

        var modelTypes = JsonNode.Parse(CoreTools.GetStandards(store, Obj("""{ "section": "modelTypes" }""")).Text)!;
        Assert.NotNull(modelTypes["wallTypes"]);

        Assert.Throws<ToolArgumentException>(() => CoreTools.GetStandards(store, Obj("""{ "section": "bogus" }""")));
    }
}

public class AuditLogTests
{
    [Fact]
    public void Writes_one_json_line_per_entry_in_monthly_file()
    {
        using var dir = new TempDir();
        var log = new AuditLog(dir.AppPaths);
        var store = new ProposalStore(() => new DateTime(2026, 11, 22, 0, 0, 0, DateTimeKind.Utc));
        var p = store.Add("Smith House", new ProposalRequest("Sheets", "why", new ProposalChange[] { new CreateSheetChange("A1", "PLAN", "tb") }));
        store.Reject(p.Id, "bryan");

        Assert.True(log.Write(AuditEntry.From(p, "bryan")));
        Assert.True(log.Write(AuditEntry.From(p, "bryan")));

        var file = Path.Combine(dir.AppPaths.AuditDir, "2026-11.jsonl");
        var lines = File.ReadAllLines(file);
        Assert.Equal(2, lines.Length);
        var entry = JsonNode.Parse(lines[0])!;
        Assert.Equal("Rejected", entry["outcome"]!.GetValue<string>());
        Assert.Equal("bryan", entry["user"]!.GetValue<string>());
        Assert.Contains("Create sheet A1", entry["changes"]![0]!.GetValue<string>());
    }
}

public class ProposalToolsTests
{
    [Fact]
    public void List_and_get_proposals()
    {
        var store = new ProposalStore();
        var p = store.Add("doc", new ProposalRequest("Sheets", "why", new ProposalChange[] { new CreateSheetChange("A1", "n", "tb") }));
        store.Add("doc", new ProposalRequest("Other", "", new ProposalChange[] { new CreateSheetChange("A2", "n", "tb") }));
        store.Reject(p.Id, "u");

        var pending = JsonNode.Parse(CoreTools.ListProposals(store, Obj("""{ "status": "pending" }""")).Text)!;
        Assert.Equal(1, pending["count"]!.GetValue<int>());

        var one = JsonNode.Parse(CoreTools.GetProposal(store, Obj($$"""{ "id": "{{p.Id}}" }""")).Text)!;
        Assert.Equal("rejected", one["status"]!.GetValue<string>());

        Assert.True(CoreTools.GetProposal(store, Obj("""{ "id": "P999" }""")).IsError);
        Assert.Throws<ToolArgumentException>(() => CoreTools.ListProposals(store, Obj("""{ "status": "maybe" }""")));
    }
}
