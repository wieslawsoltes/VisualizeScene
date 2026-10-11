using VisualizeScene.Core;

namespace VisualizeScene.Algorithms;

public static partial class GraphAlgorithms
{
    private sealed class ResidualArc(string to, double capacity)
    {
        public string To = to;
        public double Capacity = capacity;
        public ResidualArc Reverse = null!;
    }
    /// <summary>Edmonds–Karp flow with separate residual pairs for parallel and antiparallel edges. Double precision, not arbitrary precision.</summary>
    public static FlowResult MaximumFlow(GraphSnapshot graph, string source, string sink, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); GraphValidation.Validate(graph);
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(sink);
        if (source == sink || graph.Edges.Any(e => !e.Directed || e.Weight < 0))
            throw new ArgumentException("Flow requires distinct terminals, directed edges and finite nonnegative capacities.");
        var residual = graph.Nodes.ToDictionary(n => n.Id, _ => new List<ResidualArc>(), StringComparer.Ordinal);
        if (!residual.ContainsKey(source) || !residual.ContainsKey(sink)) throw new ArgumentException("Unknown terminal.");
        var forward = new Dictionary<string, ResidualArc>(graph.Edges.Length, StringComparer.Ordinal);
        foreach (var e in graph.Edges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var a = new ResidualArc(e.TargetId, e.Weight); var b = new ResidualArc(e.SourceId, 0); a.Reverse = b; b.Reverse = a;
            residual[e.SourceId].Add(a); residual[e.TargetId].Add(b); forward[e.Id] = a;
        }
        // Reuse scratch storage between augmentations; retain the last reachable set as the min-cut partition.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var previous = new Dictionary<string, (string From, ResidualArc Arc)>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        double flow = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested(); seen.Clear(); previous.Clear(); queue.Clear();
            seen.Add(source); queue.Enqueue(source);
            while (!seen.Contains(sink) && queue.TryDequeue(out var id))
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var arc in residual[id]) if (arc.Capacity > 0 && seen.Add(arc.To))
                { previous[arc.To] = (id, arc); queue.Enqueue(arc.To); }
            }
            if (!seen.Contains(sink)) break;
            var amount = double.PositiveInfinity;
            for (var id = sink; id != source; id = previous[id].From) amount = Math.Min(amount, previous[id].Arc.Capacity);
            for (var id = sink; id != source; id = previous[id].From)
            { var arc = previous[id].Arc; arc.Capacity -= amount; arc.Reverse.Capacity += amount; }
            flow += amount;
            if (!double.IsFinite(flow)) throw new OverflowException("The flow exceeds the finite double range.");
        }
        // C - remaining loses a small flow when C is huge. The paired reverse arc stores the net flow directly.
        // Each original edge owns a distinct pair, including antiparallel edges and self-loops.
        var edgeFlows = new Dictionary<string, double>(forward.Count, StringComparer.Ordinal);
        foreach (var edge in graph.Edges) edgeFlows.Add(edge.Id, forward[edge.Id].Reverse.Capacity);
        return new(flow, edgeFlows, seen);
    }
}
