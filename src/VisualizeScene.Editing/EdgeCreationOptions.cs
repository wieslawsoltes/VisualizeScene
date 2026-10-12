using VisualizeScene.Core;

namespace VisualizeScene.Editing;

/// <summary>Validated defaults for future connections. Marker appearance does not change topology direction.</summary>
public sealed record EdgeCreationOptions(EdgeRouting Routing = EdgeRouting.Orthogonal,
    string Stroke = "#607D99", EdgeStyle? Style = null)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Routing) || !GraphValidation.ValidColor(Stroke))
            throw new ArgumentException("Edge defaults require a supported route and #RRGGBB color.", nameof(Routing));
        Style?.Validate();
    }
}

public sealed partial class EditorSession
{
    private EdgeCreationOptions edgeDefaults = new();
    public EdgeCreationOptions EdgeDefaults
    {
        get => edgeDefaults;
        set
        {
            ArgumentNullException.ThrowIfNull(value); value.Validate();
            if (value == edgeDefaults) return;
            // A two-click connection cannot silently change style half way through creation.
            CancelGesture(); edgeDefaults = value;
        }
    }
}
