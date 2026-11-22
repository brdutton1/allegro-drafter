using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AllegroDrafter.Core.Proposals;

namespace AllegroDrafter.Revit.UI;

/// <summary>
/// Dockable pane listing every proposal with Approve / Reject.
/// Built in code (no XAML) so it compiles on any OS.
/// </summary>
internal sealed class ApprovalPane : UserControl
{
    private static readonly Brush PendingBrush = Frozen(0x1F, 0x6F, 0xB2);
    private static readonly Brush ApplyingBrush = Frozen(0xB2, 0x7A, 0x1F);
    private static readonly Brush AppliedBrush = Frozen(0x2E, 0x7D, 0x32);
    private static readonly Brush RejectedBrush = Frozen(0x75, 0x75, 0x75);
    private static readonly Brush FailedBrush = Frozen(0xC6, 0x28, 0x28);
    private static readonly Brush MutedBrush = Frozen(0x66, 0x66, 0x66);
    private static readonly Brush BorderGray = Frozen(0xD0, 0xD0, 0xD0);

    private readonly App _app;
    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBlock _statusLine;
    private readonly TextBlock _emptyState;
    private readonly StackPanel _cards = new();

    internal ApprovalPane(App app)
    {
        _app = app;
        MinWidth = 280;
        Background = Brushes.White;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 12;

        _statusLine = Text("", muted: true);
        _emptyState = Text("No proposals yet. Ask Claude to set up sheets, place views or apply view templates.", muted: true);

        var clear = new Button { Content = "Clear approved & rejected", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
        clear.Click += (_, _) => _app.Proposals.ClearResolved();

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        header.Children.Add(new TextBlock { Text = "AI proposals", FontSize = 16, FontWeight = FontWeights.SemiBold });
        header.Children.Add(_statusLine);
        header.Children.Add(Text("Nothing changes in the model until you approve. Each approval is one undo step.", muted: true));
        header.Children.Add(clear);
        DockPanel.SetDock(header, Dock.Top);

        var body = new StackPanel();
        body.Children.Add(_emptyState);
        body.Children.Add(_cards);

        var root = new DockPanel { Margin = new Thickness(8) };
        root.Children.Add(header);
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body });
        Content = root;

        _app.Proposals.Changed += (_, _) => Dispatcher.BeginInvoke(Refresh);
        Loaded += (_, _) => Refresh();
    }

    private void Refresh()
    {
        var proposals = _app.Proposals.List().OrderByDescending(p => p.CreatedUtc).ToList();
        _cards.Children.Clear();
        foreach (var p in proposals) _cards.Children.Add(Card(p));
        _emptyState.Visibility = proposals.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var server = _app.Server?.IsRunning == true ? "Claude connection ready" : "Claude connection DOWN (see Allegro > Status)";
        var standards = _app.Standards.Current is { } s ? $"standards: {s.SourceDocument}" : "no office standards loaded";
        _statusLine.Text = $"{server} · {standards} · {_app.Proposals.PendingCount} pending";
    }

    private UIElement Card(Proposal p)
    {
        var panel = new StackPanel();

        var badge = new Border
        {
            Background = StatusBrush(p.Status),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 1, 6, 1),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = p.Status.ToString().ToUpperInvariant(), Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold },
        };
        DockPanel.SetDock(badge, Dock.Right);
        var titleRow = new DockPanel();
        titleRow.Children.Add(badge);
        titleRow.Children.Add(new TextBlock { Text = p.Title, FontWeight = FontWeights.SemiBold, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 6, 0) });
        panel.Children.Add(titleRow);

        var meta = $"{p.Id} · {p.DocumentTitle} · {p.CreatedUtc.ToLocalTime():t}";
        if (p.ResolvedBy is not null) meta += $" · {p.Status.ToString().ToLowerInvariant()} by {p.ResolvedBy}";
        panel.Children.Add(Text(meta, muted: true, size: 11));

        if (!string.IsNullOrWhiteSpace(p.Rationale)) panel.Children.Add(Text(p.Rationale, top: 6));

        var changes = new StackPanel { Margin = new Thickness(16, 4, 0, 0) };
        foreach (var change in p.Changes) changes.Children.Add(Text(change.Describe(), top: 1));
        var expander = new Expander
        {
            Header = $"{p.Changes.Count} change{(p.Changes.Count == 1 ? "" : "s")}",
            Margin = new Thickness(0, 6, 0, 0),
            IsExpanded = _expanded.Contains(p.Id),
            Content = changes,
        };
        expander.Expanded += (_, _) => _expanded.Add(p.Id);
        expander.Collapsed += (_, _) => _expanded.Remove(p.Id);
        panel.Children.Add(expander);

        if (!string.IsNullOrEmpty(p.ResultMessage))
        {
            var result = Text(p.ResultMessage, top: 6);
            if (p.Status == ProposalStatus.Failed) result.Foreground = FailedBrush;
            panel.Children.Add(result);
        }

        if (!p.IsResolved)
        {
            var approve = new Button
            {
                Content = p.Status == ProposalStatus.Failed ? "Retry" : "Approve",
                Padding = new Thickness(12, 3, 12, 3),
                Margin = new Thickness(0, 0, 6, 0),
                IsEnabled = p.CanApprove,
            };
            approve.Click += async (_, _) =>
            {
                approve.IsEnabled = false; // Instant feedback. The store also blocks a second apply.
                try
                {
                    await _app.ApproveAsync(p.Id);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Allegro Drafter", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            var reject = new Button { Content = "Reject", Padding = new Thickness(12, 3, 12, 3), IsEnabled = p.CanReject };
            reject.Click += (_, _) => _app.Reject(p.Id);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            buttons.Children.Add(approve);
            buttons.Children.Add(reject);
            panel.Children.Add(buttons);
        }

        return new Border
        {
            BorderBrush = BorderGray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 8),
            Child = panel,
        };
    }

    private static Brush StatusBrush(ProposalStatus status) => status switch
    {
        ProposalStatus.Pending => PendingBrush,
        ProposalStatus.Applying => ApplyingBrush,
        ProposalStatus.Applied => AppliedBrush,
        ProposalStatus.Rejected => RejectedBrush,
        _ => FailedBrush,
    };

    private static TextBlock Text(string text, bool muted = false, double size = 12, double top = 2) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = size,
        Margin = new Thickness(0, top, 0, 0),
        Foreground = muted ? MutedBrush : Brushes.Black,
    };

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
