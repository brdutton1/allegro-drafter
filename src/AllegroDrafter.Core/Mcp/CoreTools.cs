using System.Text.Json;
using System.Text.Json.Nodes;
using AllegroDrafter.Core.Proposals;
using AllegroDrafter.Core.Standards;

namespace AllegroDrafter.Core.Mcp;

/// <summary>Tools that need no Revit API access. Shared by the add-in and tests.</summary>
public static class CoreTools
{
    public static ToolCallResult GetStandards(StandardsStore store, JsonObject args)
    {
        var profile = store.Current;
        if (profile is null)
        {
            var why = store.LoadError ?? $"No profile at {store.ActivePath}.";
            return ToolCallResult.Error($"{why} In Revit, open the office template and run Allegro > Export Standards, then make it the active profile.");
        }

        var section = Args.OptionalString(args, "section", "arguments") ?? "all";
        var full = JsonSerializer.SerializeToNode(profile, Json.Indented)!.AsObject();
        JsonNode? result = section switch
        {
            "all" => full,
            "summary" => new JsonObject
            {
                ["sourceDocument"] = profile.SourceDocument,
                ["exportedUtc"] = profile.ExportedUtc,
                ["revitVersion"] = profile.RevitVersion,
                ["counts"] = new JsonObject
                {
                    ["titleBlocks"] = profile.TitleBlocks.Count,
                    ["viewTemplates"] = profile.ViewTemplates.Count,
                    ["viewFamilyTypes"] = profile.ViewFamilyTypes.Count,
                    ["tagTypes"] = profile.TagTypes.Count,
                    ["schedules"] = profile.Schedules.Count,
                    ["draftingViews"] = profile.DraftingViews.Count,
                    ["legends"] = profile.Legends.Count,
                    ["sheets"] = profile.Sheets.Count,
                },
                ["manual"] = full["manual"]?.DeepClone(),
            },
            "modelTypes" => new JsonObject
            {
                ["wallTypes"] = full["wallTypes"]?.DeepClone(),
                ["floorTypes"] = full["floorTypes"]?.DeepClone(),
                ["roofTypes"] = full["roofTypes"]?.DeepClone(),
                ["ceilingTypes"] = full["ceilingTypes"]?.DeepClone(),
                ["doorTypes"] = full["doorTypes"]?.DeepClone(),
                ["windowTypes"] = full["windowTypes"]?.DeepClone(),
            },
            _ when full.ContainsKey(section) => new JsonObject { [section] = full[section]!.DeepClone() },
            _ => throw new ToolArgumentException($"Unknown section '{section}'."),
        };

        if (store.LoadError is not null && result is JsonObject obj)
        {
            obj["warning"] = $"The profile file has an error, so this is the last good copy. {store.LoadError}";
        }
        return ToolCallResult.Ok(result!.ToJsonString(Json.Indented));
    }

    public static ToolCallResult ListProposals(ProposalStore store, JsonObject args)
    {
        var status = Args.OptionalString(args, "status", "arguments");
        ProposalStatus? filter = null;
        if (status is not null)
        {
            if (!Enum.TryParse<ProposalStatus>(status, ignoreCase: true, out var parsed))
                throw new ToolArgumentException($"Unknown status '{status}'.");
            filter = parsed;
        }

        var items = store.List()
            .Where(p => filter is null || p.Status == filter)
            .Select(p => new
            {
                id = p.Id,
                title = p.Title,
                status = p.Status,
                document = p.DocumentTitle,
                changes = p.Changes.Count,
                createdUtc = p.CreatedUtc,
                resultMessage = p.ResultMessage,
            })
            .ToList();
        return ToolCallResult.Ok(new { count = items.Count, proposals = items });
    }

    public static ToolCallResult GetProposal(ProposalStore store, JsonObject args)
    {
        var id = Args.RequiredString(args, "id", "arguments");
        var p = store.Get(id);
        if (p is null) return ToolCallResult.Error($"No proposal with id '{id}' in this Revit session.");
        return ToolCallResult.Ok(Describe(p));
    }

    public static object Describe(Proposal p) => new
    {
        id = p.Id,
        title = p.Title,
        rationale = p.Rationale,
        status = p.Status,
        document = p.DocumentTitle,
        createdUtc = p.CreatedUtc,
        resolvedUtc = p.ResolvedUtc,
        resolvedBy = p.ResolvedBy,
        attempts = p.Attempts,
        resultMessage = p.ResultMessage,
        createdElementIds = p.CreatedElementIds,
        changes = p.Changes.Select(c => c.Describe()).ToList(),
    };
}
