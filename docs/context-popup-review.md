# Native context-popup readiness

Build 38298212260 passed the corrected identical-file import and all nine group-fit
scenarios, but the existing batch suite stopped after clicking Create node here in a
bottom-right context menu. The retained state still showed an open menu, five nodes,
an empty selection and no graph error. The old helper did not record the clicked
rectangle, so late popup placement is a hypothesis, not a proven native-event cause.

GraphControl formerly set IsContextMenuOpen before calling ShowAt. It now publishes
open state from the current flyout's native Opened event. A monotonic generation
identifies successive popup requests. Closing, replacing or disposing the popup still
clears the open state and old command references. No editing command is invoked by
these observations and no native event is synthesized by the control.

The batch test waits for the same fully onscreen command rectangle in three distinct
read-only diagnostics publications within the same open popup generation. Merely
re-reading one cached rectangle on consecutive animation frames cannot satisfy the
check. It then performs exactly one pointer click. Every clicked context target is
retained in the report. Create node here now additionally checks one revision and a
closed menu before the existing exact-model undo assertion. No retry click, arbitrary
model mutation, disabled assertion or failed-job retry is introduced.

Uno's public FlyoutBase implementation documents the lifecycle in code: presenter
Loaded raises Opened, while presenter SizeChanged can reposition the popup afterwards.
Reference consulted: https://github.com/unoplatform/uno/blob/56e430f5c56af07e86ea4b3fe782c83d2a94ac64/src/Uno.UI/UI/Xaml/Controls/Flyout/FlyoutBase.cs
This check observes stable layout in addition to native open state; it does not claim
that Opened alone is a compositor fence, nor that all platform popup timing issues
are eliminated. The failure artifact remains attributable to the preceding head.
