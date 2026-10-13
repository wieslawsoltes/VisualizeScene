# Native menu input review

PR #15 run 38301292038 passed context-menu readiness and group fitting, then its
existing drawing-order suite waited for a GraphML download that never started.
PR #16 run 38302027893 passed all five enlargement scenarios, but an existing
Select edges menu click left node `a` selected. Both artifacts show the intended
menu still open, with no graph exception. The GraphML pointer log confirms a down
and up at (100, 190.5), matching the final menu-item rectangle; a stale rectangle
alone does not fully explain this failure. Native event receipt was not observed
in those runs, so the exact event-routing cause remains unproven.

The two affected suites now wait for three distinct, stable native diagnostics
publications, move the real mouse to that item's center, and require its actual
native PointerEntered event before the original single click. The observer uses
handledEventsToo only to record events handled by native controls. It never changes
Handled, captures the pointer, moves focus, invokes an action or alters model data.
It is enabled only for browser `?test=1` diagnostics. A bounded 32-event trace records
enter/exit/press/release. Unload resets hover; a per-instance token rejects replaced
controls. All original topology, style, download, rollback and exact-undo assertions
remain. No retries or action fallbacks are added. The immediate selection-to-Cut
regression still has no added readiness wait between selection and its direct click.

A Node contract test executes the same observation predicate with frozen snapshots,
moving bounds, replacements, unloading, clipped/invalid rectangles and read-only
application data. Live browser gates still provide the actual input validation;
these predicate tests alone cannot prove native platform correctness.

API references: Microsoft UIElement.PointerEntered and UIElement.AddHandler:
https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.pointerentered
https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.addhandler

This improves reproducibility and failure diagnosis. It is not a claim that a
production native popup defect has been conclusively identified or eliminated.
