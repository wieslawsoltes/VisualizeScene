using VisualizeScene.Core;

namespace VisualizeScene.Editing;

public sealed partial class EditorSession
{
    private IConnectionPolicy connectionPolicy = new ConnectionPolicy();
    public IConnectionPolicy ConnectionPolicy
    {
        get => connectionPolicy;
        set { ArgumentNullException.ThrowIfNull(value); connectionPolicy = value; CancelGesture(); }
    }
    public ConnectionEnd? ConnectionCandidate { get; private set; }
    public ConnectionDecision CandidateDecision { get; private set; } = ConnectionDecision.Accept;
    public PointD? CandidatePosition { get; private set; }

    /// <summary>Finds explicit ports before node interiors. Folded child endpoints are never candidates.</summary>
    public ConnectionEnd? FindConnectionCandidate(PointD world)
    {
        var tolerance = 9 / Viewport.Zoom; QueryHitCandidates(world, tolerance);
        string? best = null; var distance = double.PositiveInfinity; var order = int.MaxValue;
        foreach (var id in hitPorts)
        {
            var d = PointD.Distance(index.PortLocation(id), world);
            if (d <= tolerance && (d < distance || d == distance && portOrder[id] < order))
            { best = id; distance = d; order = portOrder[id]; }
        }
        if (best is not null) return new(index.Ports[best].NodeId, best);
        GraphNode? hit = null; var top = -1;
        foreach (var id in hitNodes)
        {
            var n = index.Nodes[id];
            if (nodeOrder[id] > top && Geometry.HitNode(n, world) &&
                (!n.IsGroup || n.Collapsed || world.Y <= n.Bounds.Y + 25 || world.X <= n.Bounds.X + 6 || world.X >= n.Bounds.Right - 6 || world.Y >= n.Bounds.Bottom - 6))
            { hit = n; top = nodeOrder[id]; }
        }
        return hit is null ? null : new(hit.Id);
    }

    private PointD CandidatePoint(ConnectionEnd candidate, PointD toward) => candidate.PortId is { } port
        ? index.PortLocation(port) : Geometry.ClipToNode(index.Nodes[candidate.NodeId], toward);

    private void ShowCandidate(ConnectionEnd? candidate, ConnectionDecision decision, PointD world)
    {
        ConnectionCandidate = candidate; CandidateDecision = decision;
        CandidatePosition = candidate?.PortId is { } port ? index.PortLocation(port) : candidate is null ? null : world;
        if (!decision.Allowed && decision.Reason is { } reason) StatusChanged?.Invoke(this, reason);
    }

    private void CreateEdgeContact(PointD world)
    {
        var candidate = FindConnectionCandidate(world);
        if (candidate is null) { CancelGesture(); return; }
        if (sourceNode is null)
        {
            var decision = connectionPolicy.CanStart(index, candidate, EdgeEnd.Source);
            ShowCandidate(candidate, decision, world);
            if (decision.Allowed)
            {
                sourceNode = candidate.NodeId; sourcePort = candidate.PortId;
                EdgePreview = (candidate.PortId is null ? index.Nodes[candidate.NodeId].Bounds.Center : index.PortLocation(candidate.PortId), world);
            }
            Invalidate(); return;
        }
        var request = new ConnectionRequest(new(sourceNode, sourcePort), candidate);
        var result = connectionPolicy.Evaluate(index, request); ShowCandidate(candidate, result, world);
        if (!result.Allowed) { Invalidate(); return; }
        var edgeId = GraphEdit.NewId("e");
        CancelGesture(false);
        Document.Edit("Create edge", edit => edit.Add(new GraphEdge(edgeId, request.Source.NodeId, request.Target.NodeId,
            Bends: [], Stroke: edgeDefaults.Stroke, Routing: edgeDefaults.Routing,
            SourcePortId: request.Source.PortId, TargetPortId: request.Target.PortId) { Style = edgeDefaults.Style }));
        Select([edgeId]);
    }

    private void PreviewConnection(PointD world, GraphEdge? reconnect = null, EdgeEnd end = EdgeEnd.Target)
    {
        var candidate = FindConnectionCandidate(world);
        ConnectionRequest? request = candidate is null ? null : reconnect is null
            ? new(new(sourceNode!, sourcePort), candidate)
            : end == EdgeEnd.Source
                ? new(candidate, new(reconnect.TargetId, reconnect.TargetPortId), reconnect.Id)
                : new(new(reconnect.SourceId, reconnect.SourcePortId), candidate, reconnect.Id);
        var decision = request is null ? ConnectionDecision.Reject("Drop on a node or port.") : connectionPolicy.Evaluate(index, request);
        ShowCandidate(candidate, decision, world);
        var fixedPoint = reconnect is null ? EdgePreview!.Value.Start
            : end == EdgeEnd.Source ? paths[reconnect.Id][^1] : paths[reconnect.Id][0];
        EdgePreview = (fixedPoint, candidate is null ? world : CandidatePoint(candidate, fixedPoint));
        preview = null;
        if (reconnect is not null && request is not null && decision.Allowed)
        {
            var edit = new GraphEdit(Document.Snapshot);
            edit.Update(reconnect with { SourceId = request.Source.NodeId, SourcePortId = request.Source.PortId,
                TargetId = request.Target.NodeId, TargetPortId = request.Target.PortId });
            preview = edit.Build(); EdgePreview = null;
        }
        Invalidate();
    }

    /// <summary>Reconnects an endpoint as one validated, undoable operation. Invalid requests do not mutate the graph.</summary>
    public ConnectionDecision Reconnect(string edgeId, EdgeEnd end, ConnectionEnd endpoint)
    {
        var edge = index.Edges[edgeId];
        var request = end == EdgeEnd.Source
            ? new ConnectionRequest(endpoint, new(edge.TargetId, edge.TargetPortId), edgeId)
            : new ConnectionRequest(new(edge.SourceId, edge.SourcePortId), endpoint, edgeId);
        var decision = connectionPolicy.Evaluate(index, request);
        if (!decision.Allowed) return decision;
        Document.Edit("Reconnect " + end.ToString().ToLowerInvariant(), edit => edit.Update(edge with
        {
            SourceId = request.Source.NodeId, SourcePortId = request.Source.PortId,
            TargetId = request.Target.NodeId, TargetPortId = request.Target.PortId
        }));
        return decision;
    }

    public string AddPort(string nodeId, PointD world)
    {
        var node = index.Nodes[nodeId];
        if (node.Locked || !index.IsVisible(nodeId)) throw new InvalidOperationException("Cannot add a port to a locked or hidden node.");
        var id = GraphEdit.NewId("p"); var location = RelativePortLocation(node, world);
        Document.Edit("Add port", edit => edit.Add(new GraphPort(id, nodeId, location))); Select([id]); return id;
    }

    public void MovePort(string portId, PointD world)
    {
        var port = index.Ports[portId];
        if (!CanMovePort(port)) throw new InvalidOperationException("This port cannot be moved.");
        var location = RelativePortLocation(index.Nodes[port.NodeId], world);
        Document.Edit("Move port", edit => edit.Update(port with { RelativeLocation = location }));
    }

    private static PointD RelativePortLocation(GraphNode node, PointD world)
    {
        if (!world.IsFinite) throw new ArgumentException("Port coordinates must be finite.", nameof(world));
        return new(Math.Clamp((world.X - node.Bounds.X) / node.Bounds.Width, 0, 1),
            Math.Clamp((world.Y - node.Bounds.Y) / node.Bounds.Height, 0, 1));
    }

    private bool CanMovePort(GraphPort port) => !index.Nodes[port.NodeId].Locked && index.IsVisible(port.NodeId) &&
        !(connectionPolicy is ConnectionPolicy policy && policy.Ports.TryGetValue(port.Id, out var rule) && !rule.Movable);
}
