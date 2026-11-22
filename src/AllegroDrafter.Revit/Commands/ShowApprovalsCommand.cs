using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AllegroDrafter.Revit.Commands;

[Transaction(TransactionMode.ReadOnly)]
public sealed class ShowApprovalsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        commandData.Application.GetDockablePane(App.PaneId)?.Show();
        return Result.Succeeded;
    }
}
