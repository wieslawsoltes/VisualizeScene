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
Console.WriteLine("Package-only consumer passed; no source project references.");
