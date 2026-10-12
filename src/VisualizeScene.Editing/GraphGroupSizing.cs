using VisualizeScene.Core;

namespace VisualizeScene.Editing;

/// <summary>World-unit frame insets and size constraints. The header is added above the normal padding.</summary>
public sealed record GroupSizingOptions(double Padding = 28, double HeaderHeight = 20,
    double MinimumWidth = 80, double MinimumHeight = 60, bool IncludeLabels = true, bool EnlargeOnly = false)
{
    public void Validate()
    {
        if (!double.IsFinite(Padding) || Padding < 0 || !double.IsFinite(HeaderHeight) || HeaderHeight < 0 ||
            !double.IsFinite(MinimumWidth) || MinimumWidth < 1 || !double.IsFinite(MinimumHeight) || MinimumHeight < 1 ||
            !double.IsFinite(2 * Padding + HeaderHeight))
            throw new ArgumentOutOfRangeException(nameof(Padding), "Group insets and minimum dimensions must be finite and nonnegative; dimensions must be at least one.");
    }
}

public sealed record GraphGroupSizeResult(GraphSnapshot Graph, int EligibleGroups, int ResizedGroups,
    int ExaminedNodes, int ExaminedLabels, int MeasuredLabels, int MaximumStackDepth);

/// <summary>Fits selected expanded group frames to visible descendant nodes and optional node/port annotations, without moving contents.</summary>
/// <remarks>Only selected, visible, unlocked, nonempty groups are resized. Selected nested groups are processed bottom-up once.
/// Collapsed groups contribute their frame and annotations, not hidden children. Edges, edge captions, adorners and the fitted group's
/// own annotations are outside this node-envelope contract. Group-owned ports/annotations follow the new frame normally.</remarks>
public static class GraphGroupSizing
{
    private struct Extent
    {
        public bool HasValue;
        public RectD Value;
        public void Add(RectD rectangle)
        {
            ValidateBounds(rectangle);
            var next = HasValue ? RectD.Union(Value, rectangle) : rectangle;
            ValidateBounds(next); Value = next; HasValue = true;
        }
    }
    private struct Frame(GraphNode node)
    {
        public GraphNode Node = node;
        public int Child;
        public Extent Content;
    }
    private static void ValidateBounds(RectD bounds)
    {
        if (!bounds.IsValid || !double.IsFinite(bounds.Right) || !double.IsFinite(bounds.Bottom))
            throw new ArgumentException("Group content exceeds the finite coordinate range.");
    }
    public static bool CanFit(GraphIndex index, GraphNode node) => node.IsGroup && !node.Collapsed && !node.Locked &&
        index.IsVisible(node.Id) && index.Children(node.Id).Count != 0;

    public static GraphGroupSizeResult Calculate(GraphSnapshot graph, IEnumerable<string> groupIds,
        GroupSizingOptions? options = null, Func<GraphLabel, PointD>? measure = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph); ArgumentNullException.ThrowIfNull(groupIds);
        cancellationToken.ThrowIfCancellationRequested(); options ??= new(); options.Validate();
        var index = graph.Index(); var selected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in groupIds)
        {
            cancellationToken.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(id);
            if (index.Nodes.TryGetValue(id, out var node)) { if (CanFit(index, node)) selected.Add(id); }
            else if (!index.Edges.ContainsKey(id) && !index.Ports.ContainsKey(id) && !index.Labels.ContainsKey(id))
                throw new ArgumentException("Selected item is absent: " + id, nameof(groupIds));
        }
        if (selected.Count == 0) return new(graph, 0, 0, 0, 0, 0, 0);
        // Find disjoint traversal roots. Memoization prevents quadratic ancestor walks in deep selected hierarchies.
        var roots = new List<string>(); var memo = new Dictionary<string, bool>(StringComparer.Ordinal); var ancestry = new List<string>();
        foreach (var id in selected)
        {
            var parent = index.Nodes[id].ParentId; var covered = false; ancestry.Clear();
            while (parent is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (selected.Contains(parent)) { covered = true; break; }
                if (memo.TryGetValue(parent, out covered)) break;
                ancestry.Add(parent); parent = index.Nodes[parent].ParentId;
            }
            foreach (var ancestor in ancestry) memo[ancestor] = covered;
            if (!covered) roots.Add(id);
        }
        var labels = new Dictionary<string, List<GraphLabel>>(StringComparer.Ordinal);
        var examinedLabels = 0;
        if (options.IncludeLabels)
        {
            foreach (var label in graph.Labels)
            {
                cancellationToken.ThrowIfCancellationRequested(); examinedLabels++;
                var owner = index.Ports.TryGetValue(label.OwnerId, out var port) ? port.NodeId : label.OwnerId;
                if (!index.Nodes.TryGetValue(owner, out var ownerNode) || !index.IsVisible(owner)) continue;
                // Only strict descendants influence a selected frame. Reuse the ancestry memo
                // from root discovery; top-level unrelated labels need no per-owner storage.
                var parent = ownerNode.ParentId; var included = false; ancestry.Clear();
                while (parent is not null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (selected.Contains(parent)) { included = true; break; }
                    if (memo.TryGetValue(parent, out included)) break;
                    ancestry.Add(parent); parent = index.Nodes[parent].ParentId;
                }
                foreach (var ancestor in ancestry) memo[ancestor] = included;
                if (!included) continue;
                if (!labels.TryGetValue(owner, out var list)) labels.Add(owner, list = []);
                list.Add(label);
            }
        }
        measure ??= GraphScene.EstimateLabelSize;
        var changes = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
        var stack = new Stack<Frame>(); var examinedNodes = 0; var measuredLabels = 0; var maximumDepth = 0;
        foreach (var root in roots)
        {
            stack.Push(new(index.Nodes[root]));
            while (stack.Count != 0)
            {
                cancellationToken.ThrowIfCancellationRequested(); maximumDepth = Math.Max(maximumDepth, stack.Count);
                var frame = stack.Pop(); var children = index.Children(frame.Node.Id);
                if (!frame.Node.Collapsed && frame.Child < children.Count)
                {
                    var child = children[frame.Child++]; stack.Push(frame); stack.Push(new(child)); continue;
                }
                examinedNodes++; var node = frame.Node;
                if (selected.Contains(node.Id) && frame.Content.HasValue)
                {
                    var content = frame.Content.Value;
                    var fitted = new RectD(content.X - options.Padding, content.Y - options.Padding - options.HeaderHeight,
                        Math.Max(options.MinimumWidth, content.Width + 2 * options.Padding),
                        Math.Max(options.MinimumHeight, content.Height + 2 * options.Padding + options.HeaderHeight));
                    ValidateBounds(fitted);
                    if (options.EnlargeOnly) { fitted = RectD.Union(node.Bounds, fitted); ValidateBounds(fitted); }
                    if (fitted != node.Bounds) { node = node with { Bounds = fitted }; changes.Add(node.Id, node); }
                }
                // The root's own annotation does not influence its frame. Descendant annotations use UPDATED node/port anchors.
                if (stack.Count == 0) continue;
                var envelope = new Extent(); envelope.Add(node.Bounds);
                if (frame.Content.HasValue) envelope.Add(frame.Content.Value);
                if (labels.TryGetValue(node.Id, out var ownedLabels))
                {
                    foreach (var label in ownedLabels)
                    {
                        cancellationToken.ThrowIfCancellationRequested(); var size = measure(label); measuredLabels++;
                        if (!size.IsFinite || size.X < 0 || size.Y < 0) throw new ArgumentException("Invalid measured group annotation size.", nameof(measure));
                        var anchor = node.Bounds.Center;
                        if (index.Ports.TryGetValue(label.OwnerId, out var port))
                            anchor = new(node.Bounds.X + port.RelativeLocation.X * node.Bounds.Width,
                                node.Bounds.Y + port.RelativeLocation.Y * node.Bounds.Height);
                        var center = anchor + label.Offset;
                        envelope.Add(new(center.X - size.X / 2, center.Y - size.Y / 2, size.X, size.Y));
                    }
                }
                var parentFrame = stack.Pop(); parentFrame.Content.Add(envelope.Value); stack.Push(parentFrame);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (changes.Count == 0) return new(graph, selected.Count, 0, examinedNodes, examinedLabels, measuredLabels, maximumDepth);
        var nodes = graph.Nodes.ToBuilder();
        for (var i = 0; i < nodes.Count; i++)
        { cancellationToken.ThrowIfCancellationRequested(); if (changes.TryGetValue(nodes[i].Id, out var replacement)) nodes[i] = replacement; }
        var result = graph with { Nodes = nodes.ToImmutable() };
        GraphValidation.Validate(result); cancellationToken.ThrowIfCancellationRequested();
        return new(result, selected.Count, changes.Count, examinedNodes, examinedLabels, measuredLabels, maximumDepth);
    }
}
