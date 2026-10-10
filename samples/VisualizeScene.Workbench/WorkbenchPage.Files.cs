using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VisualizeScene.Serialization;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace VisualizeScene.Workbench;

public sealed partial class WorkbenchPage
{
    private async Task OpenFile()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary }; picker.FileTypeFilter.Add(".json"); picker.FileTypeFilter.Add(".graphml"); picker.FileTypeFilter.Add(".xml");
        var file = await picker.PickSingleFileAsync(); if (file is null) return; var text = await FileIO.ReadTextAsync(file); if (!await ConfirmDiscard()) return; Import(text); currentName = file.Name; dirty = false;
    }
    private void Import(string text)
    {
        var import = text.TrimStart().StartsWith('<') ? GraphIO.FromGraphML(text) : new GraphImportResult(GraphIO.FromJson(text), []);
        document.Replace(import.Graph, "Import graph"); graph.Fit(); status.Text = import.Diagnostics.Count == 0 ? "Graph imported." : string.Join(" ", import.Diagnostics);
    }
    private async Task Save(string extension)
    {
        await RunAsync(async () =>
        {
            var bytes = extension switch { "png" => graph.ExportPng(), "svg" => Encoding.UTF8.GetBytes(SvgExporter.Export(document.Snapshot, graph.MeasureLabel)), "graphml" => Encoding.UTF8.GetBytes(GraphIO.ToGraphML(document.Snapshot)), _ => Encoding.UTF8.GetBytes(GraphIO.ToJson(document.Snapshot)) };
            var name = "visualizescene." + extension;
#if __WASM__
            var mime = extension switch { "png" => "image/png", "svg" => "image/svg+xml", "graphml" => "application/graphml+xml", _ => "application/json" };
            Uno.Foundation.WebAssemblyRuntime.InvokeJS("(()=>{const raw=atob('" + Convert.ToBase64String(bytes) + "');const data=Uint8Array.from(raw,c=>c.charCodeAt(0));const url=URL.createObjectURL(new Blob([data],{type:'" + mime + "'}));const a=document.createElement('a');a.href=url;a.download='" + name + "';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);})()"); await Task.CompletedTask;
#else
            var picker = new FileSavePicker { SuggestedFileName = "visualizescene", SuggestedStartLocation = PickerLocationId.DocumentsLibrary }; picker.FileTypeChoices.Add(extension.ToUpperInvariant(), ["." + extension]);
            var file = await picker.PickSaveFileAsync(); if (file is null) return; await FileIO.WriteBytesAsync(file, bytes);
#endif
            if (extension is "json" or "graphml") dirty = false; status.Text = "Saved " + name;
        });
    }
    private async Task TextExchange(bool import)
    {
        await RunAsync(async () =>
        {
            var text = new TextBox { Text = import ? "" : GraphIO.ToJson(document.Snapshot), AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Consolas"), FontSize = 11, Width = 600, Height = 400, IsReadOnly = !import }; Register("GraphSource", text);
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = import ? "Import JSON or GraphML" : "Graph document · JSON", Content = text, PrimaryButtonText = import ? "Import" : "", CloseButtonText = "Close" };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && import) Import(text.Text);
        });
    }
    private Task ShowHelp() => ShowMessage("Editing guide", "Select: drag a node to move it; drag empty canvas for a selection rectangle. Ctrl+click adds or removes items.\n\nDouble-click a node or edge to edit its label. Double-click empty canvas to create a node. Ctrl+double-click an edge inserts a bend. Drag selected corners or bends to reshape.\n\nNode mode: click to create nodes. Edge mode: click a source and then a target; ports are supported. Pan mode or middle-button drag moves the viewport. The wheel zooms around the pointer.\n\nCtrl+Z/Y: undo/redo. Ctrl+C/V/D: copy/paste/duplicate. Ctrl+G: group. Ctrl+Shift+G: ungroup. F2: label. Delete: remove. Home: fit. Escape: cancel and select.\n\nDouble-click group headers to fold/unfold. All graph edits and layouts are undoable.");
    private Task ShowStatus() => ShowMessage("Implementation status", "VisualizeScene 0.1 is an independent graph toolkit and Uno workbench, not yFiles.\n\nImplemented: immutable graph model, groups/folding, ports/labels, transactional editing, undo/redo, six layouts, obstacle-aware orthogonal routing, graph analysis, JSON/GraphML interchange, SVG/PNG export, Skia rendering and reusable Uno controls.\n\nFull yFiles API/visual parity is not implemented. Advanced layout constraints, specialized BPMN/UML/table styles, WPF template compatibility, printing and full GraphML extensions remain outside this version. See the repository capability matrix for exact boundaries.\n\nRendering uses Uno’s shared Skia surface. Hardware acceleration depends on the platform and driver; CPU draw timings are not GPU timings.");
    private async Task ShowMessage(string title, string body) => await new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = new ScrollViewer { Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap, MaxWidth = 550 } }, CloseButtonText = "Close" }.ShowAsync();
}
