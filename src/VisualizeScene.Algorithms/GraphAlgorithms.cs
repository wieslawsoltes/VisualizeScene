using VisualizeScene.Core;

namespace VisualizeScene.Algorithms;

public sealed record GraphPath(IReadOnlyList<string> Nodes, IReadOnlyList<string> Edges, double Cost)
{
    public bool Found => double.IsFinite(Cost);
}
public sealed record FlowResult(double Value, IReadOnlyDictionary<string, double> EdgeFlows, IReadOnlySet<string> SourcePartition);

public static class GraphAlgorithms
{
    private sealed record Arc(string To, GraphEdge Edge);
    private static Dictionary<string, List<Arc>> Adjacency(GraphSnapshot graph, bool undirected = false, bool reverse = false)
    {
        var result = graph.Nodes.ToDictionary(n => n.Id, _ => new List<Arc>());
        foreach (var e in graph.Edges)
        {
            var a = reverse ? e.TargetId : e.SourceId; var b = reverse ? e.SourceId : e.TargetId;
            result[a].Add(new(b, e));
            if (undirected || !e.Directed) result[b].Add(new(a, e));
        }
        return result;
    }
    public static IReadOnlyList<string> BreadthFirst(GraphSnapshot graph, string start, bool undirected = false, CancellationToken cancellationToken = default)
    {
        var adjacency = Adjacency(graph, undirected);
        if (!adjacency.ContainsKey(start)) throw new ArgumentException("Start node is absent.", nameof(start));
        var result = new List<string>(); var visited = new HashSet<string> { start }; var queue = new Queue<string>(); queue.Enqueue(start);
        while (queue.TryDequeue(out var id))
        {
            cancellationToken.ThrowIfCancellationRequested(); result.Add(id);
            foreach (var arc in adjacency[id]) if (visited.Add(arc.To)) queue.Enqueue(arc.To);
        }
        return result;
    }
    public static IReadOnlyList<IReadOnlyList<string>> ConnectedComponents(GraphSnapshot graph, CancellationToken cancellationToken = default)
    {
        var adjacency = Adjacency(graph, true); var visited = new HashSet<string>(); var result = new List<IReadOnlyList<string>>();
        foreach (var node in graph.Nodes)
        {
            if (!visited.Add(node.Id)) continue;
            var component = new List<string>(); var queue = new Queue<string>(); queue.Enqueue(node.Id);
            while (queue.TryDequeue(out var id))
            {
                cancellationToken.ThrowIfCancellationRequested(); component.Add(id);
                foreach (var arc in adjacency[id]) if (visited.Add(arc.To)) queue.Enqueue(arc.To);
            }
            result.Add(component);
        }
        return result;
    }
    /// <summary>Iterative Kosaraju traversal; does not consume the call stack for long chains.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> StronglyConnectedComponents(GraphSnapshot graph, CancellationToken cancellationToken = default)
    {
        var adjacency = Adjacency(graph); var reversed = Adjacency(graph, reverse: true);
        var visited = new HashSet<string>(); var order = new List<string>();
        foreach (var n in graph.Nodes)
        {
            if (!visited.Add(n.Id)) continue;
            var stack = new Stack<(string Id, int Next)>(); stack.Push((n.Id, 0));
            while (stack.TryPop(out var entry))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var neighbors = adjacency[entry.Id];
                if (entry.Next >= neighbors.Count) { order.Add(entry.Id); continue; }
                stack.Push((entry.Id, entry.Next + 1));
                var next = neighbors[entry.Next].To;
                if (visited.Add(next)) stack.Push((next, 0));
            }
        }
        visited.Clear(); var result = new List<IReadOnlyList<string>>();
        for (var i = order.Count - 1; i >= 0; i--)
        {
            if (!visited.Add(order[i])) continue;
            var component = new List<string>(); var stack = new Stack<string>(); stack.Push(order[i]);
            while (stack.TryPop(out var id))
            {
                cancellationToken.ThrowIfCancellationRequested(); component.Add(id);
                foreach (var a in reversed[id]) if (visited.Add(a.To)) stack.Push(a.To);
            }
            result.Add(component);
        }
        return result;
    }
    public static IReadOnlyList<string> TopologicalOrder(GraphSnapshot graph)
    {
        if (graph.Edges.Any(e => !e.Directed)) throw new ArgumentException("Topological ordering requires directed edges.");
        var adjacency = Adjacency(graph); var indegrees = graph.Nodes.ToDictionary(n => n.Id, _ => 0);
        foreach (var e in graph.Edges) indegrees[e.TargetId]++;
        var queue = new Queue<string>(graph.Nodes.Where(n => indegrees[n.Id] == 0).Select(n => n.Id)); var order = new List<string>();
        while (queue.TryDequeue(out var id))
        {
            order.Add(id);
            foreach (var a in adjacency[id]) if (--indegrees[a.To] == 0) queue.Enqueue(a.To);
        }
        if (order.Count != graph.Nodes.Length) throw new InvalidOperationException("The graph contains a directed cycle.");
        return order;
    }
    public static GraphPath ShortestPath(GraphSnapshot graph, string source, string target, bool undirected = false, CancellationToken cancellationToken = default)
    {
        if (graph.Edges.Any(e => e.Weight < 0)) throw new ArgumentException("Dijkstra requires nonnegative weights; use BellmanFord for negative weights.");
        var adjacency = Adjacency(graph, undirected);
        if (!adjacency.ContainsKey(source) || !adjacency.ContainsKey(target)) throw new ArgumentException("Unknown endpoint.");
        var distance = graph.Nodes.ToDictionary(n => n.Id, _ => double.PositiveInfinity); var previous = new Dictionary<string, (string Node, string Edge)>();
        var queue = new PriorityQueue<string, double>(); distance[source] = 0; queue.Enqueue(source, 0);
        while (queue.TryDequeue(out var id, out var cost))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (cost > distance[id]) continue;
            if (id == target) break;
            foreach (var arc in adjacency[id])
            {
                var candidate = cost + arc.Edge.Weight;
                if (candidate >= distance[arc.To]) continue;
                distance[arc.To] = candidate; previous[arc.To] = (id, arc.Edge.Id); queue.Enqueue(arc.To, candidate);
            }
        }
        return Path(source, target, distance[target], previous);
    }
    public static GraphPath BellmanFord(GraphSnapshot graph, string source, string target, CancellationToken cancellationToken = default)
    {
        var adjacency = Adjacency(graph);
        if (!adjacency.ContainsKey(source) || !adjacency.ContainsKey(target)) throw new ArgumentException("Unknown endpoint.");
        var distance = graph.Nodes.ToDictionary(n => n.Id, _ => double.PositiveInfinity); distance[source] = 0;
        var previous = new Dictionary<string, (string Node, string Edge)>();
        for (var iteration = 0; iteration < graph.Nodes.Length; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested(); var changed = false;
            foreach (var (id, arcs) in adjacency) foreach (var arc in arcs)
            {
                var candidate = distance[id] + arc.Edge.Weight;
                if (candidate >= distance[arc.To]) continue;
                if (iteration == graph.Nodes.Length - 1) throw new InvalidOperationException("A reachable negative-weight cycle exists.");
                distance[arc.To] = candidate; previous[arc.To] = (id, arc.Edge.Id); changed = true;
            }
            if (!changed) break;
        }
        return Path(source, target, distance[target], previous);
    }
    private static GraphPath Path(string source, string target, double cost, Dictionary<string, (string Node, string Edge)> previous)
    {
        if (!double.IsFinite(cost)) return new([], [], double.PositiveInfinity);
        var nodes = new List<string> { target }; var edges = new List<string>(); var current = target;
        while (current != source) { var p = previous[current]; edges.Add(p.Edge); nodes.Add(p.Node); current = p.Node; }
        nodes.Reverse(); edges.Reverse(); return new(nodes, edges, cost);
    }
    /// <summary>Kruskal minimum spanning forest of the underlying undirected multigraph.</summary>
    public static IReadOnlyList<string> MinimumSpanningForest(GraphSnapshot graph)
    {
        var parent = graph.Nodes.ToDictionary(n => n.Id, n => n.Id); var rank = graph.Nodes.ToDictionary(n => n.Id, _ => 0);
        string Find(string id) { while (parent[id] != id) { parent[id] = parent[parent[id]]; id = parent[id]; } return id; }
        var result = new List<string>();
        foreach (var edge in graph.Edges.OrderBy(e => e.Weight).ThenBy(e => e.Id, StringComparer.Ordinal))
        {
            var a = Find(edge.SourceId); var b = Find(edge.TargetId); if (a == b) continue;
            if (rank[a] < rank[b]) (a, b) = (b, a);
            parent[b] = a; if (rank[a] == rank[b]) rank[a]++; result.Add(edge.Id);
        }
        return result;
    }
    public static IReadOnlyDictionary<string, double> PageRank(GraphSnapshot graph, double damping = .85, int iterations = 200, double tolerance = 1e-10, CancellationToken cancellationToken = default)
    {
        if (damping < 0 || damping >= 1 || iterations < 1 || tolerance <= 0) throw new ArgumentOutOfRangeException(nameof(damping));
        var adjacency = Adjacency(graph); var n = graph.Nodes.Length;
        var rank = graph.Nodes.ToDictionary(x => x.Id, _ => n == 0 ? 0 : 1d / n);
        if (n == 0) return rank;
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dangling = rank.Where(pair => adjacency[pair.Key].Count == 0).Sum(pair => pair.Value);
            var next = graph.Nodes.ToDictionary(x => x.Id, _ => (1 - damping + damping * dangling) / n);
            foreach (var (id, arcs) in adjacency)
                if (arcs.Count > 0) foreach (var arc in arcs) next[arc.To] += damping * rank[id] / arcs.Count;
            var error = next.Sum(pair => Math.Abs(pair.Value - rank[pair.Key])); rank = next;
            if (error <= tolerance) break;
        }
        return rank;
    }
    /// <summary>Brandes unweighted betweenness. Parallel edges count as distinct shortest paths.</summary>
    public static IReadOnlyDictionary<string, double> Betweenness(GraphSnapshot graph, bool undirected = false, CancellationToken cancellationToken = default)
    {
        var adjacency = Adjacency(graph, undirected); var scores = graph.Nodes.ToDictionary(n => n.Id, _ => 0d);
        foreach (var source in graph.Nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var predecessors = graph.Nodes.ToDictionary(n => n.Id, _ => new List<string>());
            var sigma = graph.Nodes.ToDictionary(n => n.Id, _ => 0d); sigma[source.Id] = 1;
            var distance = graph.Nodes.ToDictionary(n => n.Id, _ => -1); distance[source.Id] = 0;
            var stack = new Stack<string>(); var queue = new Queue<string>(); queue.Enqueue(source.Id);
            while (queue.TryDequeue(out var id))
            {
                stack.Push(id);
                foreach (var arc in adjacency[id])
                {
                    if (distance[arc.To] < 0) { distance[arc.To] = distance[id] + 1; queue.Enqueue(arc.To); }
                    if (distance[arc.To] == distance[id] + 1) { sigma[arc.To] += sigma[id]; predecessors[arc.To].Add(id); }
                }
            }
            var dependency = graph.Nodes.ToDictionary(n => n.Id, _ => 0d);
            while (stack.TryPop(out var id))
            {
                foreach (var predecessor in predecessors[id]) dependency[predecessor] += sigma[predecessor] / sigma[id] * (1 + dependency[id]);
                if (id != source.Id) scores[id] += dependency[id];
            }
        }
        if (undirected || graph.Edges.All(e => !e.Directed)) foreach (var id in scores.Keys.ToArray()) scores[id] /= 2;
        return scores;
    }
    /// <summary>DSATUR greedy coloring. It is not an exact chromatic-number solver.</summary>
    public static IReadOnlyDictionary<string, int> Coloring(GraphSnapshot graph)
    {
        if (graph.Edges.Any(e => e.SourceId == e.TargetId)) throw new ArgumentException("A graph with self-loops cannot be properly colored.");
        var adjacency = Adjacency(graph, true); var colors = new Dictionary<string, int>();
        while (colors.Count < graph.Nodes.Length)
        {
            var id = graph.Nodes.Where(n => !colors.ContainsKey(n.Id))
                .OrderByDescending(n => adjacency[n.Id].Where(a => colors.ContainsKey(a.To)).Select(a => colors[a.To]).Distinct().Count())
                .ThenByDescending(n => adjacency[n.Id].Count).ThenBy(n => n.Id, StringComparer.Ordinal).First().Id;
            var used = adjacency[id].Where(a => colors.ContainsKey(a.To)).Select(a => colors[a.To]).ToHashSet();
            var color = 0; while (used.Contains(color)) color++; colors[id] = color;
        }
        return colors;
    }
    private sealed class ResidualArc(string to, double capacity)
    {
        public string To = to;
        public double Capacity = capacity;
        public ResidualArc Reverse = null!;
    }
    public static FlowResult MaximumFlow(GraphSnapshot graph, string source, string sink, CancellationToken cancellationToken = default)
    {
        if (source == sink || graph.Edges.Any(e => !e.Directed || e.Weight < 0 || !double.IsFinite(e.Weight))) throw new ArgumentException("Flow requires distinct terminals, directed edges and finite nonnegative capacities.");
        var residual = graph.Nodes.ToDictionary(n => n.Id, _ => new List<ResidualArc>());
        if (!residual.ContainsKey(source) || !residual.ContainsKey(sink)) throw new ArgumentException("Unknown terminal.");
        var forward = new Dictionary<string, ResidualArc>();
        foreach (var e in graph.Edges)
        {
            var a = new ResidualArc(e.TargetId, e.Weight); var b = new ResidualArc(e.SourceId, 0); a.Reverse = b; b.Reverse = a;
            residual[e.SourceId].Add(a); residual[e.TargetId].Add(b); forward[e.Id] = a;
        }
        double flow = 0; HashSet<string> seen;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            seen = [source]; var previous = new Dictionary<string, (string From, ResidualArc Arc)>(); var queue = new Queue<string>(); queue.Enqueue(source);
            while (queue.TryDequeue(out var id) && !seen.Contains(sink))
                foreach (var arc in residual[id]) if (arc.Capacity > 1e-12 && seen.Add(arc.To)) { previous[arc.To] = (id, arc); queue.Enqueue(arc.To); }
            if (!seen.Contains(sink)) break;
            var amount = double.PositiveInfinity;
            for (var id = sink; id != source; id = previous[id].From) amount = Math.Min(amount, previous[id].Arc.Capacity);
            for (var id = sink; id != source; id = previous[id].From) { var arc = previous[id].Arc; arc.Capacity -= amount; arc.Reverse.Capacity += amount; }
            flow += amount;
        }
        return new(flow, graph.Edges.ToDictionary(e => e.Id, e => e.Weight - forward[e.Id].Capacity), seen);
    }
}
