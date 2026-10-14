using Microsoft.UI.Xaml.Controls;
using VisualizeScene.Controls.Uno;

namespace VisualizeScene.Workbench;

public sealed partial class WorkbenchPage
{
    private readonly Dictionary<GraphClipboardCommand, MenuFlyoutItem> clipboardMenuItems = [];
    private void AddClipboardMenu(MenuBarItem menu)
    {
        foreach (var (command, caption) in new[] {
            (GraphClipboardCommand.Cut, "Cut  Ctrl+X"), (GraphClipboardCommand.Copy, "Copy  Ctrl+C"),
            (GraphClipboardCommand.Paste, "Paste  Ctrl+V"), (GraphClipboardCommand.PasteInPlace, "Paste in place  Ctrl+Shift+V"),
            (GraphClipboardCommand.Duplicate, "Duplicate  Ctrl+D") })
        {
            var item = new MenuFlyoutItem { Text = caption };
            Register("Edit" + command, item); clipboardMenuItems.Add(command, item); menu.Items.Add(item);
            item.Click += (_,_) => Run(() => { graph.ExecuteClipboardCommand(command); ReturnSelectionFocus(menu, item); });
        }
    }
    private void UpdateClipboardCommands()
    {
        foreach (var pair in clipboardMenuItems) pair.Value.IsEnabled = graph.CanExecuteClipboardCommand(pair.Key);
        foreach (var command in new[] { GraphClipboardCommand.Copy, GraphClipboardCommand.Cut, GraphClipboardCommand.Paste })
            if (testControls.GetValueOrDefault(command.ToString()) is Control control) control.IsEnabled = graph.CanExecuteClipboardCommand(command);
    }
}
