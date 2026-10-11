using System.Collections.Immutable;
using VisualizeScene.Core;

namespace VisualizeScene.Layout;

public sealed record LabelPlacementOptions(double Gap = 8, int Passes = 4, int CandidateLimit = 128,
    int EdgeSamples = 5, bool AvoidNodes = true, bool AvoidEdges = true, bool AvoidPorts = true,
    long EvaluationLimit = 5_000_000);
public sealed record LabelCandidateContext(GraphIndex Index, GraphLabel Label, PointD Size,
    LabelPlacementOptions Options);

/// <summary>Produces label centers in world coordinates. The current location is always considered as well.</summary>
public interface ILabelCandidateProvider
{
    IEnumerable<PointD> GetCandidates(LabelCandidateContext context);
}

public enum LabelConflictKind { Node, Edge, Port, Label, Caption }
public sealed record LabelConflict(string LabelId, string OtherId, LabelConflictKind Kind);
public sealed record LabelPlacementResult(GraphSnapshot Graph, int MovedLabels, int EligibleLabels,
    ImmutableArray<LabelConflict> Conflicts, long Evaluations);

/// <summary>Deterministic bounded candidate refinement. Only independent label offsets are changed.</summary>
public sealed class LabelPlacementEngine
{
    private sealed record Obstacle(string Id, LabelConflictKind Kind, RectD Bounds, PointD? Start = null, PointD? End = null);
    private sealed record Candidate(PointD Center, RectD Bounds, int Conflicts, double Area);
    private readonly record struct Score(int Conflicts, double Area, double Movement) : IComparable<Score>
    {
        public int CompareTo(Score other)
        {
            var order = Conflicts.CompareTo(other.Conflicts);
            if (order != 0) return order;
            order = Area.CompareTo(other.Area);
            return order != 0 ? order : Movement.CompareTo(other.Movement);
        }
    }

    public LabelPlacementResult Calculate(GraphSnapshot graph, LabelPlacementOptions? options = null,
        Func<GraphLabel, PointD>? measure = null, IReadOnlySet<string>? labelIds = null,
        ILabelCandidateProvider? candidates = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        options ??= new(); Validate(options); cancellationToken.ThrowIfCancellationRequested(); GraphValidation.Validate(graph);
        var index = graph.Index();
        if (labelIds is not null && labelIds.Any(id => !index.Labels.ContainsKey(id)))
            throw new ArgumentException("The placement selection contains an unknown label.", nameof(labelIds));
        measure ??= GraphScene.EstimateLabelSize; candidates ??= new DefaultLabelCandidateProvider();
        long evaluations = 0;
        void Tick()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++evaluations > options.EvaluationLimit) throw new InvalidOperationException("Label placement exceeded its evaluation limit. No result was applied.");
        }
        PointD Measure(GraphLabel label)
        {
            Tick(); var size = measure(label);
            if (!size.IsFinite || size.X <= 0 || size.Y <= 0) throw new ArgumentException("Label measurements must be finite and positive.", nameof(measure));
            return size;
        }
        var visible = graph.Labels.Where(l => GraphScene.IsOwnerVisible(index, l.OwnerId)).OrderBy(l => l.Id, StringComparer.Ordinal).ToArray();
        var sizes = visible.ToDictionary(l => l.Id, Measure, StringComparer.Ordinal);
        var centers = visible.ToDictionary(l => l.Id, l => GraphScene.LabelAnchor(index, l.OwnerId) + l.Offset, StringComparer.Ordinal);
        var originalCenters = centers.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        var eligible = visible.Where(l => (labelIds is null || labelIds.Contains(l.Id)) && !Locked(index, l.OwnerId)).ToArray();
        var obstacles = new List<Obstacle>();
        if (options.AvoidNodes) foreach (var node in graph.Nodes.Where(n => index.IsVisible(n.Id)))
        {
            Tick(); var b = node.Bounds;
            if (!node.IsGroup || node.Collapsed) obstacles.Add(new(node.Id, LabelConflictKind.Node, b.Inflate(options.Gap)));
            else
            {
                // Expanded group interiors are usable; headers and borders are obstacles.
                obstacles.Add(new(node.Id, LabelConflictKind.Node, new RectD(b.X, b.Y, b.Width, Math.Min(28, b.Height)).Inflate(options.Gap)));
                obstacles.Add(new(node.Id, LabelConflictKind.Node, new RectD(b.X, b.Y, 1, b.Height).Inflate(options.Gap)));
                obstacles.Add(new(node.Id, LabelConflictKind.Node, new RectD(b.Right - 1, b.Y, 1, b.Height).Inflate(options.Gap)));
                obstacles.Add(new(node.Id, LabelConflictKind.Node, new RectD(b.X, b.Bottom - 1, b.Width, 1).Inflate(options.Gap)));
            }
        }
        foreach (var edge in graph.Edges.Where(e => GraphScene.IsEdgeVisible(index, e)))
        {
            Tick(); var path = index.EdgePath(edge);
            if (options.AvoidEdges) for (var i = 1; i < path.Count; i++)
            {
                Tick(); // Route subdivision consumes work just like obstacle queries.
                obstacles.Add(new(edge.Id, LabelConflictKind.Edge, RectD.FromPoints(path[i - 1], path[i]).Inflate(options.Gap), path[i - 1], path[i]));
            }
            if (edge.Text.Length > 0)
            {
                var caption = new GraphLabel("", edge.Id, edge.Text, new(0, -9), 11);
                obstacles.Add(new(edge.Id, LabelConflictKind.Caption, GraphScene.LabelBounds(index, caption, Measure(caption)).Inflate(options.Gap)));
            }
        }
        if (options.AvoidPorts) foreach (var port in graph.Ports.Where(p => index.IsVisible(p.NodeId)))
        {
            Tick(); var p = index.PortLocation(port.Id); var radius = 4 + options.Gap;
            obstacles.Add(new(port.Id, LabelConflictKind.Port, new(p.X - radius, p.Y - radius, 2 * radius, 2 * radius)));
        }
        // Node captions normally lie inside their node obstacle. When node avoidance is off,
        // their text remains an obstacle so labels do not obscure captions.
        if (!options.AvoidNodes) foreach (var node in graph.Nodes.Where(n => index.IsVisible(n.Id) && n.Text.Length > 0))
        {
            var offset = node.IsGroup && !node.Collapsed ? new PointD(0, 14 - node.Bounds.Height / 2) : new PointD();
            var caption = new GraphLabel("", node.Id, node.Text, offset, 13);
            obstacles.Add(new(node.Id, LabelConflictKind.Caption, GraphScene.LabelBounds(index, caption, Measure(caption)).Inflate(options.Gap)));
        }
        var staticIndex = new SpatialIndex<Obstacle>(obstacles.Select(o => (o.Bounds, o)));
        var occupied = new LabelOccupancy(visible.Select(l => (l.Id, Box(centers[l.Id], sizes[l.Id]))), options.Gap);
        (int Count, double Area) StaticConflicts(RectD box, string id, List<LabelConflict>? report = null)
        {
            var seen = new HashSet<(string, LabelConflictKind)>(); var area = 0d;
            foreach (var obstacle in staticIndex.Query(box))
            {
                Tick();
                var overlap = obstacle.Start is { } a && obstacle.End is { } b
                    ? SegmentIntersects(box.Inflate(options.Gap), a, b) : Overlap(box, obstacle.Bounds) > 1e-8;
                if (!overlap || !seen.Add((obstacle.Id, obstacle.Kind))) continue;
                area += obstacle.Start is null ? Overlap(box, obstacle.Bounds) : 1;
                report?.Add(new(id, obstacle.Id, obstacle.Kind));
            }
            return (seen.Count, area);
        }
        var choices = new Dictionary<string, Candidate[]>(StringComparer.Ordinal);
        foreach (var label in eligible)
        {
            Tick(); var seen = new HashSet<PointD>(); var list = new List<Candidate>();
            void Add(PointD center)
            {
                Tick(); if (!center.IsFinite) throw new ArgumentException("A candidate center must be finite.", nameof(candidates));
                if (!seen.Add(center)) return;
                var box = Box(center, sizes[label.Id]); var conflict = StaticConflicts(box, label.Id);
                list.Add(new(center, box, conflict.Count, conflict.Area));
            }
            Add(centers[label.Id]);
            var count = 0;
            foreach (var center in candidates.GetCandidates(new(index, label, sizes[label.Id], options)))
            {
                if (++count > options.CandidateLimit) throw new InvalidOperationException("The label candidate provider exceeded CandidateLimit.");
                Add(center);
            }
            choices[label.Id] = list.ToArray();
        }
        Score Evaluate(Candidate candidate, string id)
        {
            Tick(); var count = candidate.Conflicts; var area = candidate.Area;
            foreach (var other in occupied.Query(candidate.Bounds))
            {
                Tick(); if (other.Id == id) continue;
                var intersection = Overlap(candidate.Bounds.Inflate(options.Gap / 2), other.Bounds.Inflate(options.Gap / 2));
                if (intersection > 1e-8) { count++; area += intersection; }
            }
            return new(count, area, PointD.Distance(candidate.Center, originalCenters[id]));
        }
        // Stable ordering plus strict improvement avoids cyclic choices and gives repeatable results.
        for (var pass = 0; pass < options.Passes; pass++)
        {
            var changed = false;
            foreach (var label in eligible)
            {
                Tick(); var current = choices[label.Id].First(c => c.Center == centers[label.Id]);
                var best = current; var bestScore = Evaluate(current, label.Id);
                foreach (var candidate in choices[label.Id])
                {
                    var score = Evaluate(candidate, label.Id);
                    if (score.CompareTo(bestScore) < 0) { best = candidate; bestScore = score; }
                }
                if (best.Center == current.Center) continue;
                changed = true; centers[label.Id] = best.Center; occupied.Update(label.Id, best.Bounds);
            }
            if (!changed) break;
        }
        var conflicts = new List<LabelConflict>();
        foreach (var label in visible)
        {
            Tick(); var box = Box(centers[label.Id], sizes[label.Id]); StaticConflicts(box, label.Id, conflicts);
            foreach (var other in occupied.Query(box))
            {
                Tick(); if (StringComparer.Ordinal.Compare(label.Id, other.Id) >= 0) continue;
                if (Overlap(box.Inflate(options.Gap / 2), other.Bounds.Inflate(options.Gap / 2)) > 1e-8)
                    conflicts.Add(new(label.Id, other.Id, LabelConflictKind.Label));
            }
        }
        var moved = eligible.Where(l => centers[l.Id] != originalCenters[l.Id]).Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
        var result = moved.Count == 0 ? graph : graph with
        {
            Labels = graph.Labels.Select(l => moved.Contains(l.Id)
                ? l with { Offset = centers[l.Id] - GraphScene.LabelAnchor(index, l.OwnerId) } : l).ToImmutableArray()
        };
        cancellationToken.ThrowIfCancellationRequested(); GraphValidation.Validate(result);
        return new(result, moved.Count, eligible.Length, conflicts.ToImmutableArray(), evaluations);
    }

    private static void Validate(LabelPlacementOptions options)
    {
        if (!double.IsFinite(options.Gap) || options.Gap < 0 || options.Gap > 10000 || options.Passes < 1 || options.Passes > 50 ||
            options.CandidateLimit < 1 || options.CandidateLimit > 4096 || options.EdgeSamples < 1 || options.EdgeSamples > 50 || options.EvaluationLimit < 1)
            throw new ArgumentOutOfRangeException(nameof(options));
    }
    private static bool Locked(GraphIndex index, string owner)
    {
        bool NodeLocked(string id)
        {
            for (string? current = id; current is not null; current = index.Nodes[current].ParentId)
                if (index.Nodes[current].Locked) return true;
            return false;
        }
        if (index.Nodes.ContainsKey(owner)) return NodeLocked(owner);
        if (index.Ports.TryGetValue(owner, out var port)) return NodeLocked(port.NodeId);
        var edge = index.Edges[owner]; return NodeLocked(edge.SourceId) || NodeLocked(edge.TargetId);
    }
    private static RectD Box(PointD center, PointD size)
    {
        var box = new RectD(center.X - size.X / 2, center.Y - size.Y / 2, size.X, size.Y);
        if (!box.IsValid || !double.IsFinite(box.Right) || !double.IsFinite(box.Bottom)) throw new ArgumentException("Label bounds exceed the finite coordinate range.");
        return box;
    }
    private static double Overlap(RectD a, RectD b) => Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X)) * Math.Max(0, Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y));
    private static bool SegmentIntersects(RectD bounds, PointD a, PointD b)
    {
        // Slab intersection with the open interior: touching the clearance boundary is allowed.
        var box = bounds.Inflate(-1e-8); if (box.Width <= 0 || box.Height <= 0) return false;
        var low = 0d; var high = 1d; var d = b - a;
        bool Axis(double p, double v, double min, double max)
        {
            if (Math.Abs(v) < 1e-15) return p >= min && p <= max;
            var t0 = (min - p) / v; var t1 = (max - p) / v;
            low = Math.Max(low, Math.Min(t0, t1)); high = Math.Min(high, Math.Max(t0, t1)); return low <= high;
        }
        return Axis(a.X, d.X, box.X, box.Right) && Axis(a.Y, d.Y, box.Y, box.Bottom);
    }

    /// <summary>Mutable broad phase for labels changed during a refinement pass. Giant boxes use a bounded overflow set.</summary>
    private sealed class LabelOccupancy
    {
        private const double CellSize = 128;
        private readonly double gap;
        private readonly Dictionary<string, RectD> bounds = new(StringComparer.Ordinal);
        private readonly Dictionary<(double, double), HashSet<string>> cells = [];
        private readonly Dictionary<string, (double, double)[]> memberships = new(StringComparer.Ordinal);
        private readonly HashSet<string> overflow = new(StringComparer.Ordinal);
        public LabelOccupancy(IEnumerable<(string Id, RectD Bounds)> entries, double gap)
        { this.gap = gap; foreach (var entry in entries) Update(entry.Id, entry.Bounds); }
        private (double, double)[]? Keys(RectD box)
        {
            box = box.Inflate(gap / 2);
            var x = Math.Floor(box.X / CellSize); var y = Math.Floor(box.Y / CellSize);
            var nx = Math.Floor(box.Right / CellSize) - x + 1; var ny = Math.Floor(box.Bottom / CellSize) - y + 1;
            if (!double.IsFinite(nx * ny) || nx < 1 || ny < 1 || nx * ny > 256 || Math.Abs(x) > 1e12 || Math.Abs(y) > 1e12) return null;
            return Enumerable.Range(0, (int)nx).SelectMany(i => Enumerable.Range(0, (int)ny).Select(j => (x + i, y + j))).ToArray();
        }
        public void Update(string id, RectD box)
        {
            if (memberships.Remove(id, out var old)) foreach (var key in old) { cells[key].Remove(id); if (cells[key].Count == 0) cells.Remove(key); }
            overflow.Remove(id); bounds[id] = box; var keys = Keys(box);
            if (keys is null) { overflow.Add(id); return; }
            memberships[id] = keys;
            foreach (var key in keys) { if (!cells.TryGetValue(key, out var set)) cells[key] = set = []; set.Add(id); }
        }
        public IEnumerable<(string Id, RectD Bounds)> Query(RectD box)
        {
            var keys = Keys(box); var ids = new HashSet<string>(overflow, StringComparer.Ordinal);
            if (keys is null) ids.UnionWith(bounds.Keys);
            else foreach (var key in keys) if (cells.TryGetValue(key, out var set)) ids.UnionWith(set);
            // Deterministic summation order matters when overlap areas are fractional.
            return ids.Order(StringComparer.Ordinal).Select(id => (id, bounds[id]));
        }
    }
}
