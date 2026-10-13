using VisualizeScene.Core;

namespace VisualizeScene.Editing;

public sealed partial class EditorSession
{
    public GraphEdgeCommandResult? LastEdgeCommand { get; private set; }
    public void EditEdges(EdgeCommand command, CancellationToken cancellationToken = default)
    {
        var revision = Document.Revision; var snapshot = Document.Snapshot; var selected = Selection; var policy = ConnectionPolicy;
        var result = GraphEdgeCommands.Calculate(snapshot, selected, command, policy, cancellationToken);
        if (Document.Revision != revision || !ReferenceEquals(snapshot, Document.Snapshot) ||
            !ReferenceEquals(selected, Selection) || !ReferenceEquals(policy, ConnectionPolicy))
            throw new InvalidOperationException("The graph, selection or connection policy changed while editing edges.");
        cancellationToken.ThrowIfCancellationRequested();
        Document.Replace(result.Graph, command == EdgeCommand.Reverse ? "Reverse edges" : "Clear edge bends", expectedRevision: revision);
        LastEdgeCommand = ReferenceEquals(Document.Snapshot, result.Graph) ? result : null;
    }
}
