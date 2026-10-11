"""Summarize same-harness measurements; correctness/complexity gates live in managed tests."""
import json
from pathlib import Path
import sys

root = Path(sys.argv[1])
before = json.loads((root / "baseline.json").read_text())
after = json.loads((root / "current.json").read_text())
assert before["runtime"] == after["runtime"] and before["nodes"] == after["nodes"]
old = {r["name"]: r for r in before["results"]}
rows = []
for r in after["results"]:
    b = old[r["name"]]
    rows.append({"name": r["name"], "before": b, "after": r,
                 "medianSpeedup": b["medianMilliseconds"] / max(r["medianMilliseconds"], 1e-12),
                 "allocationReductionPercent": 100 * (1 - r["medianAllocatedBytes"] / b["medianAllocatedBytes"]) if b["medianAllocatedBytes"] else None})
report = {"baseline": before["revision"], "current": after["revision"], "pixelHashesMatch": before["pixelSha256"] == after["pixelSha256"], "results": rows}
(root / "comparison.json").write_text(json.dumps(report, indent=2))
lines = ["# Same-runner performance comparison", "", before["note"], "", f"Baseline `{before['revision']}`; current `{after['revision']}`.",
         f"10,000 nodes, {before['edges']:,} edges, 10,000 ports and labels; 800 × 400 viewport at zoom 1.", "",
         "| Operation | Baseline median ms | Current median ms | Ratio | Bytes/op before → after |", "|---|---:|---:|---:|---:|"]
for row in rows:
    b, a = row["before"], row["after"]
    lines.append(f"| {row['name']} | {b['medianMilliseconds']:.6f} | {a['medianMilliseconds']:.6f} | {row['medianSpeedup']:.2f}× | {b['medianAllocatedBytes']:,} → {a['medianAllocatedBytes']:,} |")
lines += ["", f"Raster fixture pixel hashes match: {report['pixelHashesMatch']}.",
          "Timings are descriptive and vary by runner. No timing threshold is substituted for correctness or bounded-work regression assertions."]
(root / "comparison.md").write_text("\n".join(lines) + "\n")
print("\n".join(lines))
