# Compact scrollbar template review

The first PR #10 browser run failed before its End-key assertion. The retained state
placed the horizontal native Thumb at y=1018.5 with height 12 inside a scrollbar whose
bounds were y=1006 and height 16. Its midpoint was therefore outside the control and
inside the status bar. The vertical thumb similarly extended beyond its rail. A default
overlay-style template was being forced into a compact desktop layout; changing only
ScrollBar.Height/Width did not resize that template's interactive geometry.

`EditorScrollBarResources` supplies original, compiled XAML templates sized to the rails.
The native ScrollBar still owns all range arithmetic, RepeatButton commands and Thumb
drag events. It finds the standard HorizontalRoot/VerticalRoot, small/large increase and
decrease buttons and Thumb parts. The new visuals use neither overlay transforms nor
runtime XAML parsing. Hover/press/disabled feedback and focus visuals remain; visible
arrow buttons match a desktop editor more closely. These are original visuals, not copied
yFiles or Uno artwork. Resource objects are scoped to each host and packaged with the
reusable controls. The default template is not modified for other application controls.

The native part contract was checked against Uno's public ScrollBar implementation:
https://github.com/unoplatform/uno/blob/master/src/Uno.UI/UI/Xaml/Controls/ScrollBar/ScrollBar.mux.cs
and the WinUI ScrollBar API:
https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.primitives.scrollbar

The browser suite now requires both thumbs to remain inside their corresponding rail
before pointer input. It exercises native arrow-button input as well as keyboard and
dragging. The entire suite runs independently at pixel densities 1 and 2; each run has
its own report and screenshots. A failed high-density run fails the workflow as well.
No failing assertion was removed or repeated until it passed. Successful run IDs and
source revisions, not this document, establish final validation.

This review does not establish complete graph accessibility or physical touch/GPU
certification. The 16-unit rails target mouse and keyboard desktop use; touch navigation
remains available on the graph canvas. See viewport-scrolling.md for range/lifetime and
performance contracts and remaining yFiles compatibility limits.
