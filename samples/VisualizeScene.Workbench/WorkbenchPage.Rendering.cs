using Microsoft.UI.Xaml.Controls;

namespace VisualizeScene.Workbench;

public sealed partial class WorkbenchPage
{
    private readonly Dictionary<long, ToggleMenuFlyoutItem> renderingCacheItems = [];
    private void AddRenderingMenu(MenuBarItem view)
    {
        var menu = new MenuFlyoutSubItem { Text = "Rendering cache" };
        Register("RenderingCacheMenu", menu); view.Items.Add(menu);
        foreach (var mib in new[] { 0, 4, 16, 64 })
        {
            var bytes = (long)mib * 1024 * 1024;
            var item = new ToggleMenuFlyoutItem
            {
                Text = mib == 0 ? "Off" : $"{mib} MiB estimated pictures" + (mib == 16 ? " (default)" : ""),
                IsChecked = graph.PictureCacheByteBudget == bytes
            };
            Register("RenderingCache" + mib, item); renderingCacheItems[bytes] = item; menu.Items.Add(item);
            item.Click += (_, _) => Run(() =>
            {
                graph.PictureCacheByteBudget = bytes;
                foreach (var pair in renderingCacheItems) pair.Value.IsChecked = pair.Key == bytes;
                ReturnSelectionFocus(view, item); PublishTestState();
            });
        }
    }
    // Read-only UI observations: tests drive the actual native menu, never these values.
    private string RenderingCacheChoicesJson() => "{" + string.Join(",", renderingCacheItems.Select(pair =>
        "\"" + (pair.Key / (1024 * 1024)) + "\":" + (pair.Value.IsChecked ? "true" : "false"))) + "}";
}
