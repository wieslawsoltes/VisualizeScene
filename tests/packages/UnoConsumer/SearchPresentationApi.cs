using VisualizeScene.Controls.Uno;

namespace PackageConsumer;

// Compile the reusable control's presentation lifetime from packages on both Uno targets.
public static class SearchPresentationApi
{
    public static void Verify(GraphControl graph)
    {
        using var search = new GraphSearchControl { Target = graph };
        _ = search.RevealRequests;
        _ = search.FindElement("Ids");
        search.Target = null;
    }
}
