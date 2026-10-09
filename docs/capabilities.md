# Capability matrix

This is an implementation inventory, not a statement of yFiles parity. “Implemented” identifies code paths in this repository; validation evidence is reported separately. Public yFiles WPF documentation defines a much broader SDK surface than this initial release. There is no source, binary, API, screenshot, or algorithm-output equivalence claim.

| Area | Implemented | Boundaries / missing work |
|---|---|---|
| Graph model | Immutable node/edge/port/label records; multigraphs; directed/undirected edges; self-loops; IDs; validation; revision events | Not yFiles IGraph API-compatible; no lookup/decorator framework or arbitrary typed tag serialization |
| Editing | Selection/marquee; node creation/move/resize; two-click edges; bend insertion/move; label edit; deletion; snapping; alignment; internal clipboard/duplicate | No full input-mode hierarchy, port relocation gestures, edge reconnection handles, smart guides or magnetic alignment |
| History | Atomic edits; rollback; bounded undo/redo; one history entry per drag/layout; stale revision rejection | Snapshot history can consume substantial memory; no persistent collaborative history or operation merging |
| Groups | Nested ownership; subtree movement/removal; grouping/ungrouping; folding; visible ancestor edge projection | No navigation into group graphs, alternate collapsed sizing, folder proxy metadata or table-style group nodes |
| Ports | Relative port model; edge endpoint ownership checks; display/hit testing; connection creation | No complete port candidate/constraint systems, custom port shapes or side-allocation optimization |
| Labels | Node/edge captions; independent owner labels; offset positions; HarfBuzz-shaped text; multiline wrapping and truncation | No full label models, automatic conflict-free label placement, rich text or comprehensive multilingual font fallback |
| Styles | Six node shapes; colors; strokes; arrows; group headers; selection handles | No yFiles XAML styles/templates, all arrow/line styles, image nodes, custom decorators, BPMN/UML/table/complex business styles |
| Hierarchical layout | SCC condensation; rank assignment; barycenter crossing sweeps; variable sizes; compound layout | No full incremental constraints, swimlanes, partition grids, bus structures, node/edge constraints, advanced edge bundling or yFiles-equivalent quality |
| Other layouts | Tree spanning forests; radial rings; circular; variable-size grid; seeded Barnes-Hut organic | Not all yFiles layout families; no balloon/series-parallel/orthogonal-node/compact-disk/multipage/mixed layout engines |
| Routing | Simple polyline/orthogonal paths; manual bends; obstacle-aware orthogonal A* with clearance and bend costs | Self-loop obstacle routing unsupported; search-budget exhaustion diagnosed; no bus/spline/bundled routing or full crossing optimization |
| Analysis | BFS; weak/strong components; topological order; Dijkstra; Bellman-Ford; Kruskal forest; PageRank; unweighted Brandes; DSATUR; Edmonds-Karp flow/min-cut | Not the full yFiles analysis catalog; coloring is heuristic; no planarity, isomorphism, exact coloring, clustering or all centrality variants |
| Rendering | Uno shared Skia canvas; retained node pictures; culling; level-of-detail; shaped labels; selection and overview | Backend chosen by Uno host; no direct WebGPU renderer; no physical-GPU performance certification; indexes rebuild on changed snapshots |
| Controls | GraphControl; GraphOverview; NodePalette; GraphPropertyInspector | Not WPF/yFiles drop-in controls; no complete accessibility/UI Automation graph peer tree, touch pinch or drag/drop palette parity |
| Workbench | Menu/toolbar; palette; inspector; layout/analysis panels; examples; responsive rails; keyboard commands | GraphEditor-inspired original UI, not screenshot-identical; no dockable workspace, multi-document tabs or original icon reproduction |
| JSON | Versioned native round trip; source-generated serialization; input-size validation | Schema is VisualizeScene-specific; no arbitrary extension payload persistence |
| GraphML | Native structured round trip; plain graph topology; groups; ports; selected legacy yWorks shape/geometry/bend data | Partial yFiles interchange only; unknown extension keys reported; specialized data/styles not retained; nesting limit 128 |
| Export | Standalone SVG; PNG with allocation guard; JSON; GraphML | No PDF/XPS, printing, poster tiling, multipage output, full label overflow bounds or output font embedding |
| Platforms | Uno desktop and WebAssembly projects using the same libraries and control implementation | Only reported build/test configurations count as validated; native Windows/macOS execution and physical GPU tests must be verified independently |
| Packaging | Seven separable NuGet projects; source and browser build artifacts | Package artifacts are not the same as a release published to nuget.org; no package feed publication without explicit credentials |

## Parity validation still required

A genuine parity release would require a version-pinned yFiles WPF reference build, licensed test fixtures, an enumerated API/behavior matrix, screenshot baselines, layout-quality benchmarks, import/export round trips, accessibility tests, platform tests, performance budgets and all corresponding implementations. The current tests cover this repository's contracts, not the complete commercial SDK.
