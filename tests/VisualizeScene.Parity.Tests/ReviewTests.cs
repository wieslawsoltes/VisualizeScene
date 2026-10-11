using System.Collections.Immutable;
using VisualizeScene.Core;
using VisualizeScene.Editing;
using VisualizeScene.Layout;
using VisualizeScene.Serialization;
using VisualizeScene.Algorithms;

static class ReviewTests
{
    static void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
    static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    public static void Run(Action<string, Action> test)
    {
        test("Review: copying an edge to a group includes the group's descendants", () => {
            var graph = new GraphSnapshot([new("g", new(0, 0, 200, 200), IsGroup: true), new("child", new(40, 40, 50, 50), ParentId: "g"), new("other", new(500, 0, 50, 50))], [new("edge", "g", "other")], [new("port", "child", new(.5, .5))], [new("label", "port", "Label", new())]);
            var copy = GraphClipboard.Copy(graph, ["edge"]); GraphValidation.Validate(copy);
            Check(copy.Nodes.Length == 3 && copy.Ports.Length == 1 && copy.Labels.Length == 1);
        });
        test("Review: legacy group imports never inherit nested-child graphics", () => {
            const string text = """
                <graphml xmlns="http://graphml.graphdrawing.org/xmlns" xmlns:y="http://www.yworks.com/xml/graphml">
                  <key id="graphics" for="node" yfiles.type="nodegraphics"/>
                  <graph edgedefault="directed"><node id="parent"><graph edgedefault="directed">
                    <node id="child"><data key="graphics"><y:ShapeNode><y:Geometry x="777" y="888" width="50" height="60"/>
                      <y:Fill color="#FF0000"/><y:Shape type="ellipse"/><y:NodeLabel>Child</y:NodeLabel></y:ShapeNode></data></node>
                  </graph></node></graph>
                </graphml>
                """;
            var graph = GraphIO.FromGraphML(text).Graph.Index();
            Check(graph.Nodes["parent"].IsGroup && graph.Nodes["parent"].Bounds.X != 777 && graph.Nodes["parent"].Fill != "#FF0000");
            Check(graph.Nodes["child"].Bounds.X == 777 && graph.Nodes["child"].Shape == NodeShape.Ellipse);
        });
        test("Review: variable-width grid positions follow column maxima", () => {
            var nodes = Enumerable.Range(0, 10000).Select(i => new GraphNode("n" + i, new(0, 0, 50 + i % 100, 60))).ToImmutableArray();
            var result = new LayoutEngine().Calculate(new(nodes, [], [], []), new(LayoutKind.Grid, NodeSpacing: 20, LayerSpacing: 30)).Graph;
            double x = 0;
            for (var column = 0; column < 100; column++) {
                for (var row = 0; row < 100; row++) {
                    var n = result.Nodes[row * 100 + column]; Check(Math.Abs(n.Bounds.X - x) < 1e-8 && Math.Abs(n.Bounds.Y - row * 90) < 1e-8);
                }
                x += 50 + column + 20;
            }
        });
    }
}
