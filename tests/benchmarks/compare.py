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
    # Zero elapsed time is a censored sample below the timer resolution, not infinite speedup.
    speedup = b["medianMilliseconds"] / r["medianMilliseconds"] if b["medianMilliseconds"] > 0 and r["medianMilliseconds"] > 0 else None
    rows.append({"name": r["name"], "before": b, "after": r,
                 "medianSpeedup": speedup,
                 "allocationReductionPercent": 100 * (1 - r["medianAllocatedBytes"] / b["medianAllocatedBytes"]) if b["medianAllocatedBytes"] else None})
report = {"baseline": before["revision"], "current": after["revision"], "pixelHashesMatch": before["pixelSha256"] == after["pixelSha256"], "captionPixelHashesMatch": before["captionPixelSha256"] == after["captionPixelSha256"], "results": rows}
(root / "comparison.json").write_text(json.dumps(report, indent=2))
lines = ["# Same-runner performance comparison", "", before["note"], "", f"Baseline `{before['revision']}`; current `{after['revision']}`.",
         f"10,000 nodes, {before['edges']:,} edges, 10,000 ports and labels; 800 × 400 viewport at zoom 1.", "",
         "| Operation | Baseline median ms | Current median ms | Ratio | Bytes/op before → after |", "|---|---:|---:|---:|---:|"]
for row in rows:
    b, a = row["before"], row["after"]
    ratio = f"{row['medianSpeedup']:.2f}×" if row["medianSpeedup"] is not None else "Not resolved"
    current_time = f"{a['medianMilliseconds']:.6f}" if a["medianMilliseconds"] > 0 else "Below resolution"
    baseline_time = f"{b['medianMilliseconds']:.6f}" if b["medianMilliseconds"] > 0 else "Below resolution"
    lines.append(f"| {row['name']} | {baseline_time} | {current_time} | {ratio} | {b['medianAllocatedBytes']:,} → {a['medianAllocatedBytes']:,} |")
lines += ["", f"Raster fixture pixel hashes match: {report['pixelHashesMatch']}.",
          f"Separate 20-node/10-caption fixture pixel hashes match: {report['captionPixelHashesMatch']}.",
          "Zero elapsed samples are reported as below timer resolution; no speedup ratio is computed for them.",
          "Timings are descriptive and vary by runner. No timing threshold is substituted for correctness or bounded-work regression assertions."]
(root / "comparison.md").write_text("\n".join(lines) + "\n")
print("\n".join(lines))

# Persist comparison artifacts before failing any output-equivalence regression.
assert report["pixelHashesMatch"] and report["captionPixelHashesMatch"], "Raster output differs between revisions"
