using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AllegroDrafter.Revit.Commands;

/// <summary>Allegro > Finish Drafting: opens the pane and starts (or resumes) the run.</summary>
[Transaction(TransactionMode.ReadOnly)]
public sealed class FinishDraftingCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var doc = commandData.Application.ActiveUIDocument?.Document;
        if (doc is null || doc.IsFamilyDocument)
        {
            message = "Open the project first. Finish Drafting works on a project, not a family.";
            return Result.Failed;
        }
        commandData.Application.GetDockablePane(App.PaneId)?.Show();
        App.Instance.Finish.Start();
        return Result.Succeeded;
    }
}
