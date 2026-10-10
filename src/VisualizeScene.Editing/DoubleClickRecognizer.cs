using VisualizeScene.Core;

namespace VisualizeScene.Editing;

/// <summary>
/// Recognizes two completed, stationary primary-pointer clicks in the same editing context.
/// A mode switch, drag, document change, modifier change or cancellation breaks the sequence.
/// Coordinates are device-independent screen coordinates; timestamps are monotonic milliseconds.
/// </summary>
public sealed class DoubleClickRecognizer
{
    private sealed record Click(HitResult Hit, EditorMode Mode, InputModifiers Modifiers,
        PointD Position, long Revision, long Time);
    private Click? previous, pressed;
    public long IntervalMilliseconds { get; set; } = 500;
    public double Distance { get; set; } = 4;

    public void Reset() => previous = pressed = null;

    public void Press(HitResult hit, EditorMode mode, InputModifiers modifiers,
        PointD screen, long revision, long milliseconds)
    {
        if (IntervalMilliseconds < 1 || !double.IsFinite(Distance) || Distance < 0)
            throw new InvalidOperationException("Double-click interval and distance must be valid.");
        if (mode != EditorMode.Select || hit.Kind is not (HitKind.None or HitKind.Node or HitKind.Edge or HitKind.Label))
        {
            Reset(); return;
        }
        pressed = new(hit, mode, modifiers, screen, revision, milliseconds);
    }

    public void Move(PointD screen)
    {
        if (pressed is { } click && PointD.Distance(click.Position, screen) > Distance) Reset();
    }

    public HitResult? Release(PointD screen, long revision, long milliseconds)
    {
        var click = pressed; pressed = null;
        if (click is null || click.Revision != revision || milliseconds < click.Time ||
            milliseconds - click.Time > IntervalMilliseconds || PointD.Distance(click.Position, screen) > Distance)
        {
            previous = null; return null;
        }
        var first = previous;
        previous = click with { Position = screen, Time = milliseconds };
        if (first is null || first.Hit != click.Hit || first.Mode != click.Mode ||
            first.Modifiers != click.Modifiers || first.Revision != revision ||
            milliseconds < first.Time || milliseconds - first.Time > IntervalMilliseconds ||
            PointD.Distance(first.Position, screen) > Distance) return null;
        previous = null;
        return click.Hit;
    }
}
