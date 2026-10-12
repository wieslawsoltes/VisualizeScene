# Arrangement recovery and interaction review

The two initial PR #14 commits contained the arrangement implementation and portable tests.
Its independent browser entry point, package API consumer and pinned-baseline benchmark
were stored in a Git tree but had not been committed to the branch. Recovery commit
63ecd7b48677bd19104ca391f00fbf77473ac35a publishes those files without replacing the
previous implementation commits. The package-only core consumer additionally executes
reference-node sizing, exact undo and arrangement-state queries, rather than relying
solely on compile-only Uno API coverage.

## Layout selector failure

Build 38264344495 passed core and desktop compilation and the other browser entry points,
but failed the first layout-panel scenario. The retained snapshot had revision 1, status
Ready, no graph error and unchanged sample data: the layout command had not started.
The registered LayoutKind rectangle included its label/header (57 units high), placing
its center at the header/dropdown boundary. This is a suspected hit-target cause, not
proof of the exact native event sequence; the old test did not record dropdown state.

The workbench now places the algorithm and direction labels outside their dropdown hit
regions and assigns explicit accessibility names. Tests observe native IsDropDownOpen
and SelectedIndex through read-only diagnostics. A layout choice must open, select the
requested index and close before Apply is clicked. The existing layout completion,
topology, routing/undo, analysis, exports and sample assertions remain unchanged.
There is no repeat-until-pass click, forced graph mutation, or disabled failing test.

## Scope and evidence

The recovered arrangement suite drives all eleven toolbar commands and the matching
menu through real pointer/keyboard input. It checks reference selection, locked nodes,
unchanged camera/collections, repeated no-op commands, exact undo and responsive controls.
Portable regressions retain the prior alignment implementation as an exact oracle on
fractional-coordinate and grouped fixtures. The benchmark distinguishes the whole
alignment operation from an already-aligned no-op kernel; it does not imply a graphics
backend or FPS improvement.

Only completed final-head CI jobs establish passing validation. Intermediate failures
and canceled superseded runs must remain attributed to their own source revisions.
This recovery does not establish full yFiles WPF API or visual parity, or certification
on physical GPU/touch devices and native desktop GUI input.
