using System.Collections.Immutable;
using VisualizeScene.Algorithms;
using VisualizeScene.Core;

namespace VisualizeScene.Layout;

public enum LayoutKind { Hierarchical, Organic, Tree, Circular, Radial, Grid }
public enum LayoutDirection { TopToBottom, LeftToRight, BottomToTop, RightToLeft }
public sealed record LayoutOptions(LayoutKind Kind = LayoutKind.Hierarchical, LayoutDirection Direction = LayoutDirection.TopToBottom,
    double NodeSpacing = 45, double LayerSpacing = 75, int Iterations = 180, int Seed = 42, bool UseExistingPositions = false);
public sealed record LayoutResult(GraphSnapshot Graph, IReadOnlyList<string> Diagnostics);
public interface ILayoutAlgorithm
{
    LayoutResult Calculate(GraphSnapshot graph, LayoutOptions options, CancellationToken cancellationToken = default);
}

/// <summary>Independent layout algorithms. Compound groups are laid out bottom-up; locked nodes retain their positions.</summary>
public sealed class LayoutEngine : ILayoutAlgorithm
{
    public LayoutResult Calculate(GraphSnapshot graph, LayoutOptions options, CancellationToken cancellationToken = default)
    {
        GraphValidation.Validate(graph);
        if (!Enum.IsDefined(options.Kind) || !Enum.IsDefined(options.Direction) || !double.IsFinite(options.NodeSpacing) || !double.IsFinite(options.LayerSpacing) || options.NodeSpacing < 1 || options.LayerSpacing < 1 || options.Iterations < 1 || options.Iterations > 5000) throw new ArgumentException("Invalid layout options.");
        var nodes = graph.Nodes.ToDictionary(n => n.Id); var index = graph.Index(); var diagnostics = new List<string>();
        if (graph.Nodes.Any(n => n.Locked)) diagnostics.Add("Locked positions are retained; overlap-free placement around fixed nodes is not guaranteed.");
        void Translate(string id, PointD delta)
        {
            var n = nodes[id]; if (n.Locked) return;
            nodes[id] = n with { Bounds = n.Bounds.Move(delta) };
            foreach (var descendant in index.Descendants(id)) if (!nodes[descendant.Id].Locked) nodes[descendant.Id] = nodes[descendant.Id] with { Bounds = nodes[descendant.Id].Bounds.Move(delta) };
        }
        void Level(string? parent, int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (depth > 128) throw new NotSupportedException("Layout nesting is limited to 128 levels.");
            var children = graph.Nodes.Where(n => n.ParentId == parent).Select(n => n.Id).ToArray();
            foreach (var id in children)
            {
                var node = nodes[id];
                if (!node.IsGroup || node.Collapsed || index.Children(id).Count == 0) continue;
                Level(id, depth + 1);
                var b = RectD.Enclose(index.Children(id).Select(n => nodes[n.Id].Bounds)).Inflate(28);
                b = b with { Y = b.Y - 22, Height = b.Height + 22 };
                if (!node.Locked) nodes[id] = node with { Bounds = b };
            }
            if (children.Length == 0) return;
            string? Representative(string id)
            {
                for (var current = graph.Nodes.Length > 0 ? index.Nodes[id] : null; current is not null; current = current.ParentId is null ? null : index.Nodes[current.ParentId])
                    if (current.ParentId == parent) return current.Id;
                return null;
            }
            var localNodes = children.Select(id => nodes[id] with { ParentId = null, IsGroup = false, Collapsed = false }).ToImmutableArray();
            var localEdges = graph.Edges.Select(e => (Edge: e, A: Representative(e.SourceId), B: Representative(e.TargetId)))
                .Where(e => e.A is not null && e.B is not null && e.A != e.B)
                .Select(e => e.Edge with { SourceId = e.A!, TargetId = e.B!, SourcePortId = null, TargetPortId = null, Bends = [] }).ToImmutableArray();
            var local = new GraphSnapshot(localNodes, localEdges, [], []);
            var positions = Place(local, options, cancellationToken);
            foreach (var id in children) if (!nodes[id].Locked) Translate(id, positions[id] - new PointD(nodes[id].Bounds.X, nodes[id].Bounds.Y));
        }
        Level(null, 0);
        var result = new GraphSnapshot(graph.Nodes.Select(n => nodes[n.Id]).ToImmutableArray(), graph.Edges.Select(e => e with { Bends = [] }).ToImmutableArray(), graph.Ports, graph.Labels);
        GraphValidation.Validate(result);
        return new(result, diagnostics);
    }
    private static Dictionary<string, PointD> Place(GraphSnapshot graph, LayoutOptions options, CancellationToken token)
    {
        var horizontal = options.Direction is LayoutDirection.LeftToRight or LayoutDirection.RightToLeft;
        var normalized = horizontal ? graph with { Nodes = graph.Nodes.Select(n => n with { Bounds = new(n.Bounds.Y, n.Bounds.X, n.Bounds.Height, n.Bounds.Width) }).ToImmutableArray() } : graph;
        var centers = options.Kind switch
        {
            LayoutKind.Hierarchical => Hierarchical(normalized, options, token),
            LayoutKind.Tree => Tree(normalized, options, token),
            LayoutKind.Radial => Radial(normalized, options, token),
            LayoutKind.Circular => Circular(normalized, options),
            LayoutKind.Grid => Grid(normalized, options),
            LayoutKind.Organic => Organic(normalized, options, token),
            _ => throw new NotSupportedException()
        };
        var result = new Dictionary<string, PointD>();
        foreach (var node in normalized.Nodes)
        {
            var p = centers[node.Id];
            if (options.Direction is LayoutDirection.BottomToTop or LayoutDirection.RightToLeft) p = p with { Y = -p.Y };
            if (horizontal) p = new(p.Y, p.X);
            var original = graph.Nodes.First(n => n.Id == node.Id);
            result[node.Id] = p - new PointD(original.Bounds.Width / 2, original.Bounds.Height / 2);
        }
        return result;
    }
    private static Dictionary<string, PointD> Grid(GraphSnapshot graph, LayoutOptions options)
    {
        var result = new Dictionary<string, PointD>(); var count = graph.Nodes.Length;
        var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count)));
        var widths = Enumerable.Range(0, columns).Select(c => graph.Nodes.Where((_, i) => i % columns == c).Select(n => n.Bounds.Width).DefaultIfEmpty(0).Max()).ToArray();
        double y = 0;
        for (var row = 0; row * columns < count; row++)
        {
            var items = graph.Nodes.Skip(row * columns).Take(columns).ToArray(); var height = items.Max(n => n.Bounds.Height); double x = 0;
            for (var col = 0; col < items.Length; col++) { result[items[col].Id] = new(x + widths[col] / 2, y + height / 2); x += widths[col] + options.NodeSpacing; }
            y += height + options.LayerSpacing;
        }
        return result;
    }
    private static Dictionary<string, PointD> Circular(GraphSnapshot graph, LayoutOptions options)
    {
        var result = new Dictionary<string, PointD>(); var count = graph.Nodes.Length;
        if (count == 0) return result;
        var diameters = graph.Nodes.Select(n => Math.Sqrt(n.Bounds.Width * n.Bounds.Width + n.Bounds.Height * n.Bounds.Height) + options.NodeSpacing).ToArray();
        var radius = count == 1 ? 0 : Math.Max(diameters.Sum() / (2 * Math.PI), diameters.Max() / (2 * Math.Sin(Math.PI / count)));
        for (var i = 0; i < count; i++) { var angle = 2 * Math.PI * i / count - Math.PI / 2; result[graph.Nodes[i].Id] = new(radius * Math.Cos(angle), radius * Math.Sin(angle)); }
        return result;
    }
    private static Dictionary<string, PointD> Hierarchical(GraphSnapshot graph, LayoutOptions options, CancellationToken token)
    {
        var components = GraphAlgorithms.StronglyConnectedComponents(graph, token); var componentId = new Dictionary<string, int>();
        for (var i = 0; i < components.Count; i++) foreach (var id in components[i]) componentId[id] = i;
        var successors = Enumerable.Range(0, components.Count).Select(_ => new HashSet<int>()).ToArray(); var indegree = new int[components.Count]; var rank = new int[components.Count];
        foreach (var e in graph.Edges) { var a = componentId[e.SourceId]; var b = componentId[e.TargetId]; if (a != b && successors[a].Add(b)) indegree[b]++; }
        var queue = new Queue<int>(Enumerable.Range(0, components.Count).Where(i => indegree[i] == 0));
        while (queue.TryDequeue(out var i)) foreach (var j in successors[i]) { rank[j] = Math.Max(rank[j], rank[i] + 1); if (--indegree[j] == 0) queue.Enqueue(j); }
        var nodes = graph.Nodes.ToDictionary(n => n.Id); var layers = graph.Nodes.GroupBy(n => rank[componentId[n.Id]]).OrderBy(g => g.Key).Select(g => g.Select(n => n.Id).ToList()).ToArray();
        var incoming = graph.Edges.ToLookup(e => e.TargetId, e => e.SourceId); var outgoing = graph.Edges.ToLookup(e => e.SourceId, e => e.TargetId);
        // Alternating median/barycenter sweeps reduce crossings without random order changes.
        for (var sweep = 0; sweep < 8; sweep++)
        {
            token.ThrowIfCancellationRequested();
            var order = layers.SelectMany(layer => layer.Select((id, i) => (id, i))).ToDictionary(v => v.id, v => (double)v.i);
            var sequence = sweep % 2 == 0 ? layers : layers.Reverse();
            foreach (var layer in sequence)
            {
                var sorted = layer.Select((id, i) => (id, i, neighbors: (sweep % 2 == 0 ? incoming[id] : outgoing[id]).ToArray()))
                    .OrderBy(v => v.neighbors.Length == 0 ? order[v.id] : v.neighbors.Average(id => order[id])).ThenBy(v => v.i).Select(v => v.id).ToArray();
                layer.Clear(); layer.AddRange(sorted);
                for (var i = 0; i < layer.Count; i++) order[layer[i]] = i;
            }
        }
        var result = new Dictionary<string, PointD>(); double y = 0;
        foreach (var layer in layers)
        {
            var height = layer.Max(id => nodes[id].Bounds.Height); var width = layer.Sum(id => nodes[id].Bounds.Width) + options.NodeSpacing * (layer.Count - 1); var x = -width / 2;
            foreach (var id in layer) { var n = nodes[id]; result[id] = new(x + n.Bounds.Width / 2, y + height / 2); x += n.Bounds.Width + options.NodeSpacing; }
            y += height + options.LayerSpacing;
        }
        return result;
    }
    private static (List<string> Roots, Dictionary<string, List<string>> Children, Dictionary<string, int> Depth) Forest(GraphSnapshot graph)
    {
        var index = graph.Index(); var roots = new List<string>(); var children = graph.Nodes.ToDictionary(n => n.Id, _ => new List<string>()); var depth = new Dictionary<string, int>();
        foreach (var node in graph.Nodes.OrderBy(n => index.Incoming(n.Id).Count == 0 ? 0 : 1))
        {
            if (depth.ContainsKey(node.Id)) continue;
            roots.Add(node.Id); depth[node.Id] = 0; var queue = new Queue<string>(); queue.Enqueue(node.Id);
            while (queue.TryDequeue(out var id))
            {
                var neighbors = index.Outgoing(id).Select(e => e.TargetId).Concat(index.Incoming(id).Where(e => !e.Directed).Select(e => e.SourceId));
                foreach (var next in neighbors) if (depth.TryAdd(next, depth[id] + 1)) { children[id].Add(next); queue.Enqueue(next); }
            }
        }
        return (roots, children, depth);
    }
    private static Dictionary<string, PointD> Tree(GraphSnapshot graph, LayoutOptions options, CancellationToken token)
    {
        var (roots, children, depth) = Forest(graph); var nodes = graph.Nodes.ToDictionary(n => n.Id); var widths = new Dictionary<string, double>();
        foreach (var id in depth.OrderByDescending(p => p.Value).Select(p => p.Key))
        {
            token.ThrowIfCancellationRequested(); widths[id] = Math.Max(nodes[id].Bounds.Width, children[id].Sum(c => widths[c]) + Math.Max(0, children[id].Count - 1) * options.NodeSpacing);
        }
        var heights = graph.Nodes.GroupBy(n => depth[n.Id]).ToDictionary(g => g.Key, g => g.Max(n => n.Bounds.Height)); var ys = new Dictionary<int, double>(); double y = 0;
        foreach (var d in heights.Keys.Order()) { ys[d] = y + heights[d] / 2; y += heights[d] + options.LayerSpacing; }
        var result = new Dictionary<string, PointD>(); var queue = new Queue<(string Id, double X)>(); double rootX = 0;
        foreach (var root in roots) { queue.Enqueue((root, rootX)); rootX += widths[root] + options.NodeSpacing * 2; }
        while (queue.TryDequeue(out var item))
        {
            result[item.Id] = new(item.X + widths[item.Id] / 2, ys[depth[item.Id]]);
            var childWidth = children[item.Id].Sum(c => widths[c]) + Math.Max(0, children[item.Id].Count - 1) * options.NodeSpacing;
            var x = item.X + (widths[item.Id] - childWidth) / 2;
            foreach (var child in children[item.Id]) { queue.Enqueue((child, x)); x += widths[child] + options.NodeSpacing; }
        }
        return result;
    }
    private static Dictionary<string, PointD> Radial(GraphSnapshot graph, LayoutOptions options, CancellationToken token)
    {
        var forest = Forest(graph); var nodes = graph.Nodes.ToDictionary(n => n.Id); var result = new Dictionary<string, PointD>();
        double previousRadius = 0, previousDiameter = 0;
        foreach (var layer in forest.Depth.GroupBy(p => p.Value).OrderBy(g => g.Key))
        {
            token.ThrowIfCancellationRequested();
            var ids = layer.Select(p => p.Key).ToArray(); var diameter = ids.Max(id => Math.Sqrt(nodes[id].Bounds.Width * nodes[id].Bounds.Width + nodes[id].Bounds.Height * nodes[id].Bounds.Height));
            var radius = layer.Key == 0 && ids.Length == 1 ? 0 : Math.Max(previousRadius + (previousDiameter + diameter) / 2 + options.LayerSpacing, ids.Length == 1 ? 0 : (diameter + options.NodeSpacing) / (2 * Math.Sin(Math.PI / ids.Length)));
            for (var i = 0; i < ids.Length; i++) { var angle = i * 2 * Math.PI / ids.Length - Math.PI / 2; result[ids[i]] = new(radius * Math.Cos(angle), radius * Math.Sin(angle)); }
            previousRadius = radius; previousDiameter = diameter;
        }
        return result;
    }
    private sealed class Quad
    {
        public RectD Bounds;
        public PointD Center;
        public int Count;
        public int[]? Items;
        public Quad[]? Children;
        public static Quad Build(PointD[] points, int[] ids, RectD bounds, int depth = 0)
        {
            var q = new Quad { Bounds = bounds, Count = ids.Length };
            q.Center = ids.Aggregate(new PointD(), (sum, i) => sum + points[i]) * (1d / Math.Max(1, ids.Length));
            if (ids.Length <= 4 || depth >= 24 || bounds.Width < .01) { q.Items = ids; return q; }
            var groups = new List<int>[] { [], [], [], [] };
            foreach (var id in ids) groups[(points[id].X >= bounds.Center.X ? 1 : 0) + (points[id].Y >= bounds.Center.Y ? 2 : 0)].Add(id);
            var children = new List<Quad>();
            for (var i = 0; i < 4; i++) if (groups[i].Count > 0)
                children.Add(Build(points, groups[i].ToArray(), new(bounds.X + (i % 2) * bounds.Width / 2, bounds.Y + (i / 2) * bounds.Height / 2, bounds.Width / 2, bounds.Height / 2), depth + 1));
            q.Children = children.ToArray(); return q;
        }
        public PointD Repulsion(int id, PointD[] points, double strength)
        {
            var point = points[id];
            if (Items is not null)
            {
                var force = new PointD();
                foreach (var other in Items) if (id != other) { var d = point - points[other]; var length = Math.Max(1, d.Length); force += d * (strength / (length * length)); }
                return force;
            }
            var delta = point - Center; var distance = Math.Max(1, delta.Length);
            if (!Bounds.Contains(point) && Bounds.Width / distance < .7) return delta * (strength * Count / (distance * distance));
            return Children!.Aggregate(new PointD(), (sum, child) => sum + child.Repulsion(id, points, strength));
        }
    }
    private static Dictionary<string, PointD> Organic(GraphSnapshot graph, LayoutOptions options, CancellationToken token)
    {
        var count = graph.Nodes.Length; var result = new Dictionary<string, PointD>(); if (count == 0) return result;
        var random = new Random(options.Seed); var circle = Circular(graph, options);
        var positions = graph.Nodes.Select(n => (options.UseExistingPositions || n.Locked ? n.Bounds.Center : circle[n.Id]) + new PointD(random.NextDouble() * .01, random.NextDouble() * .01)).ToArray();
        var ids = graph.Nodes.Select((n, i) => (n.Id, i)).ToDictionary(p => p.Id, p => p.i);
        var ideal = graph.Nodes.Average(n => Math.Max(n.Bounds.Width, n.Bounds.Height)) + options.NodeSpacing;
        var numbers = Enumerable.Range(0, count).ToArray();
        for (var iteration = 0; iteration < options.Iterations; iteration++)
        {
            token.ThrowIfCancellationRequested();
            var bounds = RectD.Enclose(positions.Select(p => new RectD(p.X, p.Y, 1, 1))).Inflate(1); var size = Math.Max(bounds.Width, bounds.Height);
            var quad = Quad.Build(positions, numbers, new(bounds.X, bounds.Y, size, size)); var forces = numbers.Select(i => quad.Repulsion(i, positions, ideal * ideal)).ToArray();
            foreach (var edge in graph.Edges)
            {
                var a = ids[edge.SourceId]; var b = ids[edge.TargetId]; if (a == b) continue;
                var delta = positions[b] - positions[a]; var distance = Math.Max(1, delta.Length); var force = delta * (distance / ideal * .15); forces[a] += force; forces[b] -= force;
            }
            var temperature = Math.Max(.2, ideal * .15 * Math.Pow(1 - (double)iteration / options.Iterations, 1.5));
            for (var i = 0; i < count; i++)
            {
                if (graph.Nodes[i].Locked) continue;
                var force = forces[i] - positions[i] * .03; var length = force.Length;
                if (length > 1e-9) positions[i] += force * (Math.Min(length, temperature) / length);
            }
        }
        // Resolve residual rectangular overlap with a spatial broad phase, not all-pairs tests.
        for (var pass = 0; pass < 30; pass++)
        {
            token.ThrowIfCancellationRequested(); var boxes = numbers.Select(i => new RectD(positions[i].X - graph.Nodes[i].Bounds.Width / 2, positions[i].Y - graph.Nodes[i].Bounds.Height / 2, graph.Nodes[i].Bounds.Width, graph.Nodes[i].Bounds.Height).Inflate(options.NodeSpacing / 4)).ToArray();
            var spatial = new SpatialIndex<int>(numbers.Select(i => (boxes[i], i))); var corrections = new PointD[count]; var overlaps = 0;
            for (var i = 0; i < count; i++) foreach (var j in spatial.Query(boxes[i]).Where(j => j > i))
            {
                var dx = Math.Min(boxes[i].Right, boxes[j].Right) - Math.Max(boxes[i].X, boxes[j].X); var dy = Math.Min(boxes[i].Bottom, boxes[j].Bottom) - Math.Max(boxes[i].Y, boxes[j].Y);
                if (dx <= 0 || dy <= 0) continue; overlaps++;
                var delta = dx < dy ? new PointD((positions[i].X <= positions[j].X ? -1 : 1) * (dx / 2 + 1), 0) : new PointD(0, (positions[i].Y <= positions[j].Y ? -1 : 1) * (dy / 2 + 1));
                corrections[i] += delta; corrections[j] -= delta;
            }
            for (var i = 0; i < count; i++) if (!graph.Nodes[i].Locked) positions[i] += corrections[i];
            if (overlaps == 0) break;
        }
        for (var i = 0; i < count; i++) result[graph.Nodes[i].Id] = positions[i];
        return result;
    }
}
