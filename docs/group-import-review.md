# Group-fit import regression

Run 38281310019 passed all managed tests and the first seven group-fitting browser
scenarios. Its final scenario re-opened the JSON just saved at narrow width. Import
completed, but the previous five group IDs remained selected and the fit command
remained enabled. The retained state recorded `Graph imported.`, no graph error,
and the old selection: this was not a file-picker timeout.

The workbench import path previously relied on document changes to prune selection.
IDs shared with the imported graph survived, and equivalent graph data deliberately
emits no new revision. Imports now explicitly return to Select mode, cancel unfinished
input, clear selection and keyboard item focus, then fit the viewport. Parsing and
revision validation still happen before this reset, so rejected files cannot reset
input through this path. GraphDocument's equivalent-model suppression is unchanged.
Opening the same saved data does not manufacture an undo entry.

The native browser assertion is retained and strengthened: identical JSON import
must keep the graph revision unchanged and reset Copy/Cut/Delete, arrangement
availability, keyboard item focus and selection. Actual file input is still used;
there are no new diagnostic mutation hooks or retry clicks. Only final-head CI
results establish whether the corrected integration passes.
