using VisualizeScene.Core;

namespace VisualizeScene.Algorithms;

public static partial class GraphAlgorithms
{
    /// <summary>Ordinal, unweighted PageRank. Parallel arcs count separately; two rank buffers are reused across iterations.</summary>
    public static IReadOnlyDictionary<string, double> PageRank(GraphSnapshot graph, double damping = .85,
        int iterations = 200, double tolerance = 1e-10, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!double.IsFinite(damping) || damping < 0 || damping >= 1) throw new ArgumentOutOfRangeException(nameof(damping));
        if (!double.IsFinite(tolerance) || tolerance <= 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
        if (iterations < 1) throw new ArgumentOutOfRangeException(nameof(iterations));
        GraphValidation.Validate(graph);
        var count = graph.Nodes.Length;
        var ids = new Dictionary<string, int>(count, StringComparer.Ordinal);
        for (var i = 0; i < count; i++) ids.Add(graph.Nodes[i].Id, i);
        if (count == 0) return new Dictionary<string, double>(StringComparer.Ordinal);
        var degree = new int[count];
        foreach (var edge in graph.Edges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            checked { degree[ids[edge.SourceId]]++; if (!edge.Directed) degree[ids[edge.TargetId]]++; }
        }
        var offsets = new int[checked(count + 1)];
        for (var i = 0; i < count; i++) offsets[i + 1] = checked(offsets[i] + degree[i]);
        var targets = new int[offsets[count]];
        var cursor = (int[])offsets.Clone();
        foreach (var edge in graph.Edges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var a = ids[edge.SourceId]; var b = ids[edge.TargetId];
            targets[cursor[a]++] = b;
            if (!edge.Directed) targets[cursor[b]++] = a;
        }
        var rank = new double[count]; var next = new double[count]; Array.Fill(rank, 1d / count);
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double dangling = 0;
            for (var i = 0; i < count; i++) if (degree[i] == 0) dangling += rank[i];
            Array.Fill(next, (1 - damping + damping * dangling) / count);
            for (var i = 0; i < count; i++)
            {
                if ((i & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                if (degree[i] == 0) continue;
                var share = damping * rank[i] / degree[i];
                for (var j = offsets[i]; j < offsets[i + 1]; j++)
                {
                    if ((j & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                    next[targets[j]] += share;
                }
            }
            double error = 0;
            for (var i = 0; i < count; i++) error += Math.Abs(next[i] - rank[i]);
            (rank, next) = (next, rank);
            if (error <= tolerance) break;
        }
        cancellationToken.ThrowIfCancellationRequested();
        var result = new Dictionary<string, double>(count, StringComparer.Ordinal);
        for (var i = 0; i < count; i++) result.Add(graph.Nodes[i].Id, rank[i]);
        return result;
    }
}
