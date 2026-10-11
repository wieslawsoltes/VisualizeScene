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

public interface IConnectionPolicy
{
    ConnectionDecision CanStart(GraphIndex graph, ConnectionEnd endpoint, EdgeEnd end);
    ConnectionDecision Evaluate(GraphIndex graph, ConnectionRequest request);
}

/// <summary>Indexed endpoint rules. Only cycle detection traverses beyond endpoint adjacency; defaults inspect no edges.</summary>
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
        ArgumentNullException.ThrowIfNull(graph); ArgumentNullException.ThrowIfNull(endpoint);
        if (!Enum.IsDefined(end)) throw new ArgumentOutOfRangeException(nameof(end));
        if (Ports is null) return ConnectionDecision.Reject("Port configuration is missing.");
        if (endpoint.NodeId is null || !graph.Nodes.TryGetValue(endpoint.NodeId, out var node)) return ConnectionDecision.Reject("The endpoint node does not exist.");
        if (!graph.IsVisible(node.Id)) return ConnectionDecision.Reject("A hidden endpoint cannot be edited.");
        if (node.Locked) return ConnectionDecision.Reject("The endpoint node is locked.");
        if (endpoint.PortId is null) return RequirePorts ? ConnectionDecision.Reject("Choose an explicit port.") : ConnectionDecision.Accept;
        if (!graph.Ports.TryGetValue(endpoint.PortId, out var port) || port.NodeId != node.Id)
            return ConnectionDecision.Reject("The port does not belong to the endpoint node.");
        if (Ports.TryGetValue(port.Id, out var constraint))
        {
            if (constraint is null || constraint.Capacity < 0 || (constraint.Roles & ~PortRoles.Both) != 0)
                return ConnectionDecision.Reject("Invalid port constraint configuration.");
            var role = end == EdgeEnd.Source ? PortRoles.Source : PortRoles.Target;
            if (!constraint.Roles.HasFlag(role)) return ConnectionDecision.Reject("This port does not accept this connection direction.");
        }
        return ConnectionDecision.Accept;
    }
    private static IEnumerable<GraphEdge> Incident(GraphIndex graph, string nodeId, string? excluded)
    {
        foreach (var edge in graph.Outgoing(nodeId)) if (edge.Id != excluded) yield return edge;
        foreach (var edge in graph.Incoming(nodeId))
            if (edge.Id != excluded && edge.SourceId != nodeId) yield return edge; // self-loop once
    }
    public ConnectionDecision Evaluate(GraphIndex graph, ConnectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var source = CanStart(graph, request.Source, EdgeEnd.Source); if (!source.Allowed) return source;
        var target = CanStart(graph, request.Target, EdgeEnd.Target); if (!target.Allowed) return target;
        if (request.ReplacedEdgeId is not null && !graph.Edges.ContainsKey(request.ReplacedEdgeId))
            return ConnectionDecision.Reject("The reconnected edge no longer exists.");
        if (MaximumIncoming < 0 || MaximumOutgoing < 0) return ConnectionDecision.Reject("Degree limits cannot be negative.");
        var from = request.Source.NodeId; var to = request.Target.NodeId;
        if (!AllowSelfLoops && from == to) return ConnectionDecision.Reject("Self-loops are disabled.");
        if (!AllowParallelEdges || MaximumOutgoing != int.MaxValue)
        {
            var count = 0;
            foreach (var edge in Incident(graph, from, request.ReplacedEdgeId))
            {
                if (!AllowParallelEdges && (edge.SourceId == from && edge.TargetId == to || !edge.Directed && edge.SourceId == to && edge.TargetId == from))
                    return ConnectionDecision.Reject("A parallel connection already exists.");
                if (edge.SourceId == from || !edge.Directed && edge.TargetId == from) count++;
            }
            if (count >= MaximumOutgoing) return ConnectionDecision.Reject("The source has reached its outgoing connection limit.");
        }
        if (MaximumIncoming != int.MaxValue)
        {
            var count = 0;
            foreach (var edge in Incident(graph, to, request.ReplacedEdgeId))
                if (edge.TargetId == to || !edge.Directed && edge.SourceId == to) count++;
            if (count >= MaximumIncoming) return ConnectionDecision.Reject("The target has reached its incoming connection limit.");
        }
        bool CapacityAllows(string? portId, string owner)
        {
            if (portId is null || !Ports.TryGetValue(portId, out var constraint) || constraint.Capacity == int.MaxValue) return true;
            long count = (request.Source.PortId == portId ? 1 : 0) + (request.Target.PortId == portId ? 1 : 0);
            if (count > constraint.Capacity) return false;
            foreach (var edge in Incident(graph, owner, request.ReplacedEdgeId))
            {
                count += (edge.SourcePortId == portId ? 1 : 0) + (edge.TargetPortId == portId ? 1 : 0);
                if (count > constraint.Capacity) return false;
            }
            return true;
        }
        if (!CapacityAllows(request.Source.PortId, from) || request.Target.PortId != request.Source.PortId && !CapacityAllows(request.Target.PortId, to))
            return ConnectionDecision.Reject("The port has reached its connection capacity.");
        if (!AllowCycles)
        {
            var seen = new HashSet<string> { to }; var queue = new Queue<string>(); queue.Enqueue(to);
            while (queue.TryDequeue(out var id))
            {
                if (id == from) return ConnectionDecision.Reject("This connection would create a cycle.");
                foreach (var edge in graph.Outgoing(id))
                    if (edge.Id != request.ReplacedEdgeId && seen.Add(edge.TargetId)) queue.Enqueue(edge.TargetId);
                foreach (var edge in graph.Incoming(id))
                    if (!edge.Directed && edge.Id != request.ReplacedEdgeId && seen.Add(edge.SourceId)) queue.Enqueue(edge.SourceId);
            }
        }
        return ConnectionDecision.Accept;
    }
}
