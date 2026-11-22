using AllegroDrafter.Core.Proposals;
using AllegroDrafter.Core.Standards;
using Autodesk.Revit.DB;

namespace AllegroDrafter.Revit.Model;

internal sealed record ApplyResult(bool Success, string Message, IReadOnlyList<long> CreatedElementIds)
{
    public static ApplyResult Fail(string message) => new(false, message, Array.Empty<long>());
}

/// <summary>
/// Writes an approved proposal to the model as ONE named transaction.
/// All or nothing: any failure rolls the whole batch back. Ctrl+Z undoes it.
/// </summary>
internal static class ProposalApplier
{
    public static ApplyResult Apply(Document doc, Proposal proposal, StandardsProfile? profile)
    {
        if (doc.IsFamilyDocument) return ApplyResult.Fail("The active document is a family. Switch to the project and approve again.");
        if (!string.Equals(doc.Title, proposal.DocumentTitle, StringComparison.Ordinal))
            return ApplyResult.Fail($"This was proposed for \"{proposal.DocumentTitle}\" but the active project is \"{doc.Title}\". Switch to that project and approve again.");
        if (doc.IsReadOnly) return ApplyResult.Fail("The project is read-only.");

        var problems = ModelChecker.Check(doc, proposal.Changes);
        if (problems.Count > 0)
            return ApplyResult.Fail("The model changed since this was proposed. Nothing was changed:" + Environment.NewLine + string.Join(Environment.NewLine, problems.Select(p => "- " + p)));

        var insets = profile?.Manual.DrawingAreaInsetsInches ?? new SheetInsets();
        var created = new List<long>();
        var sheets = new Dictionary<string, ViewSheet>(StringComparer.OrdinalIgnoreCase);
        var autoPlace = new Dictionary<ElementId, (ViewSheet Sheet, List<SheetLayout.Placed> Items)>();
        var schedules = new Dictionary<string, ViewSchedule>(StringComparer.OrdinalIgnoreCase);
        int sheetCount = 0, placedCount = 0, templateCount = 0, scheduleCount = 0, elementCount = 0, valueCount = 0;

        var name = $"Allegro Drafter: {proposal.Title}";
        using var transaction = new Transaction(doc, name.Length > 80 ? name[..80] : name);
        try
        {
            transaction.Start();

            foreach (var change in proposal.Changes)
            {
                switch (change)
                {
                    case CreateSheetChange c:
                    {
                        var symbol = RevitLookup.FindTitleBlock(doc, c.TitleBlock)
                            ?? throw new InvalidOperationException($"Title block '{c.TitleBlock}' not found.");
                        if (!symbol.IsActive) symbol.Activate();
                        var sheet = ViewSheet.Create(doc, symbol.Id);
                        sheet.SheetNumber = c.Number;
                        sheet.Name = c.Name;
                        sheets[c.Number] = sheet;
                        created.Add(sheet.Id.Value);
                        sheetCount++;
                        break;
                    }

                    case PlaceViewChange c:
                    {
                        var sheet = sheets.TryGetValue(c.SheetNumber, out var s) ? s : RevitLookup.FindSheet(doc, c.SheetNumber)
                            ?? throw new InvalidOperationException($"Sheet {c.SheetNumber} not found.");
                        View view = schedules.TryGetValue(c.ViewName, out var newSchedule)
                            ? newSchedule
                            : RevitLookup.ResolveView(doc, c.ViewName, c.ViewType, out var error) ?? throw new InvalidOperationException(error);
                        var point = c.HasPosition ? new XYZ(c.XFeet!.Value, c.YFeet!.Value, 0) : XYZ.Zero;

                        ElementId placedId;
                        if (view is ViewSchedule)
                        {
                            placedId = ScheduleSheetInstance.Create(doc, sheet.Id, view.Id, point).Id;
                        }
                        else
                        {
                            if (!Viewport.CanAddViewToSheet(doc, sheet.Id, view.Id))
                                throw new InvalidOperationException($"Revit won't place \"{view.Name}\" on sheet {sheet.SheetNumber}.");
                            placedId = Viewport.Create(doc, sheet.Id, view.Id, point).Id;
                        }
                        created.Add(placedId.Value);
                        placedCount++;

                        if (!c.HasPosition)
                        {
                            if (!autoPlace.TryGetValue(sheet.Id, out var group))
                                autoPlace[sheet.Id] = group = (sheet, new List<SheetLayout.Placed>());
                            group.Items.Add(new SheetLayout.Placed(placedId, view is ViewSchedule));
                        }
                        break;
                    }

                    case ApplyViewTemplateChange c:
                    {
                        var view = RevitLookup.ResolveView(doc, c.ViewName, c.ViewType, out var error)
                            ?? throw new InvalidOperationException(error);
                        var template = RevitLookup.FindTemplate(doc, c.TemplateName)
                            ?? throw new InvalidOperationException($"View template '{c.TemplateName}' not found.");
                        view.ViewTemplateId = template.Id;
                        templateCount++;
                        break;
                    }

                    case CreateScheduleChange c:
                    {
                        ViewSchedule schedule;
                        if (c.IsCopy)
                        {
                            var source = RevitLookup.FindSchedule(doc, c.SourceSchedule!)
                                ?? throw new InvalidOperationException($"Schedule \"{c.SourceSchedule}\" not found.");
                            schedule = (ViewSchedule)doc.GetElement(source.Duplicate(ViewDuplicateOption.Duplicate));
                        }
                        else
                        {
                            var template = RevitLookup.FindTemplate(doc, c.TemplateName!)
                                ?? throw new InvalidOperationException($"Template '{c.TemplateName}' not found.");
                            schedule = ViewSchedule.CreateSchedule(doc, new ElementId(ParameterWriter.Categories[c.Category!]));
                            if (!schedule.IsValidViewTemplate(template.Id))
                                throw new InvalidOperationException($"Template '{template.Name}' doesn't fit a {c.Category} schedule.");
                            schedule.ViewTemplateId = template.Id;
                        }
                        schedule.Name = c.Name;
                        schedules[c.Name] = schedule;
                        created.Add(schedule.Id.Value);
                        scheduleCount++;
                        break;
                    }

                    case SetParametersChange c:
                    {
                        var element = doc.GetElement(new ElementId(c.ElementId))
                            ?? throw new InvalidOperationException($"Element {c.ElementId} not found.");
                        foreach (var value in c.Values) ParameterWriter.Write(element, value.Name, value.Value);
                        elementCount++;
                        valueCount += c.Values.Count;
                        break;
                    }

                    default:
                        throw new InvalidOperationException($"Unsupported change {change.GetType().Name}.");
                }
            }

            var overflowNotes = new List<string>();
            if (autoPlace.Count > 0)
            {
                doc.Regenerate(); // Viewport sizes and title block extents are only known after regenerating.
                foreach (var (sheet, items) in autoPlace.Values)
                {
                    var overflow = SheetLayout.Arrange(doc, sheet, items, insets);
                    if (overflow > 0) overflowNotes.Add($"{overflow} view(s) on {sheet.SheetNumber} run past the drawing area");
                }
            }

            var status = transaction.Commit();
            if (status != TransactionStatus.Committed)
                return ApplyResult.Fail($"Revit rolled the change back ({status}). Check the Revit warning dialog. Nothing was changed.");

            var parts = new List<string>();
            if (sheetCount > 0) parts.Add($"{sheetCount} sheet(s) created");
            if (placedCount > 0) parts.Add($"{placedCount} view(s) placed");
            if (templateCount > 0) parts.Add($"{templateCount} view template(s) applied");
            if (scheduleCount > 0) parts.Add($"{scheduleCount} schedule(s) created");
            if (elementCount > 0) parts.Add($"{valueCount} value(s) set on {elementCount} element(s)");
            var message = string.Join(", ", parts) + ".";
            if (autoPlace.Count > 0) message += " Auto-placed views are roughly arranged; adjust as needed.";
            if (overflowNotes.Count > 0) message += " Check: " + string.Join("; ", overflowNotes) + ".";
            message += " Undo with Ctrl+Z if needed.";
            return new ApplyResult(true, message, created);
        }
        catch (Exception ex)
        {
            if (transaction.HasStarted() && !transaction.HasEnded()) transaction.RollBack();
            return ApplyResult.Fail($"Failed, so the whole batch was rolled back. Nothing was changed. Reason: {ex.Message}");
        }
    }
}
