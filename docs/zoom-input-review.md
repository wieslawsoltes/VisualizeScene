# Zoom dropdown input and focus lifecycle

The post-merge build 38382720358 passed search, arrangement, fitting, enlargement and
folding tests, but the scrollbar suite stopped before its first scenario: selecting
100% through the toolbar did not complete. The retained screenshot shows the native
zoom popup still open; the camera remains at the initial fitted zoom (about 7.14%) and
the graph has no input focus. No graph error or unintended model change was recorded.
The old helper sent Home/Down/Enter after a fixed delay, without observing popup
realization or focus. This does not establish the exact missed-event sequence.

The workbench no longer moves focus to the graph inside the zoom SelectionChanged
handler. It restores focus after native DropDownClosed, on the dispatcher, only while
focus still belongs to that selector or the graph. A later popup opening supersedes
the request. Selecting an unrelated editor, opening a label dialog, or unloading the
view prevents that callback from taking focus. Closed-dropdown keyboard changes retain
selector focus. Zoom still uses the same viewport operation and never edits graph data.

The actual zoom selector and its first realized item expose read-only open/index/focus
state. The shared input helper observes a stable target and native pointer entry, then
clicks once. It waits for an open, realized, focused dropdown before keyboard navigation,
and requires the selected index, closed popup and graph focus afterwards. No state is
forced and no click or key sequence is retried. Both scrollbar-density runs additionally
select 25%, 150% and 100% in succession; existing scroll geometry, drag, cache, revision
and exact-data assertions remain. The classic workspace uses the same helper.

WinUI documents that the default ComboBox selection commits on pointer input or Enter,
not on navigation with arrow keys in its open list:
https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/combo-box
The code therefore does not claim that arrow navigation should commit live zoom values.
Uno's public implementation ties item selection to popup closure:
https://github.com/unoplatform/uno/blob/56e430f5c56af07e86ea4b3fe782c83d2a94ac64/src/Uno.UI/UI/Xaml/Controls/ComboBox/ComboBox.custom.cs

This is focused lifecycle and readiness hardening, not evidence that every intermittent
native UI event issue is eliminated. The failed main artifact is retained separately;
only final-head passing runs establish completion of this correction.
