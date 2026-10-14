# Grid dialog validation-width recovery

PR23 head `d6d1e43c526456e0e61ae6a4d7e98181ffacce9b` failed its new
stationary-footer assertion in Build 38466925081. The downloaded early
`browser-grid` artifact 11849671832 reports one successful scenario before
validation-feedback geometry changed. The native dialog itself remained open;
all other existing browser entry points passed, and package consumers were
correctly skipped by the failed app dependency.

The editor previously had only a maximum width. That still allowed intrinsic
measurement to choose different widths before and after validation text appeared.
The host now supplies a finite 420-unit preferred content width, constrained by
the current root width minus native dialog chrome space. Both width and height
are updated on root changes. Its body alone scrolls; the existing fixed feedback
height, Reset footer and native Apply/Cancel buttons are retained. Native dialog
buttons are not replaced or moved into a scrolling container.

The browser test retains exact footer-rectangle equality, containment, single
native pointer input, staged settings and one-step history assertions.
It also retains the already-open-dialog resize test at 640 by 480. No tolerance,
retry-until-pass click, longer delay or removed scenario is introduced.

Source reference for native command-area behavior:
https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/dialogs-and-flyouts/dialogs

Final execution results belong to the new head and are recorded in the PR.
This recovery does not assert pixel-identical yFiles WPF dialog templates or
physical desktop/touch input certification.
