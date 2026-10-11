using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VisualizeScene.Core;
using VisualizeScene.Layout;

namespace VisualizeScene.Workbench;

public sealed partial class WorkbenchPage
{
    private readonly NumberBox labelGap = new() { Header = "Label clearance", Value = 8, Minimum = 0, Maximum = 100,
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
    private readonly CheckBox selectedLabelsOnly = new() { Content = "Selected labels / owners only", IsChecked = false };
    private readonly TextBlock labelPlacementOutput = new() { Text = "Places independent labels without moving nodes or routes.",
        TextWrapping = TextWrapping.Wrap, FontSize = 11 };
    private LabelPlacementResult? lastLabelPlacement;

    private void AddLabelPlacementControls(StackPanel panel)
    {
        panel.Children.Add(Heading("LABEL PLACEMENT")); panel.Children.Add(labelGap);
        Register("SelectedLabelsOnly", selectedLabelsOnly); panel.Children.Add(selectedLabelsOnly);
        panel.Children.Add(Button("Place labels", "PlaceLabels", () => _ = PlaceLabels()));
        panel.Children.Add(labelPlacementOutput);
    }

    private async Task PlaceLabels()
    {
        await RunAsync(async () =>
        {
            if (!double.IsFinite(labelGap.Value) || labelGap.Value < 0 || labelGap.Value > 100)
                throw new ArgumentException("Label clearance must be between 0 and 100.");
            layoutCancellation?.Cancel(); layoutCancellation?.Dispose(); layoutCancellation = new(); var token = layoutCancellation.Token;
            var revision = document.Revision; var snapshot = document.Snapshot; var index = snapshot.Index();
            var options = new LabelPlacementOptions(Gap: labelGap.Value);
            var selection = graph.Session.Selection.ToHashSet(StringComparer.Ordinal);
            IReadOnlySet<string>? ids = null;
            if (selectedLabelsOnly.IsChecked == true)
            {
                ids = snapshot.Labels.Where(l => selection.Contains(l.Id) || selection.Contains(l.OwnerId))
                    .Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
                if (ids.Count == 0) { labelPlacementOutput.Text = "Select independent labels or their owners first."; return; }
            }
            // Shape on the owning UI thread. The worker reads only immutable model data and captured metrics.
            var metrics = new Dictionary<(string Text, double Size), PointD>();
            void Measure(GraphLabel label)
            {
                token.ThrowIfCancellationRequested(); var key = (label.Text, label.FontSize);
                if (!metrics.ContainsKey(key)) metrics[key] = graph.MeasureLabel(label);
            }
            foreach (var label in snapshot.Labels.Where(l => GraphScene.IsOwnerVisible(index, l.OwnerId))) Measure(label);
            foreach (var edge in snapshot.Edges.Where(e => e.Text.Length > 0 && GraphScene.IsEdgeVisible(index, e)))
                Measure(new("", edge.Id, edge.Text, new(0, -9), 11));
            status.Text = "Placing labels…"; lastLabelPlacement = null; await Task.Delay(30, token);
            var result = await Task.Run(() => new LabelPlacementEngine().Calculate(snapshot, options,
                l => metrics[(l.Text, l.FontSize)], ids, cancellationToken: token), token);
            token.ThrowIfCancellationRequested();
            if (revision != document.Revision) throw new InvalidOperationException("Graph changed during label placement; result discarded.");
            document.Replace(result.Graph, "Place labels"); lastLabelPlacement = result;
            labelPlacementOutput.Text = $"Moved {result.MovedLabels} of {result.EligibleLabels} eligible labels. {result.Conflicts.Length} remaining conflicts."
                + (result.Conflicts.IsEmpty ? "" : "\n" + string.Join("\n", result.Conflicts.Take(5).Select(c => $"{c.LabelId}: {c.Kind} {c.OtherId}")));
            status.Text = $"Label placement complete: {result.MovedLabels} moved; {result.Conflicts.Length} conflicts.";
            graph.Fit();
        });
    }
}
