using Microsoft.UI.Xaml.Controls;
using VisualizeScene.Algorithms;
using VisualizeScene.Core;
using VisualizeScene.Editing;
using VisualizeScene.Layout;

namespace VisualizeScene.Workbench;

public sealed partial class WorkbenchPage
{
    private async Task<bool> ConfirmDiscard()
    {
        if (!dirty) return true;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Replace the current graph?", Content = "The current graph has unsaved edits. Save it before replacing it to keep those changes.", PrimaryButtonText = "Replace", CloseButtonText = "Cancel" };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
    private async Task NewGraph()
    {
        await RunAsync(async () => { if (!await ConfirmDiscard()) return; document.Replace(GraphSnapshot.Empty, "New graph", true); graph.Session.Select([]); graph.SetMode(EditorMode.Select); currentName = "Untitled"; savePoint.MarkSaved(); graph.Fit(); });
    }
    private void LoadExample(string name)
    {
        Run(() => { layoutCancellation?.Cancel(); document.Replace(SampleGraphs.Create(name), "Load " + name, true); currentName = name; savePoint.MarkSaved(); graph.Session.Select([]); graph.SetMode(EditorMode.Select); graph.Fit(); lastError = ""; });
    }
    private async Task ApplyLayout()
    {
        await RunAsync(async () =>
        {
            layoutCancellation?.Cancel(); layoutCancellation?.Dispose(); layoutCancellation = new(); var token = layoutCancellation.Token;
            var revision = document.Revision; var snapshot = document.Snapshot; var options = new LayoutOptions((LayoutKind)layoutKind.SelectedItem, (LayoutDirection)direction.SelectedItem, spacing.Value, layers.Value);
            status.Text = "Calculating " + options.Kind + " layout…"; await Task.Delay(30, token);
            var result = await Task.Run(() => new LayoutEngine().Calculate(snapshot, options, token), token);
            token.ThrowIfCancellationRequested(); if (revision != document.Revision) throw new InvalidOperationException("Graph changed during layout; result discarded.");
            document.Replace(result.Graph, options.Kind + " layout"); graph.Fit(); status.Text = result.Diagnostics.Count == 0 ? options.Kind + " layout complete." : string.Join(" ", result.Diagnostics);
        });
    }
    private async Task RouteEdges()
    {
        await RunAsync(async () =>
        {
            layoutCancellation?.Cancel(); layoutCancellation?.Dispose(); layoutCancellation = new(); var token = layoutCancellation.Token;
            var revision = document.Revision; var snapshot = document.Snapshot; status.Text = "Routing edges…"; await Task.Delay(30, token);
            var result = await Task.Run(() => new OrthogonalRouter().Route(snapshot, cancellationToken: token), token); token.ThrowIfCancellationRequested();
            if (revision != document.Revision) throw new InvalidOperationException("Graph changed during routing; result discarded.");
            document.Replace(result.Graph, "Route edges"); status.Text = result.Diagnostics.Count == 0 ? "All edges routed." : $"{result.Diagnostics.Count} routing diagnostics: " + result.Diagnostics[0].Message;
        });
    }
    private void Analyze(string name)
    {
        var snapshot = document.Snapshot; var index = snapshot.Index(); var nodes = graph.Session.Selection.Where(index.Nodes.ContainsKey).ToArray();
        switch (name)
        {
            case "Connected components":
                var components = GraphAlgorithms.ConnectedComponents(snapshot); analysisOutput.Text = $"{components.Count} connected components\n" + string.Join("\n", components.Select((c, i) => $"{i + 1}: {c.Count} nodes")); break;
            case "Strong components":
                var strong = GraphAlgorithms.StronglyConnectedComponents(snapshot); analysisOutput.Text = $"{strong.Count} strongly connected components\n" + string.Join("\n", strong.Take(15).Select(c => string.Join(", ", c))); break;
            case "Shortest path":
                if (nodes.Length != 2) throw new InvalidOperationException("Select two nodes, source first, using Ctrl+click.");
                var path = snapshot.Edges.Any(e => e.Weight < 0) ? GraphAlgorithms.BellmanFord(snapshot, nodes[0], nodes[1]) : GraphAlgorithms.ShortestPath(snapshot, nodes[0], nodes[1]);
                analysisOutput.Text = path.Found ? $"Cost: {path.Cost:G6}\n" + string.Join(" → ", path.Nodes) : "No directed path exists."; if (path.Found) graph.Session.Select(path.Nodes.Concat(path.Edges)); break;
            case "Minimum spanning forest":
                var forest = GraphAlgorithms.MinimumSpanningForest(snapshot); graph.Session.Select(forest); analysisOutput.Text = $"{forest.Count} edges in the minimum spanning forest.\nTotal weight: {snapshot.Edges.Where(e => forest.Contains(e.Id)).Sum(e => e.Weight):G6}"; break;
            case "PageRank": Scores(GraphAlgorithms.PageRank(snapshot)); break;
            case "Betweenness": Scores(GraphAlgorithms.Betweenness(snapshot)); break;
            case "Color graph":
                var colors = GraphAlgorithms.Coloring(snapshot); var count = colors.Count == 0 ? 0 : colors.Values.Max() + 1;
                document.Edit("Color graph", edit => { foreach (var n in snapshot.Nodes) edit.Update(n with { Fill = Color(colors[n.Id], count) }); }); analysisOutput.Text = $"DSATUR used {count} colors.\nThis is a heuristic, not an exact chromatic-number computation."; break;
            case "Maximum flow":
                if (nodes.Length != 2) throw new InvalidOperationException("Select source and sink nodes, in that order. Edge weights are capacities.");
                var flow = GraphAlgorithms.MaximumFlow(snapshot, nodes[0], nodes[1]); graph.Session.Select(flow.EdgeFlows.Where(p => p.Value > 0).Select(p => p.Key)); analysisOutput.Text = $"Maximum flow: {flow.Value:G6}\n" + string.Join("\n", flow.EdgeFlows.Where(p => p.Value > 0).Select(p => $"{p.Key}: {p.Value:G4}")); break;
        }
        void Scores(IReadOnlyDictionary<string, double> values) => analysisOutput.Text = name + "\n" + string.Join("\n", values.OrderByDescending(p => p.Value).Take(20).Select(p => $"{index.Nodes[p.Key].Text.Replace('\n', ' ')}: {p.Value:G4}"));
        static string Color(int value, int count)
        {
            var h = value * 6d / Math.Max(1, count); var sector = (int)h; var f = h - sector; const double high = .96, low = .76;
            var rising = low + (high - low) * f; var falling = high - (high - low) * f;
            var (r, g, b) = sector switch { 0 => (high, rising, low), 1 => (falling, high, low), 2 => (low, high, rising), 3 => (low, falling, high), 4 => (rising, low, high), _ => (high, low, falling) };
            return $"#{(int)(r * 255):X2}{(int)(g * 255):X2}{(int)(b * 255):X2}";
        }
    }
}
