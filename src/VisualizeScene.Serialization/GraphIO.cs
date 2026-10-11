using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using VisualizeScene.Core;

namespace VisualizeScene.Serialization;

public sealed record GraphFile(int Version, GraphSnapshot Graph);
public sealed record GraphImportResult(GraphSnapshot Graph, IReadOnlyList<string> Diagnostics);
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true, IgnoreReadOnlyProperties = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(GraphFile))]
[JsonSerializable(typeof(GraphSnapshot))]
[JsonSerializable(typeof(GraphNode))]
[JsonSerializable(typeof(GraphEdge))]
[JsonSerializable(typeof(GraphPort))]
[JsonSerializable(typeof(ImmutableArray<GraphLabel>))]
public partial class GraphJsonContext : JsonSerializerContext;

public static class GraphIO
{
    public const int MaximumInputCharacters = 64 * 1024 * 1024;
    private static GraphSnapshot Normalize(GraphSnapshot graph) => graph with { Edges = graph.Edges.Select(e => e.Bends.IsDefault ? e with { Bends = [] } : e).ToImmutableArray() };
    public static string ToJson(GraphSnapshot graph)
    {
        GraphValidation.Validate(graph);
        return JsonSerializer.Serialize(new GraphFile(1, Normalize(graph)), GraphJsonContext.Default.GraphFile);
    }
    public static GraphSnapshot FromJson(string text)
    {
        CheckInput(text);
        var file = JsonSerializer.Deserialize(text, GraphJsonContext.Default.GraphFile) ?? throw new FormatException("The graph file is empty.");
        if (file.Version != 1) throw new NotSupportedException($"Graph format version {file.Version} is not supported.");
        if (file.Graph is null) throw new FormatException("Missing graph.");
        GraphValidation.Validate(file.Graph);
        return Normalize(file.Graph);
    }
    private static void CheckInput(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaximumInputCharacters) throw new FormatException("Input exceeds the 64 MiB character limit.");
    }
    private static string Required(XElement element, string attribute) => (string?)element.Attribute(attribute) ?? throw new FormatException($"Missing {attribute} on {element.Name.LocalName}.");
    private static double Number(string? value, double fallback) => value is null ? fallback : double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
    public static GraphImportResult FromGraphML(string text)
    {
        CheckInput(text);
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumInputCharacters };
        using var reader = XmlReader.Create(new StringReader(text), settings);
        var document = XDocument.Load(reader);
        var root = document.Root ?? throw new FormatException("Missing GraphML root.");
        if (root.Name.LocalName != "graphml") throw new FormatException("Expected a GraphML document.");
        var ns = root.Name.Namespace;
        if (ns.NamespaceName != "" && ns.NamespaceName != "http://graphml.graphdrawing.org/xmlns") throw new FormatException("Unknown GraphML namespace.");
        var keyNames = root.Elements(ns + "key").ToDictionary(k => Required(k, "id"), k => (string?)k.Attribute("attr.name") ?? (string?)k.Attribute("yfiles.type") ?? Required(k, "id"));
        string? Data(XElement item, string name) => item.Elements(ns + "data").FirstOrDefault(d => keyNames.GetValueOrDefault((string?)d.Attribute("key") ?? "") == name)?.Value;
        var nodes = new List<GraphNode>(); var edges = new List<GraphEdge>(); var ports = new List<GraphPort>(); var diagnostics = new List<string>();
        var portIds = new Dictionary<(string Node, string Port), string>();
        var nativeLabels = root.Element(ns + "graph") is { } main ? Data(main, "visualizescene.labels") : null;
        var labels = nativeLabels is null ? ImmutableArray<GraphLabel>.Empty : JsonSerializer.Deserialize(nativeLabels, GraphJsonContext.Default.ImmutableArrayGraphLabel);
        foreach (var item in root.Descendants(ns + "node"))
        {
            if (item.Ancestors(ns + "node").Take(129).Count() > 128) throw new NotSupportedException("GraphML nesting is limited to 128 levels.");
            var id = Required(item, "id"); var parentId = (string?)item.Ancestors(ns + "node").FirstOrDefault()?.Attribute("id"); var native = Data(item, "visualizescene.node");
            GraphNode node;
            if (native is not null)
            {
                node = JsonSerializer.Deserialize(native, GraphJsonContext.Default.GraphNode) ?? throw new FormatException("Invalid native node data.");
                node = node with { Id = id, ParentId = parentId, IsGroup = node.IsGroup || item.Element(ns + "graph") is not null };
            }
            else
            {
                var graphics = item.Elements(ns + "data").SelectMany(d => d.Descendants()).ToArray();
                var geometry = graphics.FirstOrDefault(e => e.Name.LocalName == "Geometry");
                var fill = (string?)graphics.FirstOrDefault(e => e.Name.LocalName == "Fill")?.Attribute("color");
                var stroke = (string?)graphics.FirstOrDefault(e => e.Name.LocalName == "BorderStyle")?.Attribute("color");
                var label = graphics.FirstOrDefault(e => e.Name.LocalName == "NodeLabel")?.Value ?? Data(item, "label") ?? id;
                var shape = (string?)graphics.FirstOrDefault(e => e.Name.LocalName == "Shape")?.Attribute("type");
                var kind = shape?.ToLowerInvariant() switch { "ellipse" => NodeShape.Ellipse, "diamond" => NodeShape.Diamond, "hexagon" => NodeShape.Hexagon, "parallelogram" => NodeShape.Parallelogram, "rectangle" => NodeShape.Rectangle, _ => NodeShape.RoundedRectangle };
                var b = new RectD(Number((string?)geometry?.Attribute("x"), nodes.Count % 5 * 170), Number((string?)geometry?.Attribute("y"), nodes.Count / 5 * 100), Number((string?)geometry?.Attribute("width"), 120), Number((string?)geometry?.Attribute("height"), 54));
                node = new(id, b, label, kind, GraphValidation.ValidColor(fill) ? fill! : "#DDEBFA", GraphValidation.ValidColor(stroke) ? stroke! : "#4778A5", parentId, item.Element(ns + "graph") is not null);
            }
            nodes.Add(node);
            foreach (var port in item.Elements(ns + "port"))
            {
                var name = Required(port, "name"); var nativePort = Data(port, "visualizescene.port");
                var p = nativePort is null ? new GraphPort(id + ":" + name, id, new(.5, .5)) : JsonSerializer.Deserialize(nativePort, GraphJsonContext.Default.GraphPort) ?? throw new FormatException("Invalid port data.");
                p = p with { NodeId = id }; ports.Add(p); portIds.Add((id, name), p.Id);
                if (nativePort is null) diagnostics.Add($"Port '{name}' has no supported geometry and was placed at the node center.");
            }
        }
        var edgeNumber = 0;
        foreach (var item in root.Descendants(ns + "edge"))
        {
            var source = Required(item, "source"); var target = Required(item, "target"); var id = (string?)item.Attribute("id") ?? "edge-" + edgeNumber; edgeNumber++;
            var native = Data(item, "visualizescene.edge"); GraphEdge edge;
            if (native is not null) edge = JsonSerializer.Deserialize(native, GraphJsonContext.Default.GraphEdge) ?? throw new FormatException("Invalid native edge data.");
            else
            {
                var directed = (string?)item.Attribute("directed") is { } explicitDirection ? XmlConvert.ToBoolean(explicitDirection) : (string?)item.Ancestors(ns + "graph").FirstOrDefault()?.Attribute("edgedefault") != "undirected";
                var bends = item.Descendants().Where(e => e.Name.LocalName == "Point").Select(p => new PointD(Number((string?)p.Attribute("x"), 0), Number((string?)p.Attribute("y"), 0))).ToImmutableArray();
                var label = item.Descendants().FirstOrDefault(e => e.Name.LocalName == "EdgeLabel")?.Value ?? Data(item, "label") ?? "";
                edge = new(id, source, target, label, Number(Data(item, "weight"), 1), bends, Directed: directed);
            }
            var sp = (string?)item.Attribute("sourceport"); var tp = (string?)item.Attribute("targetport");
            edge = edge with { Id = id, SourceId = source, TargetId = target,
                SourcePortId = sp is null ? edge.SourcePortId : portIds.TryGetValue((source, sp), out var a) ? a : throw new FormatException("Unknown source port."),
                TargetPortId = tp is null ? edge.TargetPortId : portIds.TryGetValue((target, tp), out var b) ? b : throw new FormatException("Unknown target port.") };
            edges.Add(edge);
        }
        var supported = new HashSet<string> { "visualizescene.node", "visualizescene.edge", "visualizescene.port", "visualizescene.labels", "nodegraphics", "edgegraphics", "label", "weight" };
        foreach (var name in keyNames.Values.Distinct().Where(name => !supported.Contains(name))) diagnostics.Add($"Unsupported GraphML data key '{name}' was not imported.");
        if (root.Descendants().Any(e => e.Name.LocalName is "TableNode" or "BpmnNodeStyle" or "UMLClassNode" or "Style")) diagnostics.Add("Specialized yFiles styles are not supported; imported nodes use basic shapes.");
        var graph = new GraphSnapshot(nodes.ToImmutableArray(), edges.ToImmutableArray(), ports.ToImmutableArray(), labels);
        GraphValidation.Validate(graph);
        return new(Normalize(graph), diagnostics);
    }
    public static string ToGraphML(GraphSnapshot graph)
    {
        GraphValidation.Validate(graph); graph = Normalize(graph);
        XNamespace ns = "http://graphml.graphdrawing.org/xmlns";
        var root = new XElement(ns + "graphml");
        void Key(string id, string owner, string name) => root.Add(new XElement(ns + "key", new XAttribute("id", id), new XAttribute("for", owner), new XAttribute("attr.name", name), new XAttribute("attr.type", "string")));
        Key("n", "node", "visualizescene.node"); Key("e", "edge", "visualizescene.edge"); Key("p", "port", "visualizescene.port"); Key("l", "graph", "visualizescene.labels");
        var children = graph.Nodes.ToLookup(n => n.ParentId ?? ""); var ports = graph.Ports.ToLookup(p => p.NodeId);
        XElement Build(string? parent, int depth)
        {
            if (depth > 128) throw new NotSupportedException("GraphML nesting is limited to 128 levels.");
            var element = new XElement(ns + "graph", new XAttribute("id", parent is null ? "G" : parent + "-graph"), new XAttribute("edgedefault", "directed"));
            foreach (var node in children[parent ?? ""])
            {
                var item = new XElement(ns + "node", new XAttribute("id", node.Id), new XElement(ns + "data", new XAttribute("key", "n"), JsonSerializer.Serialize(node, GraphJsonContext.Default.GraphNode)));
                foreach (var port in ports[node.Id]) item.Add(new XElement(ns + "port", new XAttribute("name", port.Id), new XElement(ns + "data", new XAttribute("key", "p"), JsonSerializer.Serialize(port, GraphJsonContext.Default.GraphPort))));
                if (node.IsGroup) item.Add(Build(node.Id, depth + 1));
                element.Add(item);
            }
            return element;
        }
        var main = Build(null, 0);
        main.AddFirst(new XElement(ns + "data", new XAttribute("key", "l"), JsonSerializer.Serialize(graph.Labels, GraphJsonContext.Default.ImmutableArrayGraphLabel)));
        foreach (var edge in graph.Edges)
        {
            var e = new XElement(ns + "edge", new XAttribute("id", edge.Id), new XAttribute("source", edge.SourceId), new XAttribute("target", edge.TargetId), new XAttribute("directed", edge.Directed), new XElement(ns + "data", new XAttribute("key", "e"), JsonSerializer.Serialize(edge, GraphJsonContext.Default.GraphEdge)));
            if (edge.SourcePortId is not null) e.Add(new XAttribute("sourceport", edge.SourcePortId));
            if (edge.TargetPortId is not null) e.Add(new XAttribute("targetport", edge.TargetPortId));
            main.Add(e);
        }
        root.Add(main);
        return new XDocument(new XDeclaration("1.0", "utf-8", null), root).ToString();
    }
}
