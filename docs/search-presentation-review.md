# Stable Find/Replace presentation and navigation

The modeless search panel now uses a stable 320-unit preferred width rather than sizing
its right-aligned outer rectangle from changing result text. Status occupies a fixed
44-unit row; search options and replacement actions therefore do not move when the
message changes between searching, no matches, normal counts and hidden-match details.
Compact 28-unit option rows keep the existing native checkbox behavior, with explicit
transparent backgrounds over their control bounds. All visuals are original Uno controls,
not copied yFiles artwork or a second HTML graph editor.

The outer panel is constrained to the target graph viewport. Its content has an explicit
wrap width and scrolls in short or unusually narrow hosts. Target size subscriptions are
removed when the control is rebound or disposed. Camera movement and search result changes
do not recalculate the width constraint. Source: WinUI's Width contract documents Auto
sizing and the distinction between desired dimensions and asynchronously arranged bounds:
https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.frameworkelement.width

## Failure evidence and input validation

Standalone PR #15 run 38354489678 stopped at a port-ID search. Its screenshot and state
showed query `ap`, kind Ports, no matches and Search IDs **unchecked**. The combined PR #16
subsequently passed the unchanged test. The old trace does not prove exactly why the click
failed; content-driven layout and native input readiness are reviewed here, not declared
an exhaustive explanation of every intermittent popup or focus issue.

Read-only diagnostics now report native checkbox values, query focus and dropdown
open/index state. Search tests require the requested state after each single input action.
They use the existing stable-target/native-PointerEntered observer for search controls
and retain its bounded event trace. Observation never sets focus, changes a checkbox,
invokes a command or handles an input event. No click is retried and no failure assertion
is removed. IsChecked is read from the actual native control:
https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.primitives.togglebutton.ischecked

Tests also compare the same option/replacement rectangles across long hidden-match
feedback and verify viewport containment and reachable Close in a 640x520 window.
All prior search, replacement, captured-selection, Unicode, locked-owner, file-picker
revision, save/import and exact-undo scenarios remain.

## Match-navigation work

FindNext previously changed ListView.SelectedIndex (whose SelectionChanged handler
revealed the match), then explicitly revealed it again. It now lets the selection event
perform the changed-index reveal. Cycling a single match still explicitly reveals once,
since no selection-change event is generated. Post-reveal identity checks prevent a
callback-triggered result/document replacement from scrolling an obsolete result row or
overwriting its status. RevealRequests counts actual calls, not GPU frames or latency.
Browser assertions require exactly one reveal for a changed index and for a single-match
cycle. This is a deterministic redundant-work reduction, not a measured FPS speedup.

This change does not alter the literal-search kernel, registry/index allocation, virtualized
result-list implementation, replacement transaction or graph rendering backend. Existing
portable search tests and isolated package consumers remain the compatibility gates.
The package-only Uno consumer compiles the new counter and target lifetime on both targets.

## Scope

The public WPF demo remains the overall compact-editor reference:
https://marketplace.visualstudio.com/items?itemName=yWorks.yFilesWPF
This is not a claim that yFiles uses this search layout or that the full WPF API, templates,
specialized styles, advanced layout engines, accessibility, docking, printing or multiple
documents have been reproduced. Physical GPU/touch/native desktop interaction is not
established by software-rendered browser tests. Validation belongs to the exact tested
revision; source inventory alone is not evidence of passing tests.
