# Drawing-order menu focus and synchronized cache evidence

Run 38352349779's trace confirms native enter, press and release on Raise, and the
model revision records that it executed. The next click on the Arrange menu header
did not reopen the flyout. Drawing-order actions called graph.Focus synchronously,
before the native flyout finished restoring prior focus; unlike selection/arrangement
commands, they did not use the existing deferred focus-restoration helper. They now
use that same guarded post-unload helper. It refuses to take focus from another
editor/dialog or a detached view. No native menu implementation or command is replaced.

The actual browser sequence still opens/clicks each native menu once. After each
order command, it requires both native item unload and graph focus. A new regression
then presses ArrowRight without another pointer click, requires one edit, and undoes
to the exact pre-nudge model before continuing all previous layer/export assertions.
Native pointer traces from the earlier failed run are retained; the source correction
is not claimed to explain every historical popup timing failure.

The same run's arrangement cache assertion failed after zoom. Its retained model
has all original geometry, but the test did not retain before/after build counts.
Graph and arrangement diagnostics are separately published, so a fixed 250 ms pause
is not evidence that the two snapshots describe the same completed selection event.
Arrangement diagnostics now include read-only revision, selected IDs and focused item.
The test requires those to match the graph diagnostics before capturing the initial
count and again after zoom. It retains both records, still demands identical build
counts and the complete unchanged graph, and does not invoke or refresh the toolbar.
This removes a stale-observation ambiguity without relaxing the cache contract or
claiming a measured runtime performance fix. Final-head browser results are required.
