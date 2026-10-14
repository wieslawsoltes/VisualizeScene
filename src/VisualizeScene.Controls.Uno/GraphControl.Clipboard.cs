using Microsoft.UI.Xaml;

namespace VisualizeScene.Controls.Uno;

public enum GraphClipboardCommand { Copy, Cut, Paste, PasteInPlace, Duplicate }

public sealed partial class GraphControl
{
    public event EventHandler? ClipboardChanged;
    public bool CanExecuteClipboardCommand(GraphClipboardCommand command) => command switch
    {
        GraphClipboardCommand.Copy or GraphClipboardCommand.Cut or GraphClipboardCommand.Duplicate => !disposed && Session.Selection.Count > 0,
        GraphClipboardCommand.Paste or GraphClipboardCommand.PasteInPlace => !disposed && Session.CanPaste,
        _ => throw new ArgumentOutOfRangeException(nameof(command))
    };
    /// <summary>One command path for native toolbar, menu, context menu and keyboard input.</summary>
    public void ExecuteClipboardCommand(GraphClipboardCommand command)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!CanExecuteClipboardCommand(command)) return;
        CancelTouchNavigation();
        switch (command)
        {
            case GraphClipboardCommand.Copy: Session.Copy(); break;
            case GraphClipboardCommand.Cut: Session.Cut(); break;
            case GraphClipboardCommand.Paste: Session.Paste(); break;
            case GraphClipboardCommand.PasteInPlace: Session.PasteInPlace(); break;
            case GraphClipboardCommand.Duplicate: Session.Duplicate(); break;
        }
        Focus(FocusState.Programmatic); Invalidate();
    }
}
