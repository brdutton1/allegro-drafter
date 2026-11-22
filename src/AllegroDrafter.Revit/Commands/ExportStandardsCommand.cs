using AllegroDrafter.Revit.Model;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AllegroDrafter.Revit.Commands;

[Transaction(TransactionMode.ReadOnly)]
public sealed class ExportStandardsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var doc = commandData.Application.ActiveUIDocument?.Document;
        if (doc is null || doc.IsFamilyDocument)
        {
            TaskDialog.Show("Allegro Drafter", "Open the office template (or a project) first. Family files can't be exported.");
            return Result.Cancelled;
        }

        var app = App.Instance;
        var profile = StandardsExporter.Export(doc, commandData.Application.Application.VersionNumber);
        string savedPath;
        try
        {
            savedPath = app.Standards.SaveExport(profile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            message = $"Could not save the export: {ex.Message}";
            return Result.Failed;
        }

        var current = app.Standards.Current;
        var dialog = new TaskDialog("Allegro Drafter: Export Standards")
        {
            MainInstruction = $"Exported standards from \"{doc.Title}\"",
            MainContent =
                $"{profile.TitleBlocks.Count} title blocks, {profile.ViewTemplates.Count} view templates, " +
                $"{profile.Schedules.Count} schedules, {profile.DraftingViews.Count} drafting views, " +
                $"{profile.Legends.Count} legends, {profile.TagTypes.Count} tag types, {profile.Sheets.Count} sheets.\n\n" +
                $"Saved a copy to:\n{savedPath}\n\n" +
                (current is null
                    ? "There is no active office profile yet. Make this the one the AI must follow?"
                    : $"The active profile came from \"{current.SourceDocument}\" ({current.ExportedUtc.ToLocalTime():g}). Replace it with this export? Your hand-edited rules and sheet indexes are kept."),
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            DefaultButton = current is null ? TaskDialogResult.Yes : TaskDialogResult.No,
            FooterText = "Only make the office template the active profile, not a single job.",
        };

        if (dialog.Show() == TaskDialogResult.Yes)
        {
            try
            {
                app.Standards.SetActive(profile);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                message = $"Could not write the active profile: {ex.Message}";
                return Result.Failed;
            }
            TaskDialog.Show("Allegro Drafter", $"Active office standards updated:\n{app.Standards.ActivePath}");
        }
        return Result.Succeeded;
    }
}
