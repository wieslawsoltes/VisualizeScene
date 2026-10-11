using Microsoft.UI.Xaml.Controls;
using VisualizeScene.Controls.Uno;
using VisualizeScene.Core;
using VisualizeScene.Editing;

namespace PackageConsumer;

/// <summary>Compiles against NuGet packages only on both Uno targets; not a desktop execution test.</summary>
public sealed class ConsumerView : UserControl, IDisposable
{
    private readonly GraphControl graph = new();
    private readonly GraphSearchControl search;
    private readonly GraphOverview overview;
    private readonly GraphPropertyInspector inspector;
    public ConsumerView()
    {
        var document = new GraphDocument();
        document.Edit("Create", edit => edit.Add(new GraphNode("n", new(0, 0, 140, 70), "Package consumer")));
        graph.Document = document;
        graph.TouchMode = GraphTouchMode.Navigate;
        graph.PictureCacheCapacity = 128;
        graph.NavigateBoundary();
        graph.EnsureFocusedItemVisible();
        graph.Session.ConnectionPolicy = new ConnectionPolicy { AllowParallelEdges = false };
        graph.Session.AddLabel("n", "Annotation");
        search = new GraphSearchControl { Target = graph };
        graph.RevealItem("n");
        overview = new GraphOverview { Target = graph };
        inspector = new GraphPropertyInspector { Target = graph };
        var palette = new NodePalette { Target = graph };
        var panel = new StackPanel(); panel.Children.Add(graph); panel.Children.Add(overview);
        panel.Children.Add(palette); panel.Children.Add(inspector); panel.Children.Add(search); Content = panel;
    }
    public new void Dispose() { search.Dispose(); inspector.Dispose(); overview.Dispose(); graph.Dispose(); }
}
