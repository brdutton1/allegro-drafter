using AllegroDrafter.Core.Standards;
using Autodesk.Revit.DB;

namespace AllegroDrafter.Revit.Model;

/// <summary>
/// Rough auto-arrange for views placed without coordinates: left to right,
/// top to bottom inside the title block's drawing area. A starting point, not
/// a finished layout. The designer drags them where they belong.
/// </summary>
internal static class SheetLayout
{
    private const double GapFeet = 0.5 / 12.0;

    public sealed record Placed(ElementId ElementId, bool IsSchedule);

    /// <summary>Returns how many items didn't fit inside the drawing area.</summary>
    public static int Arrange(Document doc, ViewSheet sheet, IReadOnlyList<Placed> items, SheetInsets insets)
    {
        if (items.Count == 0) return 0;

        var area = DrawingArea(doc, sheet, insets);
        var cursorX = area.MinX;
        var cursorY = area.MaxY;
        var rowHeight = 0.0;
        var overflow = 0;

        foreach (var item in items)
        {
            var element = doc.GetElement(item.ElementId);
            var box = element?.get_BoundingBox(sheet);
            if (element is null || box is null) continue;

            var width = box.Max.X - box.Min.X;
            var height = box.Max.Y - box.Min.Y;

            if (cursorX > area.MinX && cursorX + width > area.MaxX)
            {
                cursorX = area.MinX;
                cursorY -= rowHeight + GapFeet;
                rowHeight = 0;
            }

            var target = new XYZ(cursorX + width / 2, cursorY - height / 2, 0);
            if (element is Viewport viewport)
            {
                viewport.SetBoxCenter(target);
            }
            else
            {
                var current = (box.Min + box.Max) / 2;
                ElementTransformUtils.MoveElement(doc, element.Id, new XYZ(target.X - current.X, target.Y - current.Y, 0));
            }

            if (cursorY - height < area.MinY || width > area.MaxX - area.MinX) overflow++;
            cursorX += width + GapFeet;
            rowHeight = Math.Max(rowHeight, height);
        }
        return overflow;
    }

    private readonly record struct Area(double MinX, double MinY, double MaxX, double MaxY);

    private static Area DrawingArea(Document doc, ViewSheet sheet, SheetInsets insets)
    {
        var titleBlock = new FilteredElementCollector(doc, sheet.Id)
            .OfCategory(BuiltInCategory.OST_TitleBlocks)
            .WhereElementIsNotElementType()
            .FirstElement();
        var box = titleBlock?.get_BoundingBox(sheet);

        double minX, minY, maxX, maxY;
        if (box is not null)
        {
            (minX, minY, maxX, maxY) = (box.Min.X, box.Min.Y, box.Max.X, box.Max.Y);
        }
        else
        {
            var outline = sheet.Outline;
            (minX, minY, maxX, maxY) = (outline.Min.U, outline.Min.V, outline.Max.U, outline.Max.V);
        }

        var area = new Area(
            minX + insets.Left / 12.0,
            minY + insets.Bottom / 12.0,
            maxX - insets.Right / 12.0,
            maxY - insets.Top / 12.0);

        // Bad insets (bigger than the sheet) fall back to the full title block.
        return area.MaxX > area.MinX && area.MaxY > area.MinY ? area : new Area(minX, minY, maxX, maxY);
    }
}
