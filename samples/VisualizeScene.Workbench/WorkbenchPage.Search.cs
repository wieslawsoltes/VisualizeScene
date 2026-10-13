using Microsoft.UI.Xaml.Controls;

namespace VisualizeScene.Workbench;

public sealed partial class WorkbenchPage
{
    private void AddSearchMenu(MenuBar menu)
    {
        RegisterSearchDiagnostics();
        var edit = menu.Items.First(item => item.Title == "Edit");
        var find = new MenuFlyoutItem { Text = "Find graph text  Ctrl+F" }; Register("FindGraph", find);
        find.Click += (_, _) => search.Open(); edit.Items.Add(find);
        var replace = new MenuFlyoutItem { Text = "Replace graph text  Ctrl+H" }; Register("ReplaceGraph", replace);
        replace.Click += (_, _) => search.Open(true); edit.Items.Add(replace);
    }
}
