using AllegroDrafter.Core.Standards;
using Autodesk.Revit.DB;

namespace AllegroDrafter.Revit.Model;

/// <summary>
/// Reads the office template (or any project) and lists every name the AI may use.
/// Run it on the office template, then on a few finished jobs to compare.
/// </summary>
internal static class StandardsExporter
{
    private static readonly BuiltInCategory[] TagCategories =
    {
        BuiltInCategory.OST_DoorTags,
        BuiltInCategory.OST_WindowTags,
        BuiltInCategory.OST_RoomTags,
        BuiltInCategory.OST_WallTags,
        BuiltInCategory.OST_AreaTags,
        BuiltInCategory.OST_FloorTags,
        BuiltInCategory.OST_CeilingTags,
        BuiltInCategory.OST_RoofTags,
        BuiltInCategory.OST_CaseworkTags,
        BuiltInCategory.OST_PlumbingFixtureTags,
        BuiltInCategory.OST_StructuralFramingTags,
        BuiltInCategory.OST_GenericModelTags,
        BuiltInCategory.OST_KeynoteTags,
        BuiltInCategory.OST_MultiCategoryTags,
        BuiltInCategory.OST_MaterialTags,
    };

    public static StandardsProfile Export(Document doc, string revitVersion)
    {
        var views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().ToList();
        var nonTemplates = views.Where(v => !v.IsTemplate).ToList();

        return new StandardsProfile
        {
            SourceDocument = doc.Title,
            ExportedUtc = DateTime.UtcNow,
            RevitVersion = revitVersion,
            TitleBlocks = SymbolNames(doc, BuiltInCategory.OST_TitleBlocks),
            ViewTemplates = views.Where(v => v.IsTemplate)
                .Select(v => new ViewTemplateInfo(v.Name, v.ViewType.ToString()))
                .OrderBy(t => t.ViewType).ThenBy(t => t.Name)
                .ToList(),
            ViewFamilyTypes = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .Select(t => new ViewFamilyTypeInfo(t.Name, t.ViewFamily.ToString()))
                .OrderBy(t => t.ViewFamily).ThenBy(t => t.Name)
                .ToList(),
            TagTypes = TagCategories.SelectMany(c => TagTypes(doc, c)).ToList(),
            TextNoteTypes = TypeNames<TextNoteType>(doc),
            DimensionTypes = new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>()
                .Where(t => !string.IsNullOrWhiteSpace(t.Name))
                .Select(t => $"{t.StyleType}: {t.Name}")
                .Distinct().OrderBy(n => n).ToList(),
            Schedules = nonTemplates.OfType<ViewSchedule>()
                .Where(s => !s.IsTitleblockRevisionSchedule && !s.IsInternalKeynoteSchedule)
                .Select(s => s.Name).Distinct().OrderBy(n => n).ToList(),
            DraftingViews = NamesOf(nonTemplates, ViewType.DraftingView),
            Legends = NamesOf(nonTemplates, ViewType.Legend),
            Sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .OrderBy(s => s.SheetNumber, StringComparer.OrdinalIgnoreCase)
                .Select(s => new SheetInfo(s.SheetNumber, s.Name))
                .ToList(),
            WallTypes = TypeNames<WallType>(doc, familyQualified: true),
            FloorTypes = TypeNames<FloorType>(doc, familyQualified: true),
            RoofTypes = TypeNames<RoofType>(doc, familyQualified: true),
            CeilingTypes = TypeNames<CeilingType>(doc, familyQualified: true),
            DoorTypes = SymbolNames(doc, BuiltInCategory.OST_Doors),
            WindowTypes = SymbolNames(doc, BuiltInCategory.OST_Windows),
            KeynoteFile = KeynotePath(doc),
        };
    }

    private static List<string> SymbolNames(Document doc, BuiltInCategory category) =>
        new FilteredElementCollector(doc).OfCategory(category).WhereElementIsElementType()
            .OfType<FamilySymbol>()
            .Select(RevitLookup.TypeName)
            .Distinct().OrderBy(n => n).ToList();

    private static IEnumerable<TagTypeInfo> TagTypes(Document doc, BuiltInCategory category)
    {
        string label;
        try
        {
            label = Category.GetCategory(doc, category)?.Name ?? category.ToString();
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            label = category.ToString();
        }
        return SymbolNames(doc, category).Select(n => new TagTypeInfo(label, n));
    }

    private static List<string> TypeNames<T>(Document doc, bool familyQualified = false) where T : ElementType =>
        new FilteredElementCollector(doc).OfClass(typeof(T)).Cast<T>()
            .Select(t => familyQualified ? RevitLookup.TypeName(t) : t.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct().OrderBy(n => n).ToList();

    private static List<string> NamesOf(IEnumerable<View> views, ViewType type) =>
        views.Where(v => v.ViewType == type).Select(v => v.Name).Distinct().OrderBy(n => n).ToList();

    private static string? KeynotePath(Document doc)
    {
        try
        {
            var reference = KeynoteTable.GetKeynoteTable(doc)?.GetExternalFileReference();
            var path = reference?.GetAbsolutePath();
            return path is null ? null : ModelPathUtils.ConvertModelPathToUserVisiblePath(path);
        }
        catch (Exception)
        {
            // Keynote file missing or stored as a cloud resource. Not fatal for an export.
            return null;
        }
    }
}
