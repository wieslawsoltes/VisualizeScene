using VisualizeScene.Core;

namespace VisualizeScene.Editing;

[Flags] public enum PortRoles { None = 0, Source = 1, Target = 2, Both = Source | Target }
public enum EdgeEnd { Source, Target }
public sealed record ConnectionEnd(string NodeId, string? PortId = null);
public sealed record ConnectionRequest(ConnectionEnd Source, ConnectionEnd Target, string? ReplacedEdgeId = null);
public sealed record ConnectionDecision(bool Allowed, string? Reason = null)
{
    public static ConnectionDecision Accept { get; } = new(true);
    public static ConnectionDecision Reject(string reason) => new(false, reason);
}
public sealed record PortConstraint(PortRoles Roles = PortRoles.Both, int Capacity = int.MaxValue, bool Movable = true);

/// <summary>Application-specific connection rules used by creation, candidate feedback and reconnection.</summary>
public interface IConnectionPolicy
{
    ConnectionDecision CanStart(GraphIndex graph, ConnectionEnd endpoint, EdgeEnd end);
    ConnectionDecision Evaluate(GraphIndex graph, ConnectionRequest request);
}

/// <summary>
/// Validates endpoint ownership, visibility, locks, port direction/capacity, degree limits,
/// parallel edges and directed cycles. Reconnection excludes the edge being replaced.
/// Rules are application configuration, not hidden mutable state in the graph document.
/// </summary>
public sealed class ConnectionPolicy : IConnectionPolicy
{
    public bool AllowSelfLoops { get; init; } = true;
    public bool AllowParallelEdges { get; init; } = true;
    public bool AllowCycles { get; init; } = true;
    public bool RequirePorts { get; init; }
    public int MaximumIncoming { get; init; } = int.MaxValue;
    public int MaximumOutgoing { get; init; } = int.MaxValue;
    public IReadOnlyDictionary<string, PortConstraint> Ports { get; init; } = new Dictionary<string, PortConstraint>();

    public ConnectionDecision CanStart(GraphIndex graph, ConnectionEnd endpoint, EdgeEnd end)
    {
        if (!graph.Nodes.TryGetValue(endpoint.NodeId, out var node)) return ConnectionDecision.Reject("The endpoint node does not exist.");
        if (!graph.IsVisible(node.Id)) return ConnectionDecision.Reject("A hidden endpoint cannot be edited.");
        if (node.Locked) return ConnectionDecision.Reject("The endpoint node is locked.");
        if (endpoint.PortId is null) return RequirePorts ? ConnectionDecision.Reject("Choose an explicit port.") : ConnectionDecision.Accept;
        if (!graph.Ports.TryGetValue(endpoint.PortId, out var port) || port.NodeId != node.Id)
            return ConnectionDecision.Reject("The port does not belong to the endpoint node.");
        if (Ports.TryGetValue(port.Id, out var constraint))
        {
            if (constraint.Capacity < 0 || (constraint.Roles & ~PortRoles.Both) != 0)
                return ConnectionDecision.Reject("Invalid port constraint configuration.");
            var role = end == EdgeEnd.Source ? PortRoles.Source : PortRoles.Target;
            if (!constraint.Roles.HasFlag(role)) return ConnectionDecision.Reject("This port does not accept this connection direction.");
        }
        return ConnectionDecision.Accept;
    }

    public ConnectionDecision Evaluate(GraphIndex graph, ConnectionRequest request)
    {
        var source = CanStart(graph, request.Source, EdgeEnd.Source); if (!source.Allowed) return source;
        var target = CanStart(graph, request.Target, EdgeEnd.Target); if (!target.Allowed) return target;
        if (request.ReplacedEdgeId is not null && !graph.Edges.ContainsKey(request.ReplacedEdgeId))
            return ConnectionDecision.Reject("The reconnected edge no longer exists.");
        if (MaximumIncoming < 0 || MaximumOutgoing < 0) return ConnectionDecision.Reject("Degree limits cannot be negative.");
        var from = request.Source.NodeId; var to = request.Target.NodeId;
        if (!AllowSelfLoops && from == to) return ConnectionDecision.Reject("Self-loops are disabled.");
        var edges = graph.Graph.Edges.Where(e => e.Id != request.ReplacedEdgeId).ToArray();
        if (!AllowParallelEdges && edges.Any(e => e.SourceId == from && e.TargetId == to || !e.Directed && e.SourceId == to && e.TargetId == from))
            return ConnectionDecision.Reject("A parallel connection already exists.");
        if (edges.Count(e => e.SourceId == from || !e.Directed && e.TargetId == from) >= MaximumOutgoing)
            return ConnectionDecision.Reject("The source has reached its outgoing connection limit.");
        if (edges.Count(e => e.TargetId == to || !e.Directed && e.SourceId == to) >= MaximumIncoming)
            return ConnectionDecision.Reject("The target has reached its incoming connection limit.");
        foreach (var portId in new[] { request.Source.PortId, request.Target.PortId }.OfType<string>().Distinct())
        {
            if (!Ports.TryGetValue(portId, out var constraint)) continue;
            var count = edges.Sum(e => (e.SourcePortId == portId ? 1 : 0) + (e.TargetPortId == portId ? 1 : 0));
            count += (request.Source.PortId == portId ? 1 : 0) + (request.Target.PortId == portId ? 1 : 0);
            if (count > constraint.Capacity) return ConnectionDecision.Reject("The port has reached its connection capacity.");
        }
        if (!AllowCycles)
        {
            var seen = new HashSet<string>(); var queue = new Queue<string>(); queue.Enqueue(to);
            while (queue.TryDequeue(out var id))
            {
                if (id == from) return ConnectionDecision.Reject("This connection would create a cycle.");
                if (!seen.Add(id)) continue;
                foreach (var e in graph.Outgoing(id)) if (e.Id != request.ReplacedEdgeId) queue.Enqueue(e.TargetId);
                foreach (var e in graph.Incoming(id)) if (!e.Directed && e.Id != request.ReplacedEdgeId) queue.Enqueue(e.SourceId);
            }
        }
        return ConnectionDecision.Accept;
    }
}
