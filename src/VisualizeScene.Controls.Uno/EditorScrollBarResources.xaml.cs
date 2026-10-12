using Microsoft.UI.Xaml;

namespace VisualizeScene.Controls.Uno;

/// <summary>Compiled native ScrollBar templates sized for 16-unit desktop rails, without overlay transforms.</summary>
public sealed partial class EditorScrollBarResources : ResourceDictionary
{
    public EditorScrollBarResources() => InitializeComponent();
    public Style ScrollBarStyle => (Style)this["DesktopGraphScrollBar"];
}
