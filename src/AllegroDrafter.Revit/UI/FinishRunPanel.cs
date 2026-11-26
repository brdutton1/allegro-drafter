using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AllegroDrafter.Core.Finishing;
using AllegroDrafter.Revit.Finishing;
using Autodesk.Revit.DB;

namespace AllegroDrafter.Revit.UI;

/// <summary>
/// Top of the approval pane while Finish Drafting runs: which step, what it's waiting on,
/// the question cards, and what's left at the end. Built in code (no XAML).
/// </summary>
internal sealed class FinishRunPanel : Border
{
    private static readonly Brush Accent = Frozen(0x1F, 0x6F, 0xB2);
    private static readonly Brush Muted = Frozen(0x66, 0x66, 0x66);
    private static readonly Brush CardBorder = Frozen(0xB8, 0xC9, 0xDB);
    private static readonly Brush Panel = Frozen(0xF3, 0xF7, 0xFB);

    private readonly App _app;
    private readonly FinishRun _run;
    private readonly StackPanel _body = new();

    internal FinishRunPanel(App app)
    {
        _app = app;
        _run = app.Finish;
        Background = Panel;
        BorderBrush = CardBorder;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(4);
        Padding = new Thickness(10);
        Margin = new Thickness(0, 0, 0, 10);
        Child = _body;
        _run.Changed += (_, _) => Dispatcher.BeginInvoke(Refresh);
        Refresh();
    }

    private void Refresh()
    {
        Visibility = _run.HasRun ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        _body.Children.Clear();
        if (!_run.HasRun) return;

        var step = _run.Current;
        var title = new DockPanel();
        if (_run.IsRunning)
        {
            var stop = new Button { Content = "Stop", Padding = new Thickness(10, 2, 10, 2) };
            stop.Click += (_, _) => _run.Stop();
            DockPanel.SetDock(stop, Dock.Right);
            title.Children.Add(stop);
        }
        var where = step is null || step is FinishDone ? "" : $" · Step {step.StepNumber} of {FinishPlanner.Steps.Count}: {step.StepTitle}";
        title.Children.Add(new TextBlock { Text = "Finish Drafting" + where, FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = Accent, TextWrapping = TextWrapping.Wrap });
        _body.Children.Add(title);
        _body.Children.Add(Text(_run.Status, top: 4));
        if (_app.FinishAnswers.Warning is { } warning) _body.Children.Add(Text(warning, muted: true, size: 11, top: 4));

        if (_run.IsRunning && step is FinishQuestions q)
            foreach (var card in q.Cards.Where(c => !_run.State.Answered(c.Id))) _body.Children.Add(Card(card));

        if (step is FinishDone done)
            foreach (var r in done.Remaining) _body.Children.Add(Text("• " + r, top: 2));

        if (_run.Notes.Count > 0)
        {
            _body.Children.Add(Text("Skipped this run:", muted: true, top: 8));
            foreach (var n in _run.Notes) _body.Children.Add(Text("• " + n, muted: true, top: 1));
        }

        if (!_run.IsRunning)
        {
            var again = new Button { Content = step is FinishDone ? "Run again" : "Resume", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(10, 2, 10, 2) };
            again.Click += (_, _) => _run.Start();
            _body.Children.Add(again);
        }
    }

    private UIElement Card(QuestionCard card)
    {
        var panel = new StackPanel();
        panel.Children.Add(Text(card.Prompt));

        TextBox? text = null;
        if (card.AllowText)
        {
            panel.Children.Add(Text(card.TextLabel ?? "Your answer", muted: true, size: 11, top: 6));
            text = new TextBox { Margin = new Thickness(0, 2, 0, 0), Padding = new Thickness(3) };
            panel.Children.Add(text);
        }

        var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var hint = Text("", muted: true, size: 11);
        foreach (var option in card.Options)
        {
            var label = option == card.Recommended ? option + " (recommended)" : option;
            var b = new Button { Content = label, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 4) };
            if (option == card.Recommended) b.FontWeight = FontWeights.SemiBold;
            b.Click += (_, _) =>
            {
                // "No" on a card that takes text means "No, and here's why": the reason is required.
                if (option == "No" && card.AllowText)
                {
                    if (string.IsNullOrWhiteSpace(text?.Text)) { hint.Text = "Write why it's No first. It goes on the element so the reviewer sees it."; return; }
                    _run.Answer(card.Id, "No: " + text!.Text.Trim());
                    return;
                }
                _run.Answer(card.Id, option);
            };
            buttons.Children.Add(b);
        }
        if (card.AllowText && !card.Options.Contains("No"))
        {
            var save = new Button { Content = "Use this", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 4) };
            save.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(text?.Text)) { hint.Text = "Type an answer first, or Skip."; return; }
                _run.Answer(card.Id, text!.Text.Trim());
            };
            buttons.Children.Insert(0, save);
        }
        if (card.ElementId is long id)
        {
            var show = new Button { Content = "Show", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 4), ToolTip = "Select it and zoom to it in Revit" };
            show.Click += (_, _) => ShowElement(id);
            buttons.Children.Add(show);
        }
        panel.Children.Add(buttons);
        panel.Children.Add(hint);

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 8, 0, 0),
            Child = panel,
        };
    }

    private void ShowElement(long id)
    {
        _ = _app.Dispatcher.InvokeAsync(app =>
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc is null) return false;
            var ids = new List<ElementId> { new(id) };
            uidoc.Selection.SetElementIds(ids);
            uidoc.ShowElements(ids);
            return true;
        }).ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
    }

    private static TextBlock Text(string text, bool muted = false, double size = 12, double top = 2) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = size,
        Margin = new Thickness(0, top, 0, 0),
        Foreground = muted ? Muted : Brushes.Black,
    };

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
