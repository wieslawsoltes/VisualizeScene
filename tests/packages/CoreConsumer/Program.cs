using System.Xml.Linq;
using SkiaSharp;
using VisualizeScene.Algorithms;
using VisualizeScene.Core;
using VisualizeScene.Editing;
using VisualizeScene.Layout;
using VisualizeScene.Rendering.Skia;
using VisualizeScene.Serialization;

void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    Console.WriteLine("PASS " + message);
}
var document = new GraphDocument();
document.Edit("Package consumer", edit =>
{
    edit.Add(new GraphNode("a", new(0, 0, 100, 60), "Start"));
    edit.Add(new GraphNode("b", new(300, 0, 100, 60), "Finish"));
    edit.Add(new GraphEdge("ab", "a", "b"));
});
Check(GraphAlgorithms.ShortestPath(document.Snapshot, "a", "b").Cost == 1, "algorithm package executes");
var result = new LayoutEngine().Calculate(document.Snapshot, new LayoutOptions(LayoutKind.Hierarchical));
document.Replace(result.Graph, "Layout");
using var session = new EditorSession(document);
using var renderer = new GraphRenderer();
session.LabelSizeProvider = renderer.MeasureLabel;
var labelId = session.AddLabel("a", "Shaped annotation", new(700, 500));
var measured = renderer.MeasureLabel(document.Snapshot.Index().Labels[labelId]);
Check(measured.IsFinite && measured.X > 20 && measured.Y > 5, "native text shaping from renderer package");
var placement = new LabelPlacementEngine().Calculate(document.Snapshot, measure: renderer.MeasureLabel);
Check(placement.EligibleLabels == 1 && placement.Graph.Nodes == document.Snapshot.Nodes,
    "label placement API from the packaged layout library");
var snap = new ItemSnapIndex(document.Snapshot).Snap(new RectD(0, 0, 10, 10), new(), new HashSet<string>(), 1);
Check(snap.Correction.IsFinite, "item snapping API from the packaged editing library");
session.SelectAll(SelectionKinds.Nodes);
var selectionSnapshot = session.Selection;
session.Select(session.Selection);
Check(session.Selection.SetEquals(selectionSnapshot), "selection alias replacement from package");
session.SelectArea(new RectD(-10000, -10000, 20000, 20000), kinds: SelectionKinds.Labels);
Check(session.Selection.Contains(labelId), "measured area-selection API from package");
session.SelectNeighbors(NeighborDirection.Both);
Check(session.Selection.Contains("a") && session.Selection.Contains("b"), "topology selection API from package");
var view = new Viewport();
var navigation = new TouchNavigator(view);
navigation.Press(1, new(100, 100), 0); navigation.Press(2, new(200, 100), 1);
navigation.Move(1, new(50, 100)); navigation.Move(2, new(250, 100));
Check(view.Zoom == 2, "packaged two-contact navigation transforms the view");
Check(navigation.Release(1, new(50, 100), 10).Tracked && !navigation.Release(2, new(250, 100), 20).IsTap,
    "packaged pinch cannot become a tap");
session.NavigateBoundary();
Check(session.FocusedItemId is not null && session.FocusedItemBounds.HasValue, "packaged keyboard focus and bounds");
var before = document.Snapshot;
session.MoveLabel(labelId, new(850, 650)); document.Undo();
Check(GraphIO.ToJson(document.Snapshot) == GraphIO.ToJson(before), "editing package preserves exact undo");
var json = GraphIO.ToJson(document.Snapshot);
Check(GraphIO.ToJson(GraphIO.FromJson(json)) == json, "JSON package round trip");
var imported = GraphIO.FromGraphML(GraphIO.ToGraphML(document.Snapshot));
Check(GraphIO.ToJson(imported.Graph) == json, "GraphML package round trip");
Check(XDocument.Parse(SvgExporter.Export(document.Snapshot, renderer.MeasureLabel)).Root!.Name.LocalName == "svg", "SVG export from package");
var expected = GraphScene.Bounds(document.Snapshot, renderer.MeasureLabel).Inflate(40);
var png = renderer.ExportPng(document.Snapshot);
using var bitmap = SKBitmap.Decode(png);
Check(bitmap is not null && bitmap.Width == (int)Math.Ceiling(expected.Width) && bitmap.Height == (int)Math.Ceiling(expected.Height), "PNG includes far-away shaped label bounds");
using var surface = SKSurface.Create(new SKImageInfo(400, 300)) ?? throw new InvalidOperationException("No raster surface");
renderer.Render(surface.Canvas, document.Snapshot, new Viewport(), 400, 300);
Check(renderer.Statistics.VisibleNodes > 0, "caller-owned canvas rendering");
renderer.PictureCacheCapacity = 2;
renderer.Render(surface.Canvas, document.Snapshot, new Viewport(), 400, 300);
Check(renderer.PictureStatistics.Retained <= 2, "packaged native picture budget");
var isolatedEdit = new GraphEdit(document.Snapshot);
var originalBounds = isolatedEdit.Node("a").Bounds;
isolatedEdit.SetNodeBounds("a", originalBounds.Move(new PointD(12, 15)));
Check(isolatedEdit.Node("a").Bounds.X == originalBounds.X + 12, "packaged bounds editing API");
var removal = isolatedEdit.RemoveRange(["a", "b"]);
var removedGraph = isolatedEdit.Build();
GraphValidation.Validate(removedGraph);
Check(removal.Nodes == 2 && removedGraph.Nodes.IsEmpty && removedGraph.Edges.IsEmpty && removedGraph.Labels.IsEmpty,
    "packaged bulk removal cascades dependents");
var searchResult = new GraphSearchIndex(document.Snapshot).Find("Start", new(MatchCase: true));
Check(searchResult.Matches.Length == 1 && searchResult.Matches[0].ItemId == "a", "packaged graph text search");
var searchSnapshot = document.Snapshot;
Check(searchResult.Replace(document, "Begin", expectedRevision: document.Revision).ChangedItems == 1, "packaged atomic text replacement");
document.Undo(); Check(ReferenceEquals(searchSnapshot, document.Snapshot), "packaged replacement preserves exact undo");
Console.WriteLine("Package-only consumer passed; no source project references.");
