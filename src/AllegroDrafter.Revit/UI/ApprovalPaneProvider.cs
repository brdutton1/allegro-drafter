using Autodesk.Revit.UI;

namespace AllegroDrafter.Revit.UI;

internal sealed class ApprovalPaneProvider : IDockablePaneProvider
{
    private readonly ApprovalPane _pane;

    public ApprovalPaneProvider(ApprovalPane pane) => _pane = pane;

    public void SetupDockablePane(DockablePaneProviderData data)
    {
        data.FrameworkElement = _pane;
        data.InitialState = new DockablePaneState
        {
            DockPosition = DockPosition.Right,
        };
        data.VisibleByDefault = true;
    }
}
