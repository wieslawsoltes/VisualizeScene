using VisualizeScene.Core;

namespace VisualizeScene.Editing;

public enum TouchNavigationPhase { Idle, Pending, Panning, Pinching }
public readonly record struct TouchRelease(bool Tracked, bool IsTap, PointD Position);

/// <summary>Pointer-ID-based navigation independent of Uno, graph mutations and platform gesture recognizers.</summary>
public sealed class TouchNavigator
{
    private sealed record Contact(uint Id, PointD Position);
    private readonly List<Contact> contacts = [];
    private readonly Viewport viewport;
    private ViewportState baseline;
    private long observedRevision, started;
    private PointD anchor;
    private double span;
    private bool singleFingerPanning = true;
    public int ContactCount => contacts.Count;
    public bool WasMultiTouch { get; private set; }
    public bool HasMoved { get; private set; }
    public TouchNavigationPhase Phase { get; private set; }
    public bool SingleFingerPanning
    {
        get => singleFingerPanning;
        set { if (value == singleFingerPanning) return; Cancel(); singleFingerPanning = value; }
    }
    public TouchNavigator(Viewport viewport) => this.viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
    public bool IsTracking(uint id) => contacts.Any(c => c.Id == id);

    public bool Press(uint id, PointD position, long milliseconds)
    {
        Validate(position, milliseconds);
        if (IsTracking(id) || contacts.Count >= 16) return false;
        if (contacts.Count == 0) { started = milliseconds; HasMoved = WasMultiTouch = false; }
        contacts.Add(new(id, position));
        if (contacts.Count > 1) WasMultiTouch = true;
        Rebase(); Phase = contacts.Count > 1 ? TouchNavigationPhase.Pinching : TouchNavigationPhase.Pending;
        return true;
    }
    public bool Move(uint id, PointD position)
    {
        Validate(position, 0); var i = contacts.FindIndex(c => c.Id == id); if (i < 0) return false;
        // Wheel/fit/overview commands from a host must not be overwritten by an old touch baseline.
        if (viewport.Revision != observedRevision) { HasMoved = true; Rebase(); }
        contacts[i] = contacts[i] with { Position = position };
        var (center, distance) = Metrics();
        if (PointD.Distance(center, anchor) > 4 || contacts.Count > 1 && Math.Abs(distance - span) > 4) HasMoved = true;
        if (contacts.Count == 1 && (!HasMoved || !SingleFingerPanning && !WasMultiTouch)) return true;
        var requestedZoom = baseline.Zoom;
        if (contacts.Count > 1 && span >= 8 && distance >= 8)
            requestedZoom = baseline.Zoom * (distance / span);
        var zoom = Math.Clamp(requestedZoom, Viewport.MinimumZoom, Viewport.MaximumZoom);
        var world = baseline.Origin + anchor * (1 / baseline.Zoom);
        viewport.Restore(new(world - center * (1 / zoom), zoom)); observedRevision = viewport.Revision;
        Phase = contacts.Count > 1 ? TouchNavigationPhase.Pinching : TouchNavigationPhase.Panning;
        // Coincident contacts can pan. Establish scale only after they separate; never divide by zero.
        if (contacts.Count > 1 && (span < 8 || distance < 8 || zoom != requestedZoom)) Rebase();
        return true;
    }
    public TouchRelease Release(uint id, PointD position, long milliseconds)
    {
        Validate(position, milliseconds); if (!IsTracking(id)) return new(false, false, position);
        Move(id, position);
        var tap = contacts.Count == 1 && !WasMultiTouch && !HasMoved && milliseconds >= started && milliseconds - started <= 500;
        contacts.RemoveAll(c => c.Id == id);
        if (contacts.Count == 0) { Phase = TouchNavigationPhase.Idle; }
        else { Rebase(); Phase = contacts.Count > 1 ? TouchNavigationPhase.Pinching : TouchNavigationPhase.Panning; }
        return new(true, tap, position);
    }
    /// <summary>Cancels recognition without rewinding an already displayed viewport.</summary>
    public void Cancel() { contacts.Clear(); Phase = TouchNavigationPhase.Idle; HasMoved = WasMultiTouch = false; }
    private void Rebase()
    {
        baseline = viewport.State; observedRevision = viewport.Revision; (anchor, span) = Metrics();
    }
    private (PointD Center, double Distance) Metrics()
    {
        if (contacts.Count < 2) return (contacts.Count == 0 ? new() : contacts[0].Position, 0);
        // The oldest pair stays authoritative; additional contacts cannot steal either anchor.
        return (PointD.Lerp(contacts[0].Position, contacts[1].Position, .5), PointD.Distance(contacts[0].Position, contacts[1].Position));
    }
    private static void Validate(PointD position, long milliseconds)
    {
        // Input is a screen-space coordinate, not arbitrary graph geometry. Bound arithmetic as well as memory.
        if (!position.IsFinite || Math.Abs(position.X) > 1e9 || Math.Abs(position.Y) > 1e9 || milliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(position));
    }
}
