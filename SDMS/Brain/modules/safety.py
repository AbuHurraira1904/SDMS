"""
modules/safety.py

Validates every PlanOperation before writing plan_output.json.

Rules:
  1. No overwriting an existing file
  2. No two ops targeting the same destination
  3. Source must be under scan root
  4. Destination must be under scan root
  5. No system directories
  6. No circular references (Move/MoveFolder only)
"""

from pathlib import PureWindowsPath, PurePosixPath
from modules.planner import PlanOperation
from models.file_features import FileFeature

SYSTEM_DIRS = {
    "windows", "system32", "program files", "program files (x86)",
    "programdata", "appdata", "syswow64", "winsxs",
    "$recycle.bin", "recovery", "boot",
}


def _pure(p: str):
    return PureWindowsPath(p) if '\\' in p else PurePosixPath(p)


def _is_system(path: str) -> bool:
    parts = {p.lower() for p in _pure(path).parts}
    return bool(parts & SYSTEM_DIRS)


def validate_plan(
    ops: list[PlanOperation],
    features: list[FileFeature],
    scan_root: str,
    allow_hidden: bool = False,
    allow_system: bool = False,
) -> tuple[list[PlanOperation], list[dict]]:
    """
    Returns (safe_ops, violations). Never raises.
    """
    existing_paths  = {f.full_path for f in features}
    destination_set: set[str] = set()

    safe_ops:   list[PlanOperation] = []
    violations: list[dict]          = []

    for op in ops:
        blocked = []

        # Rule 1: don't overwrite existing file
        if (
            op.destination
            and op.destination in existing_paths
            and op.destination != op.source
        ):
            blocked.append(
                f"Destination '{op.destination}' already exists — would overwrite."
            )

        # Rule 2: no duplicate destinations within this plan
        if (
            op.destination
            and op.op_type in ("Move", "MoveFolder")
            and op.destination in destination_set
        ):
            blocked.append(
                f"Destination '{op.destination}' already targeted by another op."
            )

        # Rule 3: source under scan root
        if op.source and not op.source.startswith(scan_root):
            blocked.append(
                f"Source '{op.source}' is outside scan root '{scan_root}'."
            )

        # Rule 4: destination under scan root
        if op.destination and not op.destination.startswith(scan_root):
            blocked.append(
                f"Destination '{op.destination}' is outside scan root '{scan_root}'."
            )

        # Rule 5: no system directories
        if not allow_system:
            if op.source and _is_system(op.source):
                blocked.append(f"Source '{op.source}' is in a system directory.")
            if op.destination and _is_system(op.destination):
                blocked.append(f"Destination '{op.destination}' is in a system directory.")

        # Rule 6: circular reference — Move/MoveFolder only
        # Use separator boundary: H:\Foo should not match H:\FooBar
        if op.op_type in ("Move", "MoveFolder") and op.destination and op.source:
            src_bs  = op.source.rstrip('/\\') + '\\'
            src_fwd = op.source.rstrip('/\\') + '/'
            if op.destination.startswith(src_bs) or op.destination.startswith(src_fwd):
                blocked.append(
                    f"Circular reference: '{op.destination}' is inside '{op.source}'."
                )

        if blocked:
            violations.append({"op": op.to_dict(), "blocked_reasons": blocked})
        else:
            safe_ops.append(op)
            if op.destination:
                destination_set.add(op.destination)

    return safe_ops, violations


def build_safety_summary(
    all_ops: list[PlanOperation],
    safe_ops: list[PlanOperation],
    violations: list[dict],
) -> dict:
    return {
        "total_operations":  len(all_ops),
        "safe_operations":   len(safe_ops),
        "blocked_operations": len(violations),
        "violations":        violations,
        "affected_files":    len([
            op for op in safe_ops if op.op_type in ("Move", "Delete")
        ]),
    }