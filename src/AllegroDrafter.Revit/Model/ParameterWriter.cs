using AllegroDrafter.Core;
using Autodesk.Revit.DB;

namespace AllegroDrafter.Revit.Model;

/// <summary>
/// Finds, checks and writes instance parameters by name. The same checks run at
/// propose time and again right before writing, so the designer never approves
/// something Revit would reject.
/// </summary>
internal static class ParameterWriter
{
    public static readonly IReadOnlyDictionary<string, BuiltInCategory> Categories = new Dictionary<string, BuiltInCategory>(StringComparer.OrdinalIgnoreCase)
    {
        ["doors"] = BuiltInCategory.OST_Doors,
        ["windows"] = BuiltInCategory.OST_Windows,
        ["rooms"] = BuiltInCategory.OST_Rooms,
    };

    public static bool IsEditableCategory(Element element) =>
        element.Category is { } c && Categories.Values.Contains(c.BuiltInCategory);

    /// <summary>Instance parameter by exact name, or null with a reason the AI can act on.</summary>
    public static Parameter? Find(Element element, string name, out string? error)
    {
        error = null;
        var parameter = element.LookupParameter(name);
        if (parameter is not null) return parameter;

        if (element.Document.GetElement(element.GetTypeId())?.LookupParameter(name) is not null)
        {
            error = $"\"{name}\" is a type parameter. Changing it would change every element of that type, so it isn't allowed here. Swap the type in Revit instead.";
            return null;
        }

        var available = element.Parameters.Cast<Parameter>()
            .Where(p => !p.IsReadOnly)
            .Select(p => p.Definition.Name)
            .Distinct()
            .ToList();
        error = $"No parameter named \"{name}\" on this element.";
        var closest = Names.ClosestMatch(name, available);
        if (closest is not null) error += $" Did you mean \"{closest}\"?";
        error += " Use list_elements with includeParameterList to see the exact names.";
        return null;
    }

    /// <summary>Null if the value can be written, otherwise why not.</summary>
    public static string? Check(Element element, string name, string value)
    {
        var parameter = Find(element, name, out var error);
        if (parameter is null) return error;
        if (parameter.IsReadOnly) return $"\"{name}\" is read-only (Revit calculates it).";

        switch (parameter.StorageType)
        {
            case StorageType.String:
                return null;
            case StorageType.Integer when IsYesNo(parameter):
                return TryParseYesNo(value, out _) ? null : $"\"{name}\" is Yes/No. Use Yes or No, not \"{value}\".";
            case StorageType.Integer:
                return int.TryParse(value, out _) ? null : $"\"{name}\" needs a whole number, not \"{value}\".";
            case StorageType.Double:
                return UnitFormatUtils.TryParse(element.Document.GetUnits(), parameter.Definition.GetDataType(), value, out _)
                    ? null
                    : $"\"{name}\" needs a value in project units (e.g. 3' - 0\"), not \"{value}\".";
            default:
                return $"\"{name}\" points at another element (or has no value type). Setting it isn't supported.";
        }
    }

    public static void Write(Element element, string name, string value)
    {
        var parameter = Find(element, name, out var error) ?? throw new InvalidOperationException(error);
        if (parameter.IsReadOnly) throw new InvalidOperationException($"\"{name}\" is read-only.");

        var ok = parameter.StorageType switch
        {
            StorageType.String => parameter.Set(value),
            StorageType.Integer when IsYesNo(parameter) => TryParseYesNo(value, out var b) && parameter.Set(b ? 1 : 0),
            StorageType.Integer => int.TryParse(value, out var i) && parameter.Set(i),
            StorageType.Double => UnitFormatUtils.TryParse(element.Document.GetUnits(), parameter.Definition.GetDataType(), value, out var d) && parameter.Set(d),
            _ => false,
        };
        if (!ok) throw new InvalidOperationException($"Revit refused {name} = \"{value}\" on {RevitLookup.ElementLabel(element)}.");
    }

    /// <summary>Text for display. Shows what the designer sees in Revit.</summary>
    public static string? Read(Parameter? parameter)
    {
        if (parameter is null || !parameter.HasValue) return null;
        return parameter.StorageType == StorageType.String ? parameter.AsString() : parameter.AsValueString();
    }

    private static bool IsYesNo(Parameter parameter) => parameter.Definition.GetDataType() == SpecTypeId.Boolean.YesNo;

    private static bool TryParseYesNo(string value, out bool result)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "yes" or "true" or "1" or "y":
                result = true;
                return true;
            case "no" or "false" or "0" or "n":
                result = false;
                return true;
            default:
                result = false;
                return false;
        }
    }
}
