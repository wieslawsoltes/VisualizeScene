using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace VisualizeScene.Workbench;

public sealed partial class WorkbenchPage
{
#if __WASM__
    private readonly Dictionary<string, (FrameworkElement Element, long Token)> diagnosticMenus = [];
    private readonly Queue<string> diagnosticMenuEvents = new();
    private long diagnosticMenuToken, diagnosticHoveredMenu;
#endif
    private void RegisterMenuDiagnostics(string id, FrameworkElement element, bool includeControl = false)
    {
#if __WASM__
        if (!DiagnosticsEnabled || element is not MenuFlyoutItemBase && !includeControl) return;
        if (diagnosticMenus.TryGetValue(id, out var previous) && ReferenceEquals(previous.Element, element)) return;
        var token = ++diagnosticMenuToken;
        diagnosticMenus[id] = (element, token);
        void Record(string kind)
        {
            // Observes native events only. Never marks them handled, takes capture, invokes commands or changes focus.
            diagnosticMenuEvents.Enqueue($"{{\"kind\":\"{kind}\",\"id\":\"{JsonEncodedText.Encode(id)}\",\"token\":{token}}}");
            while (diagnosticMenuEvents.Count > 32) diagnosticMenuEvents.Dequeue();
        }
        element.AddHandler(UIElement.PointerEnteredEvent, new PointerEventHandler((_, _) =>
        { diagnosticHoveredMenu = token; Record("entered"); }), true);
        element.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler((_, _) =>
        { if (diagnosticHoveredMenu == token) diagnosticHoveredMenu = 0; Record("exited"); }), true);
        element.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => Record("pressed")), true);
        element.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => Record("released")), true);
        element.Unloaded += (_, _) => { if (diagnosticHoveredMenu == token) diagnosticHoveredMenu = 0; };
#endif
    }
#if __WASM__
    private string MenuDiagnosticsJson()
    {
        var tokens = string.Join(",", diagnosticMenus.Where(pair => pair.Value.Element.IsLoaded)
            .Select(pair => "\"" + JsonEncodedText.Encode(pair.Key).ToString() + "\":" + pair.Value.Token));
        return $"{{\"tokens\":{{{tokens}}},\"hovered\":{diagnosticHoveredMenu},\"events\":[{string.Join(",", diagnosticMenuEvents)}]}}";
    }
#endif
}
