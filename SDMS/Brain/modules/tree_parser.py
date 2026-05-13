"""
modules/tree_parser.py

Parses the FileTree JSON produced by the C# scanner into a flat
list of FileFeature objects.

Expected format:
{
  "scanRootPath": "D:\\Uni\\ds",
  "root": {
    "name": "ds",
    "fullPath": "D:\\Uni\\ds",
    "isDirectory": true,
    "children": [ ... ],   ← recursive
    ...
  }
}

Files have: name, fullPath, isDirectory=false, sizeBytes,
            mimeType, extension, parentChain, numSiblings
"""

import json
from models.file_features import FileFeature


def _walk(node: dict, out: list[dict]) -> None:
    """
    Recursively walk the tree.
    Collect only file nodes (isDirectory = false).
    Directory nodes are traversed but not collected.
    """
    if not node.get("isDirectory", True):
        out.append(node)
    for child in node.get("children", []):
        _walk(child, out)


def parse(filetree_json_path: str) -> list[FileFeature]:
    with open(filetree_json_path, "r", encoding="utf-8") as f:
        data = json.load(f)

    # ── Root path ──────────────────────────────────────────────────────────
    scan_root = data.get("scanRootPath", "")
    root_node = data.get("root")

    if not root_node:
        print("ERROR: JSON must have a 'root' key with the directory tree.")
        return []

    if not scan_root:
        # Fall back to fullPath of the root node
        scan_root = root_node.get("fullPath", "")

    # ── Flatten tree → raw file dicts ──────────────────────────────────────
    raw: list[dict] = []
    _walk(root_node, raw)

    if not raw:
        print("No files found in tree.")
        return []

    # ── Build FileFeature objects ──────────────────────────────────────────
    features: list[FileFeature] = []

    for node in raw:
        name      = node.get("name", "")
        full_path = node.get("fullPath", "")

        if not name or not full_path:
            continue

        # parentChain is already provided by C# — e.g. ["assignment", "assignment_1", "data"]
        parent_chain: list[str] = node.get("parentChain", [])

        # extension: C# gives it without the dot ("csv"), normalise to ".csv"
        raw_ext   = node.get("extension", "")
        extension = f".{raw_ext}" if raw_ext and not raw_ext.startswith(".") else raw_ext

        mime_type     = node.get("mimeType", "other") or "other"
        size_bytes    = int(node.get("sizeBytes", 0))
        sibling_count = int(node.get("numSiblings", 0))
        folder_name   = parent_chain[-1] if parent_chain else ""
        depth         = len(parent_chain)   # depth relative to scan root

        features.append(FileFeature(
            name          = name,
            full_path     = full_path,
            extension     = extension,
            mime_type     = mime_type,
            size_bytes    = size_bytes,
            depth         = depth,
            parent_chain  = parent_chain,
            sibling_count = sibling_count,
            folder_name   = folder_name,
        ))

    return features