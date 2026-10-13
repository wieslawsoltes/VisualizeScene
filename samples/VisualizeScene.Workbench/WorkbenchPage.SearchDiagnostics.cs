using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace VisualizeScene.Workbench;

public sealed partial class WorkbenchPage
{
    private void RegisterSearchDiagnostics()
    {
        foreach (var name in new[] { "Query", "Replacement", "Case", "Word", "Ids", "Hidden", "Selection", "Capture", "Kind", "Results", "Previous", "Next", "SelectAll", "Replace", "ReplaceAll", "Close" })
            if (search.FindElement(name) is { } element)
                RegisterMenuDiagnostics("Search" + name, element, includeControl: true);
    }
    private string SearchInteractionJson()
    {
        static string Bool(bool value) => value ? "true" : "false";
        var flags = string.Join(",", new[] { "Case", "Word", "Ids", "Hidden", "Selection" }.Select(name =>
            name + ":" + Bool(search.FindElement(name) is CheckBox { IsChecked: true })));
        var kind = search.FindElement("Kind") as ComboBox;
        var query = search.FindElement("Query") as Control;
        return $"{{options:{{{flags}}},kind:{kind?.SelectedIndex ?? -1},kindOpen:{Bool(kind?.IsDropDownOpen == true)},queryFocused:{Bool(query?.FocusState is FocusState.Keyboard or FocusState.Pointer or FocusState.Programmatic)},revealRequests:{search.RevealRequests}}}";
    }
}
