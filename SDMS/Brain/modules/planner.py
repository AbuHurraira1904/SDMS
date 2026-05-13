"""
modules/planner.py

Turns LLM placement decisions into PlanOperation list.
Input:  dict[file_path → destination_folder]
Output: list[PlanOperation]
"""

from collections import Counter
from dataclasses import dataclass
from pathlib import PureWindowsPath, PurePosixPath
from typing import Optional

from models.file_features import FileFeature


@dataclass
class PlanOperation:
    op_type: str
    source: str
    destination: Optional[str]
    reason: str
    confidence: float
    importance: float
    status: str = "Pending"

    def to_dict(self) -> dict:
        return {
            "op_type":     self.op_type,
            "source":      self.source,
            "destination": self.destination,
            "reason":      self.reason,
            "confidence":  round(self.confidence, 4),
            "importance":  round(self.importance, 4),
            "status":      self.status,
        }


def _sep(path: str) -> str:
    return '\\' if '\\' in path else '/'


def _folder_already_exists(folder: str, features: list[FileFeature]) -> bool:
    norm = folder.lower().rstrip('/\\')
    for f in features:
        fp = f.full_path.lower()
        if fp.startswith(norm + '\\') or fp.startswith(norm + '/'):
            return True
    return False


def _file_already_there(file_path: str, dest_folder: str) -> bool:
    norm = dest_folder.lower().rstrip('/\\')
    fp   = file_path.lower()
    # File is already directly inside dest_folder (not deeper)
    remainder = fp[len(norm):]
    if not (fp.startswith(norm + '\\') or fp.startswith(norm + '/')):
        return False
    # Make sure it's not in a subfolder of dest
    inner = remainder.lstrip('/\\')
    return '\\' not in inner and '/' not in inner


def _duplicated_names(items: list[tuple[str, str]]) -> set[str]:
    """items = list of (file_path, dest_folder) — find filenames that collide in same dest."""
    from pathlib import PureWindowsPath, PurePosixPath
    dest_names: dict[str, list[str]] = {}
    for file_path, dest in items:
        p    = PureWindowsPath(file_path) if '\\' in file_path else PurePosixPath(file_path)
        key  = (dest.lower(), p.name.lower())
        dest_names.setdefault(key, []).append(file_path)
    return {
        key[1] for key, paths in dest_names.items() if len(paths) > 1
    }


def build_plan(
    placements: dict[str, str],        # file_path → dest_folder
    features: list[FileFeature],
    scan_root: str,
) -> list[PlanOperation]:
    """
    Build operation list from LLM placement decisions.

    For each file:
      - Already in dest folder → skip
      - Dest folder doesn't exist → emit NewFolder first, then Move
      - Otherwise → emit Move

    Unplaced files → _Unsorted
    """
    ops: list[PlanOperation] = []
    placed_paths  = set(placements.keys())
    new_folders_emitted: set[str] = set()

    # Collision detection: filename conflicts per destination
    dupe_names = _duplicated_names(list(placements.items()))

    feature_map = {f.full_path: f for f in features}

    for file_path, dest_folder in placements.items():
        feat = feature_map.get(file_path)
        if not feat:
            continue

        # Skip if already in the right place
        if _file_already_there(file_path, dest_folder):
            continue

        s    = _sep(scan_root)
        name = feat.name

        # Emit NewFolder if needed
        if (
            not _folder_already_exists(dest_folder, features)
            and dest_folder not in new_folders_emitted
        ):
            ops.append(PlanOperation(
                op_type     = "NewFolder",
                source      = scan_root,
                destination = dest_folder,
                reason      = f"New folder required for organised files.",
                confidence  = 0.9,
                importance  = 0.9,
            ))
            new_folders_emitted.add(dest_folder)

        # Disambiguate filename if it collides in this destination
        if name.lower() in dupe_names:
            folder_tag = feat.folder_name or "root"
            dest_path  = f"{dest_folder}{s}{folder_tag}_{name}"
        else:
            dest_path  = f"{dest_folder}{s}{name}"

        importance = round(
            min(0.5 + feat.size_bytes / 10_000_000, 1.0), 4
        )

        ops.append(PlanOperation(
            op_type     = "Move",
            source      = file_path,
            destination = dest_path,
            reason      = f"AI organiser: move to '{dest_folder.split(s)[-1]}'.",
            confidence  = 0.85,
            importance  = importance,
        ))

    # Unplaced files → _Unsorted
    unplaced = [f for f in features if f.full_path not in placed_paths]
    if unplaced:
        s        = _sep(scan_root)
        unsorted = f"{scan_root}{s}_Unsorted"

        if unsorted not in new_folders_emitted:
            ops.append(PlanOperation(
                op_type     = "NewFolder",
                source      = scan_root,
                destination = unsorted,
                reason      = f"{len(unplaced)} file(s) were not placed by AI.",
                confidence  = 0.7,
                importance  = 0.3,
            ))

        for f in unplaced:
            ops.append(PlanOperation(
                op_type     = "Move",
                source      = f.full_path,
                destination = f"{unsorted}{_sep(scan_root)}{f.name}",
                reason      = "Not placed by AI organiser.",
                confidence  = 0.5,
                importance  = 0.2,
            ))

    # NewFolder ops first, then Moves sorted by importance
    new_folder_ops = [op for op in ops if op.op_type == "NewFolder"]
    move_ops       = sorted(
        [op for op in ops if op.op_type != "NewFolder"],
        key=lambda op: op.importance,
        reverse=True,
    )

    return new_folder_ops + move_ops