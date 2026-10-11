# Touch and keyboard graph navigation

## Implementation and validation

Navigation is implemented in the reusable Core, Editing and Uno control packages.
The source, managed contracts, browser input suite and isolated package consumers
are built together. Use the Actions reports for the exact revision being consumed;
a test inventory alone is not a claim that an arbitrary checkout has passed.

## Package boundaries

`VisualizeScene.Core.Viewport` provides finite, atomic view transforms, immutable
`ViewportState` values, a view revision counter and minimal-pan `EnsureVisible`.
It does not reference Uno, touch input, a graph document or undo history.

`VisualizeScene.Editing.TouchNavigator` tracks up to sixteen pointer IDs. The
oldest two contacts determine pinch center and distance. Extra contacts cannot
steal the pair; changing the pair rebases the gesture to avoid jumps. One-finger
pan has a four-view-unit movement threshold. A tap must finish within 500 ms and
must not have moved outside that threshold or included a second contact.

Pinch uses an absolute gesture baseline and retains the world point under its
moving screen centroid. Zoom stays in the existing 0.05–8 range. At a zoom limit,
rebasing prevents a dead zone when the pinch reverses direction. Contacts less
than eight view units apart pan without applying an unstable scale. External
view transforms rebase the recognizer and suppress a stale tap. Cancel keeps the
already displayed view but drops recognition state; late releases cannot activate
a tap. Invalid input is rejected before contact state is modified.

```csharp
var viewport = new Viewport();
var touch = new TouchNavigator(viewport);
touch.Press(1, new PointD(100, 100), milliseconds: 0);
touch.Press(2, new PointD(200, 100), milliseconds: 1);
touch.Move(1, new PointD(50, 120));
touch.Move(2, new PointD(250, 120));
// Zoom is 2; world point (150,100) is now at screen position (150,120).
touch.Release(1, new PointD(50, 120), milliseconds: 100);
touch.Release(2, new PointD(250, 120), milliseconds: 101);
```

Hosts supply monotonic millisecond timestamps; the recognizer has no timer or
platform dependency. Screen input is restricted to finite coordinates within
±1e9, separately from the graph's world-coordinate domain.

## Uno interaction arbitration

`GraphControl.TouchMode` supports three settings exposed under **View → Touch
input**. `Navigate`, the default, pans with one finger, selects with a tap and
zooms with two fingers. `Edit` gives one-finger Select-mode input to the existing
editor session, retaining its move/resize/bend/port/label/marquee behavior. A
second finger cancels the preview before taking over viewport navigation. After
a pinch, a remaining single contact continues panning; it cannot accidentally
resume the canceled edit. `Disabled` ignores graph touch input without removing
mouse or keyboard handlers.

Node and edge creation are deferred until a completed single-contact tap. This
prevents the first contact of a pinch from creating an item. Two successive taps
in Edge mode still choose source and target. Completed Select-mode double taps
use the existing double-click recognizer for label editing and group folding.
The control captures individual touch pointers, handles cancel/capture loss,
cancels on document changes, focus loss, unload or mode changes, and unsubscribes
its document handler on replacement/disposal. An external viewport change also
cancels a one-finger edit instead of applying it in a different coordinate frame.

The renderer receives the changed viewport through the existing Uno Skia canvas;
there is no second JavaScript graph engine. The implementation does not add
inertia, rotation, pressure/tilt pen behavior, long-press editing gestures, or
operating-system touchpad-specific gestures. Browser input is exercised by the runtime tests. Native physical-device behavior
requires separate validation; setting manipulation mode alone is not proof of
correct browser gesture routing.

## Keyboard focus and navigation

`GraphNavigationIndex` is an immutable registry of item bounds with deterministic
reading order and geometric direction queries. It prefers a candidate whose
perpendicular extent overlaps the current item's extent, then the smallest
forward gap plus a cross-axis penalty, then distance and ordinal ID. There is no
wraparound when a direction has no target. Empty/overlapping layouts terminate.
Queries scan candidates once; initialization sorts reading order. No per-query
sort is needed. The session caches the registry by revision, label metrics,
item-kind settings and relevant zoom changes.

`EditorSession.FocusedItemId` is separate from selection. Direct `FocusItem` does
not select or modify graph content. Pointer selection of a single item sets its
focus. `Navigate` and `NavigateBoundary` update selection and focus together;
extended navigation retains prior selection while advancing the focused anchor.
`NavigationKinds` defaults to nodes and groups and can include edges, ports and
measured independent labels. At low-detail zoom, independent labels are omitted
from traversal. Expanded groups use header bounds. Folding maps hidden focus to
the visible representative; deleting an owner clears orphaned focus. Reentrant
application handlers cannot leave a successfully reported navigation targeting a
deleted item.

```csharp
session.NavigationKinds = SelectionKinds.Nodes | SelectionKinds.Groups;
session.Navigate(NavigationDirection.Right);
session.Navigate(NavigationDirection.Down, extendSelection: true);
session.NavigateBoundary(last: true);
if (session.FocusedItemBounds is { } bounds)
    session.Viewport.EnsureVisible(bounds, width: 1200, height: 800);
```

The Uno control exposes Ctrl+Arrow for spatial navigation, Ctrl+Shift+Arrow to
extend selection, Ctrl+Home/End for reading-order boundaries, PageUp/Down for
vertical panning, and Shift+PageUp/Down for horizontal panning. Plain arrows keep
the existing exact nudge behavior. Navigation pans only enough to reveal the
focused item and does not change zoom. Oversized targets are centered. A
contrasting dashed focus outline is drawn as an interaction adorner, never as
exported graph data. Tab is not trapped, and the label editor retains its own
text-navigation shortcuts. The View menu provides equivalent navigation commands.

This is keyboard support, **not a complete accessibility/UI Automation graph peer
tree** and not yFiles API or screenshot parity.

## Added validation entry points

```sh
dotnet run --project tests/VisualizeScene.Navigation.Tests -c Release
python tests/browser/navigation.py 'http://127.0.0.1:8080/VisualizeScene/?test=1'
```

The managed cases cover viewport validation, anchor preservation, zoom clamping,
contact identity, slop, holds, cancellation, pair changes, coincident contacts,
external changes, focus lifetime, keyboard masks, deterministic ranking,
reentrant deletion and a 10,000-target fixture. The browser suite imports a real
fixture and drives the compiled Uno application through mouse, keyboard and
Chromium `Input.dispatchTouchEvent`. It checks viewport-only navigation, edit
arbitration, deferred creation, cancellation, focus reveal and text-editor
isolation. Diagnostics are read-only. Browser touch dispatch is input emulation,
not a physical touchscreen certification; SwiftShader is software rendering.

The workflow also executes the managed cases on native desktop runners and
extends the existing isolated NuGet consumers with the new APIs. The resulting reports identify the tested source revision; physical touch hardware
and a full accessibility graph peer tree remain outside this gate.

## Reference contracts

- Uno pointer source: https://github.com/unoplatform/uno/blob/master/src/Uno.UI/UI/Xaml/Input/Pointer.cs
- Microsoft pointer types: https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.input.pointer.pointerdevicetype
- Microsoft manipulation modes: https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.input.manipulationmodes
- Chromium input protocol: https://chromedevtools.github.io/devtools-protocol/tot/Input/
- yFiles navigation capability target: https://docs.yfiles.com/yfiles-html/api/MoveViewportInputMode/

These describe dependencies and behavioral targets, not validation of this
implementation or reproduction of proprietary source.
