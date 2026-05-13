"""
main.py — FileTree JSON → plan_output.json

Pipeline:
  1. Parse      filetree JSON → FileFeature list
  2. LLM        places each file into a destination folder
  3. Plan       PlanOperation list
  4. Safety     validate ops
  5. Output     plan_output.json

Usage:
  python main.py filetree.json
"""

import json
import os
import sys
from collections import Counter

from dotenv import load_dotenv

from modules.llm_clusterer import llm_place
from modules.planner import build_plan
from modules.safety import validate_plan, build_safety_summary
from modules.tree_parser import parse

load_dotenv()


def run_analysis(
    filetree_json_path: str,
    allow_hidden: bool = False,
    allow_system: bool = False,
    output_path: str = "plan_output.json",
) -> dict:

    api_key = os.getenv("GITHUB_TOKEN")
    if not api_key:
        print("ERROR: GITHUB_TOKEN not set in .env")
        print("Get a token at https://github.com/settings/tokens")
        sys.exit(1)

    # ── Step 1: Parse ──────────────────────────────────────────────────────
    print("Step 1: Parsing file tree...")
    features = parse(filetree_json_path)
    print(f"  {len(features)} files found")
    if not features:
        print("No files found. Check your input JSON.")
        return {}

    # scan_root comes from the JSON directly
    with open(filetree_json_path, "r", encoding="utf-8") as f:
        _meta = json.load(f)
    scan_root = _meta.get("scanRootPath") or _meta.get("root", {}).get("fullPath", "")
    print(f"  Scan root: {scan_root}")

    # ── Step 2: LLM placement ──────────────────────────────────────────────
    print("\nStep 2: LLM placement (GitHub Models)...")
    placements = llm_place(features, scan_root, api_key)

    if not placements:
        print("No placements returned — aborting.")
        return {}

    print(f"\n  {len(placements)} files placed into folders:")
    dest_counts = Counter(placements.values())
    for dest, count in sorted(dest_counts.items(), key=lambda x: -x[1]):
        print(f"    {dest}  ← {count} file(s)")

    # ── Step 3: Planning ───────────────────────────────────────────────────
    print("\nStep 3: Planning operations...")
    all_ops = build_plan(
        placements=placements,
        features=features,
        scan_root=scan_root,
    )
    print(f"  {len(all_ops)} operations planned")

    # ── Step 4: Safety ─────────────────────────────────────────────────────
    print("\nStep 4: Safety validation...")
    safe_ops, violations = validate_plan(
        ops=all_ops,
        features=features,
        scan_root=scan_root,
        allow_hidden=allow_hidden,
        allow_system=allow_system,
    )
    safety = build_safety_summary(all_ops, safe_ops, violations)
    print(f"  {safety['safe_operations']} safe, {safety['blocked_operations']} blocked")

    if violations:
        print("  Blocked (first 5):")
        for v in violations[:5]:
            print(f"    [{v['op']['op_type']}] {v['op']['source']}")
            for r in v['blocked_reasons']:
                print(f"      → {r}")

    # ── Step 5: Output ─────────────────────────────────────────────────────
    # Debug file — human readable placement breakdown
    debug = {
        "scan_root":   scan_root,
        "total_files": len(features),
        "placements": [
            {
                "file":        file_path,
                "destination": dest,
            }
            for file_path, dest in placements.items()
        ],
    }
    with open("placement_output.json", "w", encoding="utf-8") as f:
        json.dump(debug, f, indent=2, default=str)

    # Main output — C# reads this
    plan_output = {
        "scan_root":           scan_root,
        "total_files_scanned": len(features),
        "safety":              safety,
        "operations":          [op.to_dict() for op in safe_ops],
    }
    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(plan_output, f, indent=2, default=str)

    print(f"\nDone.")
    print(f"  placement_output.json → debug (human readable)")
    print(f"  {output_path:<20} → C# reads this")

    print("\nOperation breakdown:")
    op_counts = Counter(op.op_type for op in safe_ops)
    for op_type, count in op_counts.most_common():
        print(f"  {op_type}: {count}")

    return plan_output


if __name__ == "__main__":
    path = sys.argv[1] if len(sys.argv) > 1 else "filetree3.json"
    print("path: ", path)
    run_analysis(path)