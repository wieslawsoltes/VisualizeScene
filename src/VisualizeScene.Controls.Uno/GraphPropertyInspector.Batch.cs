using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VisualizeScene.Core;
using VisualizeScene.Editing;

namespace VisualizeScene.Controls.Uno;

public sealed partial class GraphPropertyInspector
{
    // Build a fixed-size set of property editors, not one control per selected item.
    private void BuildBatch()
    {
        var captured = new GraphPropertySelection(target!.Document.Snapshot, target.Session.Selection);
        batchSummary = captured;
        title.Text = $"{captured.ItemIds.Length:N0} items selected";
        identity.Text = $"{captured.NodeCount:N0} nodes · {captured.EdgeCount:N0} edges · {captured.LabelCount:N0} labels · {captured.PortCount:N0} ports";
        ToolTipService.SetToolTip(identity, identity.Text);
        var changes = new Dictionary<string, Func<GraphPropertyPatch, GraphPropertyPatch>>(StringComparer.Ordinal);
        string Caption(string label, int count) => count == captured.ItemIds.Length ? label : $"{label} ({count:N0})";
        void Pending(string name, Func<GraphPropertyPatch, GraphPropertyPatch>? patch)
        {
            if (patch is null) changes.Remove(name); else changes[name] = patch;
            message.Text = changes.Count == 0 ? "No staged changes." : $"{changes.Count} properties staged. Apply changes only these fields.";
        }
        void Number(string name, string label, GraphPropertyValue<double> value, Func<GraphPropertyPatch, double, GraphPropertyPatch> set)
        {
            if (value.Count == 0) return;
            var input = Text(name, Caption(label, value.Count), value.IsMixed ? "" : F(value.Value));
            input.PlaceholderText = "Multiple values";
            input.TextChanged += (_, _) => Pending(name, (value.IsMixed ? input.Text.Length == 0 : input.Text == F(value.Value)) ? null : patch => set(patch, N(input)));
        }
        void EnumField<T>(string name, GraphPropertyValue<T> value, Func<GraphPropertyPatch, T, GraphPropertyPatch> set) where T : struct, Enum
        {
            if (value.Count == 0) return;
            var input = Choice(name, value.Value);
            input.PlaceholderText = "Multiple values";
            if (value.IsMixed) input.SelectedIndex = -1;
            input.SelectionChanged += (_, _) => Pending(name, input.SelectedItem is T chosen && (value.IsMixed || !EqualityComparer<T>.Default.Equals(chosen, value.Value)) ? patch => set(patch, chosen) : null);
        }
        void Boolean(string name, string label, GraphPropertyValue<bool> value, Func<GraphPropertyPatch, bool, GraphPropertyPatch> set)
        {
            if (value.Count == 0) return;
            var input = Check(name, Caption(label, value.Count), value.Value);
            input.IsThreeState = true; input.IsChecked = value.IsMixed ? null : value.Value;
            void Update() => Pending(name, input.IsChecked is bool chosen && (value.IsMixed || chosen != value.Value) ? patch => set(patch, chosen) : null);
            input.Checked += (_, _) => Update(); input.Unchecked += (_, _) => Update(); input.Indeterminate += (_, _) => Update();
            ToolTipService.SetToolTip(input, "Indeterminate leaves this property unchanged.");
        }
        void ColorField(string name, GraphPropertyValue<string> value, Func<GraphPropertyPatch, string, GraphPropertyPatch> set)
        {
            if (value.Count == 0) return;
            var input = Color(name, value.IsMixed ? "" : value.Value);
            input.IsMixed = value.IsMixed; input.Input.PlaceholderText = "Multiple values";
            input.ValueChanged += (_, _) => Pending(name, (value.IsMixed ? input.Value.Length == 0 : input.Value == value.Value) ? null : patch => set(patch, input.Value));
        }
        if (captured.Text.Count > 0)
        {
            fields.AddGroup("Label");
            var input = Text("Label", Caption("Text", captured.Text.Count), captured.Text.IsMixed ? "" : captured.Text.Value, true);
            input.PlaceholderText = "Multiple values";
            input.TextChanged += (_, _) => Pending("Text", input.Text == (captured.Text.IsMixed ? "" : captured.Text.Value) ? null : patch => patch with { Text = input.Text });
            // Explicit empty text is useful even when the mixed-value placeholder is already empty.
            Command("ClearText", "Clear selected text", () => { input.Text = ""; Pending("Text", patch => patch with { Text = "" }); }, reportApplied: false);
            Number("FontSize", "Font size", captured.FontSize, (p, v) => p with { FontSize = v });
        }
        if (captured.Stroke.Count > 0)
        {
            fields.AddGroup("Appearance");
            EnumField("Shape", captured.Shape, (p, v) => p with { Shape = v });
            ColorField("Fill", captured.Fill, (p, v) => p with { Fill = v });
            ColorField("Stroke", captured.Stroke, (p, v) => p with { Stroke = v });
            EnumField("Routing", captured.Routing, (p, v) => p with { Routing = v });
            Boolean("Directed", "Directed", captured.Directed, (p, v) => p with { Directed = v });
        }
        if (captured.NodeCount > 0)
        {
            fields.AddGroup("Geometry");
            Number("Width", "Width", captured.Width, (p, v) => p with { Width = v });
            Number("Height", "Height", captured.Height, (p, v) => p with { Height = v });
            Boolean("Locked", "Lock position", captured.Locked, (p, v) => p with { Locked = v });
        }
        if (captured.Weight.Count > 0)
        {
            fields.AddGroup("Data");
            Number("Weight", "Weight", captured.Weight, (p, v) => p with { Weight = v });
        }
        Apply(() =>
        {
            var patch = new GraphPropertyPatch();
            foreach (var apply in changes.Values) patch = apply(patch);
            captured.Apply(target!.Document, patch, inspectedRevision);
        });
        Command("Reset", "Reset staged changes", Refresh, reportApplied: false);
        message.Text = captured.PortCount == captured.ItemIds.Length ? "Port locations are owner-relative. Select one port to edit its coordinates." :
            "Multiple values are left unchanged until edited. Counts show which items a field applies to. Position-locked nodes must be unlocked before resizing.";
    }
}
