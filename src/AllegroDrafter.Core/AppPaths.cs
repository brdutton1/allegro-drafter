namespace AllegroDrafter.Core;

/// <summary>
/// Every file Allegro Drafter writes lives under one root folder
/// (default: %APPDATA%\AllegroDrafter). Tests pass their own root.
/// </summary>
public sealed class AppPaths
{
    public AppPaths(string? root = null)
    {
        Root = string.IsNullOrWhiteSpace(root)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AllegroDrafter")
            : root;
    }

    public string Root { get; }

    /// <summary>Port and access token the Revit add-in listens with. Read by the bridge.</summary>
    public string McpConfigFile => Path.Combine(Root, "mcp.json");

    /// <summary>The office standards profile every proposal is checked against.</summary>
    public string ActiveStandardsFile => Path.Combine(Root, "standards.json");

    /// <summary>Timestamped exports, kept so you can diff template drift.</summary>
    public string StandardsExportDir => Path.Combine(Root, "standards-exports");

    /// <summary>Proposals (pending and recent), saved after every change so a crash loses nothing.</summary>
    public string ProposalsFile => Path.Combine(Root, "proposals.json");

    /// <summary>Finish Drafting card answers, per project, so a restart doesn't ask again.</summary>
    public string FinishAnswersFile => Path.Combine(Root, "finish-answers.json");

    /// <summary>What the current Finish Drafting run has changed, per project, for the change report.</summary>
    public string FinishChangesFile => Path.Combine(Root, "finish-changes.json");

    /// <summary>Finished change reports, one markdown file per run.</summary>
    public string ReportsDir => Path.Combine(Root, "reports");

    /// <summary>One JSON-lines file per month recording every approve/reject.</summary>
    public string AuditDir => Path.Combine(Root, "audit");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(StandardsExportDir);
        Directory.CreateDirectory(AuditDir);
    }
}
