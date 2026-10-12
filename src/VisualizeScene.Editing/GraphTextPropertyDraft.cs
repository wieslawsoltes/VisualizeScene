namespace VisualizeScene.Editing;

/// <summary>Staged common/mixed text. Null Replacement means untouched; empty means an explicit clear.</summary>
/// <remarks>Repeated value notifications cannot erase an explicit clear of a mixed-value placeholder.
/// This object is UI-thread-owned and does not mutate a document.</remarks>
public sealed class GraphTextPropertyDraft
{
    private readonly string initial;
    private readonly bool mixed;
    private bool changed;
    public string Text { get; private set; }
    public string? Replacement => changed ? Text : null;
    public GraphTextPropertyDraft(GraphPropertyValue<string> property)
    {
        if (property.Count < 1) throw new ArgumentException("At least one applicable text value is required.", nameof(property));
        mixed = property.IsMixed;
        initial = mixed ? "" : property.Value ?? throw new ArgumentException("Text cannot be null.", nameof(property));
        Text = initial;
    }
    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        // TextBox can report the value set by Clear again after its command handler finishes.
        if (Text == text) return;
        Text = text; changed = text != initial;
    }
    public void Clear() { Text = ""; changed = mixed || initial.Length > 0; }
    public void Reset() { Text = initial; changed = false; }
}
