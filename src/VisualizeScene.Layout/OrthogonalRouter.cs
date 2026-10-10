using System.Collections.Immutable;
using VisualizeScene.Core;

namespace VisualizeScene.Layout;

public sealed record RoutingOptions(double Clearance = 12, double BendPenalty = 25, int MaximumVisitedStates = 50000);
public sealed record RoutingDiagnostic(string EdgeId, string Message);
public sealed record RoutingResult(GraphSnapshot Graph, IReadOnlyList<RoutingDiagnostic> Diagnostics);

/// <summary>Obstacle-aware rectilinear A* routing on an implicit Hanan grid. Unroutable edges remain unchanged with diagnostics.</summary>
public sealed class OrthogonalRouter
{
    private readonly record struct State(int X, int Y, int Direction);
    public RoutingResult Route(GraphSnapshot graph, RoutingOptions? options = null, CancellationToken cancellationToken = default)
    {
        GraphValidation.Validate(graph); options ??= new();
        if (!double.IsFinite(options.Clearance) || options.Clearance < 1 || !double.IsFinite(options.BendPenalty) || options.BendPenalty < 0 || options.MaximumVisitedStates < 1) throw new ArgumentException("Invalid routing options.");
        var index = graph.Index(); var diagnostics = new List<RoutingDiagnostic>(); var edges = new List<GraphEdge>();
        foreach (var edge in graph.Edges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceId = index.VisibleRepresentative(edge.SourceId); var targetId = index.VisibleRepresentative(edge.TargetId);
            if (sourceId != edge.SourceId || targetId != edge.TargetId) { edges.Add(edge); continue; }
            if (sourceId == targetId) { edges.Add(edge); diagnostics.Add(new(edge.Id, "Self-loop route retained; obstacle-aware self-loop routing is not implemented.")); continue; }
            var source = index.Nodes[sourceId]; var target = index.Nodes[targetId];
            (PointD Anchor, PointD Stub) Terminal(GraphNode node, PointD toward, string? portId)
            {
                var b = node.Bounds; PointD anchor, direction;
                if (portId is null)
                {
                    var delta = toward - b.Center;
                    direction = Math.Abs(delta.X) / b.Width > Math.Abs(delta.Y) / b.Height ? new(delta.X < 0 ? -1 : 1, 0) : new(0, delta.Y < 0 ? -1 : 1);
                    anchor = Geometry.ClipToNode(node, b.Center + direction * 1000);
                }
                else
                {
                    anchor = index.PortLocation(portId);
                    var distances = new[] { anchor.X - b.X, b.Right - anchor.X, anchor.Y - b.Y, b.Bottom - anchor.Y };
                    var side = Array.IndexOf(distances, distances.Min());
                    direction = side switch { 0 => new(-1, 0), 1 => new(1, 0), 2 => new(0, -1), _ => new(0, 1) };
                }
                var outside = direction.X < 0 ? new PointD(b.X - options.Clearance - 1, anchor.Y) : direction.X > 0 ? new(b.Right + options.Clearance + 1, anchor.Y) : direction.Y < 0 ? new(anchor.X, b.Y - options.Clearance - 1) : new(anchor.X, b.Bottom + options.Clearance + 1);
                return (anchor, outside);
            }
            var start = Terminal(source, target.Bounds.Center, edge.SourcePortId); var finish = Terminal(target, source.Bounds.Center, edge.TargetPortId);
            var obstacles = graph.Nodes.Where(n => index.IsVisible(n.Id) && (!n.IsGroup || n.Collapsed)).Select(n => n.Id == sourceId || n.Id == targetId ? n.Bounds : n.Bounds.Inflate(options.Clearance)).ToArray();
            bool Inside(PointD p, RectD r) => p.X > r.X + 1e-7 && p.X < r.Right - 1e-7 && p.Y > r.Y + 1e-7 && p.Y < r.Bottom - 1e-7;
            if (obstacles.Any(r => Inside(start.Stub, r) || Inside(finish.Stub, r))) { edges.Add(edge); diagnostics.Add(new(edge.Id, "An endpoint clearance region is obstructed.")); continue; }
            var spatial = new SpatialIndex<RectD>(obstacles.Select(r => (r, r)));
            bool Clear(PointD a, PointD b)
            {
                var bounds = RectD.FromPoints(a, b);
                foreach (var r in spatial.Query(bounds))
                {
                    if (Math.Abs(a.Y - b.Y) < 1e-7 && a.Y > r.Y + 1e-7 && a.Y < r.Bottom - 1e-7 && Math.Min(a.X, b.X) < r.Right - 1e-7 && Math.Max(a.X, b.X) > r.X + 1e-7) return false;
                    if (Math.Abs(a.X - b.X) < 1e-7 && a.X > r.X + 1e-7 && a.X < r.Right - 1e-7 && Math.Min(a.Y, b.Y) < r.Bottom - 1e-7 && Math.Max(a.Y, b.Y) > r.Y + 1e-7) return false;
                }
                return true;
            }
            var xs = obstacles.SelectMany(r => new[] { r.X, r.Right }).Concat([start.Stub.X, finish.Stub.X]).Distinct().Order().ToArray();
            var ys = obstacles.SelectMany(r => new[] { r.Y, r.Bottom }).Concat([start.Stub.Y, finish.Stub.Y]).Distinct().Order().ToArray();
            var initial = new State(Array.IndexOf(xs, start.Stub.X), Array.IndexOf(ys, start.Stub.Y), 0);
            var endX = Array.IndexOf(xs, finish.Stub.X); var endY = Array.IndexOf(ys, finish.Stub.Y);
            PointD Point(State state) => new(xs[state.X], ys[state.Y]);
            double Heuristic(State state) => Math.Abs(xs[state.X] - finish.Stub.X) + Math.Abs(ys[state.Y] - finish.Stub.Y);
            var costs = new Dictionary<State, double> { [initial] = 0 }; var previous = new Dictionary<State, State>(); var queue = new PriorityQueue<(State State, double Cost), double>();
            queue.Enqueue((initial, 0), Heuristic(initial)); State? goal = null; var visited = 0;
            while (queue.TryDequeue(out var item, out _))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = item.State;
                if (item.Cost > costs[current]) continue;
                if (++visited > options.MaximumVisitedStates) break;
                if (current.X == endX && current.Y == endY) { goal = current; break; }
                State[] neighbors = [new(current.X - 1, current.Y, 1), new(current.X + 1, current.Y, 1), new(current.X, current.Y - 1, 2), new(current.X, current.Y + 1, 2)];
                foreach (var next in neighbors)
                {
                    if (next.X < 0 || next.Y < 0 || next.X >= xs.Length || next.Y >= ys.Length || !Clear(Point(current), Point(next))) continue;
                    var cost = item.Cost + PointD.Distance(Point(current), Point(next)) + (current.Direction != 0 && current.Direction != next.Direction ? options.BendPenalty : 0);
                    if (cost >= costs.GetValueOrDefault(next, double.PositiveInfinity)) continue;
                    costs[next] = cost; previous[next] = current; queue.Enqueue((next, cost), cost + Heuristic(next));
                }
            }
            if (goal is null) { edges.Add(edge); diagnostics.Add(new(edge.Id, visited > options.MaximumVisitedStates ? "Routing search limit reached." : "No obstacle-free orthogonal route exists on the routing grid.")); continue; }
            var points = new List<PointD> { finish.Anchor }; var cursor = goal.Value;
            while (true) { points.Add(Point(cursor)); if (cursor == initial) break; cursor = previous[cursor]; }
            points.Add(start.Anchor); points.Reverse();
            var simplified = new List<PointD>();
            foreach (var p in points)
            {
                if (simplified.Count > 0 && PointD.Distance(simplified[^1], p) < 1e-7) continue;
                while (simplified.Count > 1)
                {
                    var a = simplified[^2]; var b = simplified[^1];
                    if (Math.Abs((b.X - a.X) * (p.Y - b.Y) - (b.Y - a.Y) * (p.X - b.X)) > 1e-7) break;
                    // Only remove a point if the path continues in the same direction.
                    if ((b.X - a.X) * (p.X - b.X) + (b.Y - a.Y) * (p.Y - b.Y) < 0) break;
                    simplified.RemoveAt(simplified.Count - 1);
                }
                simplified.Add(p);
            }
            // Preserve the selected cardinal endpoint direction through the first/last bend.
            edges.Add(edge with { Routing = EdgeRouting.Orthogonal, Bends = simplified.Skip(1).Take(Math.Max(0, simplified.Count - 2)).ToImmutableArray() });
        }
        return new(graph with { Edges = edges.ToImmutableArray() }, diagnostics);
    }
}
