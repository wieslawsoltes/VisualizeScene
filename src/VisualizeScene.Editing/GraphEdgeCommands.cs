using System.Collections.Immutable;
using VisualizeScene.Core;

namespace VisualizeScene.Editing;

public enum EdgeCommand { Reverse, ClearBends }
public readonly record struct GraphEdgeCommandState(int SelectedEdges, int EditableEdges, int EdgesWithBends)
{
    public static GraphEdgeCommandState Calculate(GraphSnapshot graph, IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(graph); ArgumentNullException.ThrowIfNull(ids);
        var index = graph.Index(); var seen = new HashSet<string>(StringComparer.Ordinal);
        var selected = 0; var editable = 0; var bent = 0;
        foreach (var id in ids)
        {
            ArgumentNullException.ThrowIfNull(id);
            if (!seen.Add(id) || !index.Edges.TryGetValue(id, out var edge)) continue;
            selected++;
            if (!GraphEdgeCommands.CanEdit(index, edge)) continue;
            editable++; if (!edge.Bends.IsDefaultOrEmpty) bent++;
        }
        return new(selected, editable, bent);
    }
}
public sealed record GraphEdgeCommandResult(GraphSnapshot Graph, int EligibleEdges, int ChangedEdges, int ExaminedEdges);

/// <summary>Atomic edge reversal or bend removal. Only selected edges with visible, unlocked endpoints are eligible.</summary>
public static class GraphEdgeCommands
{
    private static readonly IConnectionPolicy DefaultPolicy = new ConnectionPolicy();
    public static bool CanEdit(GraphIndex index, GraphEdge edge) => index.IsVisible(edge.SourceId) && index.IsVisible(edge.TargetId) &&
        !index.Nodes[edge.SourceId].Locked && !index.Nodes[edge.TargetId].Locked && GraphScene.IsEdgeVisible(index, edge);

    public static GraphEdgeCommandResult Calculate(GraphSnapshot graph, IEnumerable<string> ids, EdgeCommand command,
        IConnectionPolicy? policy = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph); ArgumentNullException.ThrowIfNull(ids);
        if (!Enum.IsDefined(command)) throw new ArgumentOutOfRangeException(nameof(command));
        cancellationToken.ThrowIfCancellationRequested();
        var index = graph.Index(); var seen = new HashSet<string>(StringComparer.Ordinal);
        var changes = new Dictionary<string, GraphEdge>(StringComparer.Ordinal); var eligible = 0;
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(id);
            if (!seen.Add(id)) continue;
            if (!index.Edges.TryGetValue(id, out var edge))
            {
                if (!index.Nodes.ContainsKey(id) && !index.Ports.ContainsKey(id) && !index.Labels.ContainsKey(id))
                    throw new ArgumentException("Selected item is absent: " + id, nameof(ids));
                continue;
            }
            if (!CanEdit(index, edge)) continue;
            eligible++;
            if (command == EdgeCommand.ClearBends)
            {
                if (!edge.Bends.IsDefaultOrEmpty) changes.Add(id, edge with { Bends = [] });
                continue;
            }
            var bends = ReverseBends(edge.Bends, cancellationToken);
            if (edge.SourceId == edge.TargetId && edge.SourcePortId == edge.TargetPortId && bends == edge.Bends) continue;
            // Style, direction flag, labels and weight stay attached to their logical edge.
            changes.Add(id, edge with { SourceId = edge.TargetId, TargetId = edge.SourceId,
                SourcePortId = edge.TargetPortId, TargetPortId = edge.SourcePortId, Bends = bends });
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (changes.Count == 0) return new(graph, eligible, 0, 0);
        var edges = graph.Edges.ToBuilder();
        for (var i = 0; i < edges.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (changes.TryGetValue(edges[i].Id, out var replacement)) edges[i] = replacement;
        }
        var result = graph with { Edges = edges.ToImmutable() }; GraphValidation.Validate(result);
        if (command == EdgeCommand.Reverse)
        {
            // All callbacks see the same FINAL topology, excluding the candidate via ReplacedEdgeId.
            // Sequential original-graph validation falsely rejects reversing both sides of a pair.
            var finalIndex = result.Index(); policy ??= DefaultPolicy;
            foreach (var edge in changes.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var decision = policy.Evaluate(finalIndex, new(new(edge.SourceId, edge.SourcePortId), new(edge.TargetId, edge.TargetPortId), edge.Id));
                if (decision is null || !decision.Allowed)
                    throw new InvalidOperationException("Cannot reverse edge '" + edge.Id + "': " + (decision?.Reason ?? "Connection policy rejected the operation."));
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(result, eligible, changes.Count, edges.Count);
    }
    private static ImmutableArray<PointD> ReverseBends(ImmutableArray<PointD> bends, CancellationToken token)
    {
        if (bends.IsDefaultOrEmpty || bends.Length < 2) return bends;
        var different = false;
        for (var i = 0; i < bends.Length / 2; i++)
        {
            token.ThrowIfCancellationRequested();
            if (bends[i] != bends[bends.Length - i - 1]) { different = true; break; }
        }
        if (!different) return bends;
        var reversed = ImmutableArray.CreateBuilder<PointD>(bends.Length);
        for (var i = bends.Length - 1; i >= 0; i--) { token.ThrowIfCancellationRequested(); reversed.Add(bends[i]); }
        return reversed.MoveToImmutable();
    }
}
