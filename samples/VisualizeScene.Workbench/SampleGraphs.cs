using System.Collections.Immutable;
using VisualizeScene.Core;
using VisualizeScene.Layout;

namespace VisualizeScene.Workbench;

public static class SampleGraphs
{
    public static IReadOnlyList<string> Names { get; } = ["Order processing", "Organization chart", "Network topology", "Nested groups", "Routing obstacles", "Large graph · 1,000 nodes"];
    public static GraphSnapshot Create(string name) => name switch
    {
        "Organization chart" => Organization(), "Network topology" => Network(), "Nested groups" => Groups(),
        "Routing obstacles" => Obstacles(), "Large graph · 1,000 nodes" => Large(), _ => Flowchart()
    };
    public static GraphSnapshot Flowchart()
    {
        GraphNode Node(string id, double x, double y, string text, NodeShape shape = NodeShape.RoundedRectangle, string fill = "#DDEBFA", string stroke = "#4778A5", double width = 150, double height = 56)
            => new(id, new(x, y, width, height), text, shape, fill, stroke);
        GraphNode[] nodes = [
            Node("start", 255, 20, "Order received", NodeShape.Ellipse, "#DDEFE3", "#5D9770"),
            Node("validate", 255, 140, "Validate order"),
            Node("available", 260, 260, "In stock?", NodeShape.Diamond, "#FFF1CC", "#C39A38", 140, 85),
            Node("reserve", 80, 415, "Reserve inventory"),
            Node("purchase", 430, 415, "Purchase stock", NodeShape.Parallelogram, "#E9E2F3", "#9375AD"),
            Node("payment", 255, 555, "Process payment"),
            Node("approved", 260, 675, "Approved?", NodeShape.Diamond, "#FFF1CC", "#C39A38", 140, 85),
            Node("ship", 80, 825, "Prepare shipment", NodeShape.Hexagon, "#E2EDF0", "#678F99"),
            Node("notify", 430, 825, "Notify customer", NodeShape.RoundedRectangle, "#F9E5DB", "#BF8063"),
            Node("end", 80, 955, "Order fulfilled", NodeShape.Ellipse, "#DDEFE3", "#5D9770")
        ];
        GraphEdge Edge(string a, string b, string text = "") => new(a + "-" + b, a, b, text, Bends: []);
        return new(nodes.ToImmutableArray(), [Edge("start", "validate"), Edge("validate", "available"), Edge("available", "reserve", "Yes"), Edge("available", "purchase", "No"), Edge("reserve", "payment"), Edge("purchase", "payment"), Edge("payment", "approved"), Edge("approved", "ship", "Yes"), Edge("approved", "notify", "No"), Edge("ship", "end")], [], []);
    }
    public static GraphSnapshot Organization()
    {
        var people = new[] { "Executive\nLeadership", "Engineering", "Product & Design", "Operations", "Platform", "Applications", "Research", "Product strategy", "Experience design", "Customer success", "Finance", "Infrastructure", "Security", "Desktop", "Web", "Data science", "Prototyping" };
        var nodes = people.Select((text, i) => new GraphNode("person" + i, new(0, 0, i < 4 ? 155 : 135, 66), text, Fill: i == 0 ? "#C9DCF1" : i < 4 ? "#DDEBFA" : "#EEF4FA")).ToImmutableArray();
        int[] parents = [0, 0, 0, 1, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6];
        var edges = parents.Select((parent, i) => new GraphEdge("org" + i, "person" + parent, "person" + (i + 1), Directed: false)).ToImmutableArray();
        return new LayoutEngine().Calculate(new(nodes, edges, [], []), new(LayoutKind.Tree)).Graph;
    }
    public static GraphSnapshot Network()
    {
        var random = new Random(24);
        var nodes = Enumerable.Range(0, 40).Select(i => new GraphNode("host" + i, new(0, 0, i < 4 ? 100 : 70, i < 4 ? 70 : 44), i < 4 ? "Gateway " + (i + 1) : "Host " + i, i < 4 ? NodeShape.Hexagon : NodeShape.RoundedRectangle, i < 4 ? "#E9E2F3" : "#DDEBFA", i < 4 ? "#9375AD" : "#4778A5")).ToImmutableArray();
        var edges = new List<GraphEdge>();
        for (var i = 1; i < 40; i++) edges.Add(new("link" + i, "host" + (i < 4 ? i - 1 : i % 4), "host" + i, Weight: random.Next(1, 10), Directed: false, Routing: EdgeRouting.Polyline));
        edges.Add(new("backbone", "host3", "host0", Directed: false, Routing: EdgeRouting.Polyline));
        return new LayoutEngine().Calculate(new(nodes, edges.ToImmutableArray(), [], []), new(LayoutKind.Organic, Iterations: 120)).Graph;
    }
    public static GraphSnapshot Groups()
    {
        var d = new GraphDocument(); d.Replace(Flowchart(), clearHistory: true);
        d.Edit("Groups", edit =>
        {
            var validation = edit.Group(["start", "validate", "available"]); edit.Update(edit.Node(validation) with { Text = "Order intake" });
            var fulfillment = edit.Group(["reserve", "purchase"]); edit.Update(edit.Node(fulfillment) with { Text = "Inventory" });
            var nested = edit.Group(["ship", "end"]); edit.Update(edit.Node(nested) with { Text = "Shipping" });
            var operations = edit.Group([nested, "notify"]); edit.Update(edit.Node(operations) with { Text = "Customer operations", Fill = "#F4F0F8", Stroke = "#AC95BD" });
        });
        return new LayoutEngine().Calculate(d.Snapshot, new()).Graph;
    }
    public static GraphSnapshot Obstacles()
    {
        GraphSnapshot graph = new([
            new("source", new(0, 160, 120, 60), "Source", Fill: "#DDEFE3", Stroke: "#5D9770"),
            new("target", new(760, 160, 120, 60), "Target", Fill: "#E9E2F3", Stroke: "#9375AD"),
            new("a", new(220, 70, 130, 210), "Obstacle A", Fill: "#EDF0F4", Stroke: "#A6B1BE"),
            new("b", new(430, 220, 130, 210), "Obstacle B", Fill: "#EDF0F4", Stroke: "#A6B1BE"),
            new("c", new(500, -100, 130, 230), "Obstacle C", Fill: "#EDF0F4", Stroke: "#A6B1BE")
        ], [new("route", "source", "target", "Obstacle-aware routing")], [], []);
        return new OrthogonalRouter().Route(graph).Graph;
    }
    public static GraphSnapshot Large()
    {
        var nodes = Enumerable.Range(0, 1000).Select(i => new GraphNode("node" + i, new(i % 40 * 140, i / 40 * 90, 105, 46), "Node " + i)).ToImmutableArray();
        var edges = Enumerable.Range(1, 999).Select(i => new GraphEdge("edge" + i, "node" + (i - 1), "node" + i, Routing: EdgeRouting.Polyline)).ToImmutableArray();
        return new(nodes, edges, [], []);
    }
}
