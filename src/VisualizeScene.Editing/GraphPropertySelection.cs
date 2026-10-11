using System.Collections.Immutable;
using VisualizeScene.Core;

namespace VisualizeScene.Editing;

/// <summary>A common property value; Count is the number of selected items to which this property applies.</summary>
public readonly record struct GraphPropertyValue<T>(int Count, bool IsMixed, T Value);

/// <summary>Only non-null fields are applied. An empty string, false, and zero are explicit values, not omission.</summary>
public sealed record GraphPropertyPatch(string? Text = null, NodeShape? Shape = null, string? Fill = null,
    string? Stroke = null, bool? Locked = null, double? Width = null, double? Height = null,
    EdgeRouting? Routing = null, bool? Directed = null, double? Weight = null, double? FontSize = null)
{
    internal void Validate()
    {
        if (Shape.HasValue && !Enum.IsDefined(Shape.Value) || Routing.HasValue && !Enum.IsDefined(Routing.Value))
            throw new ArgumentException("Unknown shape or routing option.");
        if (Fill is not null && !GraphValidation.ValidColor(Fill) || Stroke is not null && !GraphValidation.ValidColor(Stroke))
            throw new ArgumentException("Colors must use #RRGGBB.");
        if (Width.HasValue && (!double.IsFinite(Width.Value) || Width < 1) || Height.HasValue && (!double.IsFinite(Height.Value) || Height < 1))
            throw new ArgumentOutOfRangeException(nameof(Width), "Node dimensions must be finite and at least one.");
        if (Weight.HasValue && !double.IsFinite(Weight.Value)) throw new ArgumentOutOfRangeException(nameof(Weight));
        if (FontSize.HasValue && (!double.IsFinite(FontSize.Value) || FontSize <= 0 || FontSize > 512))
            throw new ArgumentOutOfRangeException(nameof(FontSize), "Font size must be greater than zero and at most 512.");
    }
}
public readonly record struct GraphPropertyEditResult(int ChangedItems, int SelectedItems);

/// <summary>One-pass, immutable property summary for a captured selection and graph revision. Safe for concurrent readers.</summary>
public sealed class GraphPropertySelection
{
    private struct Aggregate<T>
    {
        public int Count;
        public bool Mixed;
        public T Value;
        public void Add(T value)
        {
            if (Count == 0) Value = value;
            else if (!EqualityComparer<T>.Default.Equals(Value, value)) Mixed = true;
            Count++;
        }
        public readonly GraphPropertyValue<T> Result => new(Count, Mixed, Value);
    }
    public GraphSnapshot Snapshot { get; }
    public ImmutableArray<string> ItemIds { get; }
    public int ExaminedItems { get; }
    public int NodeCount { get; }
    public int EdgeCount { get; }
    public int PortCount { get; }
    public int LabelCount { get; }
    public GraphPropertyValue<string> Text { get; }
    public GraphPropertyValue<NodeShape> Shape { get; }
    public GraphPropertyValue<string> Fill { get; }
    public GraphPropertyValue<string> Stroke { get; }
    public GraphPropertyValue<bool> Locked { get; }
    public GraphPropertyValue<double> Width { get; }
    public GraphPropertyValue<double> Height { get; }
    public GraphPropertyValue<EdgeRouting> Routing { get; }
    public GraphPropertyValue<bool> Directed { get; }
    public GraphPropertyValue<double> Weight { get; }
    public GraphPropertyValue<double> FontSize { get; }

    public GraphPropertySelection(GraphSnapshot snapshot, IEnumerable<string> itemIds,
        SelectionKinds kinds = SelectionKinds.All, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(itemIds);
        cancellationToken.ThrowIfCancellationRequested();
        if ((kinds & ~SelectionKinds.All) != 0) throw new ArgumentOutOfRangeException(nameof(kinds));
        Snapshot = snapshot; var index = snapshot.Index(); var seen = new HashSet<string>(StringComparer.Ordinal);
        var selected = ImmutableArray.CreateBuilder<string>();
        Aggregate<string> text = new(), fill = new(), stroke = new();
        Aggregate<NodeShape> shape = new(); Aggregate<EdgeRouting> routing = new();
        Aggregate<bool> locked = new(), directed = new();
        Aggregate<double> width = new(), height = new(), weight = new(), fontSize = new();
        foreach (var id in itemIds)
        {
            cancellationToken.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(id);
            if (!seen.Add(id)) continue;
            ExaminedItems++;
            if (index.Nodes.TryGetValue(id, out var node))
            {
                if ((kinds & (node.IsGroup ? SelectionKinds.Groups : SelectionKinds.Nodes)) == 0) continue;
                NodeCount++; text.Add(node.Text); shape.Add(node.Shape); fill.Add(node.Fill); stroke.Add(node.Stroke);
                locked.Add(node.Locked); width.Add(node.Bounds.Width); height.Add(node.Bounds.Height);
            }
            else if (index.Edges.TryGetValue(id, out var edge))
            {
                if ((kinds & SelectionKinds.Edges) == 0) continue;
                EdgeCount++; text.Add(edge.Text); stroke.Add(edge.Stroke); routing.Add(edge.Routing); directed.Add(edge.Directed); weight.Add(edge.Weight);
            }
            else if (index.Labels.TryGetValue(id, out var label))
            {
                if ((kinds & SelectionKinds.Labels) == 0) continue;
                LabelCount++; text.Add(label.Text); fontSize.Add(label.FontSize);
            }
            else if (index.Ports.ContainsKey(id))
            {
                if ((kinds & SelectionKinds.Ports) == 0) continue;
                PortCount++;
            }
            else throw new ArgumentException("Selected item is absent: " + id, nameof(itemIds));
            selected.Add(id);
        }
        ItemIds = selected.ToImmutable(); Text = text.Result; Shape = shape.Result; Fill = fill.Result; Stroke = stroke.Result;
        Locked = locked.Result; Width = width.Result; Height = height.Result; Routing = routing.Result; Directed = directed.Result;
        Weight = weight.Result; FontSize = fontSize.Result;
    }

    /// <summary>One atomic edit. Position locks are checked before changes, so resizing locked nodes requires an earlier unlock.</summary>
    public GraphPropertyEditResult Apply(GraphDocument document, GraphPropertyPatch patch, long? expectedRevision = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document); ArgumentNullException.ThrowIfNull(patch);
        cancellationToken.ThrowIfCancellationRequested(); patch.Validate();
        if (!ReferenceEquals(document.Snapshot, Snapshot) || expectedRevision.HasValue && document.Revision != expectedRevision.Value)
            throw new InvalidOperationException("Property selection is stale; capture the current document again.");
        var revision = document.Revision; var index = Snapshot.Index(); var changed = 0;
        document.Edit("Edit selected properties", edit =>
        {
            foreach (var id in ItemIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (index.Nodes.TryGetValue(id, out var node))
                {
                    var bounds = node.Bounds with { Width = patch.Width ?? node.Bounds.Width, Height = patch.Height ?? node.Bounds.Height };
                    if (node.Locked && bounds != node.Bounds) throw new InvalidOperationException("A selected node has a locked position. No properties were changed.");
                    var next = node with { Text = patch.Text ?? node.Text, Shape = patch.Shape ?? node.Shape, Fill = patch.Fill ?? node.Fill,
                        Stroke = patch.Stroke ?? node.Stroke, Locked = patch.Locked ?? node.Locked, Bounds = bounds };
                    if (next != node) { edit.Update(next); changed++; }
                }
                else if (index.Edges.TryGetValue(id, out var edge))
                {
                    var next = edge with { Text = patch.Text ?? edge.Text, Stroke = patch.Stroke ?? edge.Stroke,
                        Routing = patch.Routing ?? edge.Routing, Directed = patch.Directed ?? edge.Directed, Weight = patch.Weight ?? edge.Weight };
                    if (next != edge) { edit.Update(next); changed++; }
                }
                else if (index.Labels.TryGetValue(id, out var label))
                {
                    var next = label with { Text = patch.Text ?? label.Text, FontSize = patch.FontSize ?? label.FontSize };
                    if (next != label) { edit.Update(next); changed++; }
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
        }, revision);
        return new(changed, ItemIds.Length);
    }
}
