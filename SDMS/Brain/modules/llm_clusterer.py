"""
modules/llm_clusterer.py

Sends the full file tree to the LLM.
LLM decides where each file should go — returns {file_path: destination_folder}.
No clustering. No grouping. Just direct placement decisions.
"""

import json
import re
import urllib.request
import urllib.error
from models.file_features import FileFeature

GITHUB_API_URL = "https://models.inference.ai.azure.com/chat/completions"
MODEL          = "gpt-4.1"
BATCH_SIZE     = 100

SYSTEM_PROMPT = """\
You are a file organiser AI. Given a list of files with their current paths \
and the existing folder structure, decide where each file should be moved to.

Rules:
- Use ONLY existing folders where they make sense, or propose a new subfolder \
  under the scan root if no good folder exists
- New folder names: 2-3 words, underscores, Title_Case e.g. "Data_Science", "Course_Notes"
- If a file is already in the right place, return its current parent folder as destination
- Every file must get a destination folder
- Organise the files that are in the root (must)
- If a folder is too generic for the files ie AI for ai_assignment_1_q1 and a_assignment_1_q2 make a new folder and put them into them
- Destination must be an absolute path to a FOLDER (not a file)
- Return ONLY valid JSON — no markdown, no explanation, nothing else

Required output format:
{
  "placements": {
    "C:\\full\\path\\file.ext": "C:\\full\\destination\\folder",
    "C:\\full\\path\\file2.ext": "C:\\full\\destination\\folder2"
  }
}"""


def _build_tree_summary(features: list[FileFeature], scan_root: str) -> str:
    """
    Build a compact tree summary to give the LLM context about
    the existing folder structure alongside the file list.
    """
    folders: set[str] = set()
    for f in features:
        # Build each ancestor path from parent_chain
        for i in range(len(f.parent_chain)):
            sep = '\\' if '\\' in scan_root else '/'
            folder = scan_root + sep + sep.join(f.parent_chain[:i+1])
            folders.add(folder)

    lines = [f"Scan root: {scan_root}", "", "Existing folders:"]
    for folder in sorted(folders):
        depth = folder.count('\\') + folder.count('/') - (
            scan_root.count('\\') + scan_root.count('/')
        )
        lines.append("  " * depth + "- " + folder)

    return "\n".join(lines)


def _call_github(
    file_paths: list[str],
    tree_summary: str,
    api_key: str,
) -> dict[str, str]:
    """One API call: list of paths + tree context → {file_path: dest_folder}."""
    file_list = "\n".join(file_paths)

    user_content = (
        f"{tree_summary}\n\n"
        f"Files to place:\n{file_list}"
    )

    payload = json.dumps({
        "model": MODEL,
        "temperature": 0.2,
        "max_tokens": 4096,
        "messages": [
            {"role": "system", "content": SYSTEM_PROMPT},
            {"role": "user",   "content": user_content},
        ]
    }).encode()

    req = urllib.request.Request(
        GITHUB_API_URL,
        data=payload,
        headers={
            "Content-Type":  "application/json",
            "Authorization": f"Bearer {api_key}",
        }
    )



    with urllib.request.urlopen(req, timeout=60) as resp:
        data = json.loads(resp.read())

    text = data["choices"][0]["message"]["content"].strip()
    text = re.sub(r"^```json\s*|^```\s*|```$", "", text, flags=re.MULTILINE).strip()

    parsed = json.loads(text)
    print(parsed)
    return parsed.get("placements", {})


def _fuzzy_match(path: str, lookup: dict[str, FileFeature]) -> FileFeature | None:
    f = lookup.get(path)
    if f:
        return f
    path_lower = path.lower()
    for fp, feat in lookup.items():
        if fp.lower() == path_lower:
            return feat
    return None


def llm_place(
    features: list[FileFeature],
    scan_root: str,
    api_key: str,
) -> dict[str, str]:
    """
    Main entry point. Replaces llm_cluster().

    Returns:
        placements  dict[full_path → destination_folder]
        e.g. {"D:\\Uni\\ds\\file.ipynb": "D:\\Uni\\ds\\Notebooks"}
    """
    all_paths   = [f.full_path for f in features]
    batches     = [all_paths[i: i + BATCH_SIZE] for i in range(0, len(all_paths), BATCH_SIZE)]
    tree_summary = _build_tree_summary(features, scan_root)

    print(f"  {len(features)} files → {len(batches)} batch(es) of up to {BATCH_SIZE}")

    placements: dict[str, str] = {}

    for i, batch in enumerate(batches):
        print(f"  Batch {i + 1}/{len(batches)} ({len(batch)} files)...", end=" ", flush=True)
        try:
            result = _call_github(batch, tree_summary, api_key)
            placements.update(result)
            print(f"→ {len(result)} placements")
        except urllib.error.HTTPError as e:
            body = e.read().decode()
            print(f"FAILED — HTTP {e.code}: {body[:200]}")
        except Exception as e:
            print(f"FAILED — {e}")

    if not placements:
        print("  ERROR: all batches failed — check your GITHUB_TOKEN in .env")
        return {}

    # Fuzzy-match any paths the LLM slightly mangled
    path_lookup = {f.full_path: f for f in features}
    clean: dict[str, str] = {}
    unmatched = 0

    for raw_path, dest in placements.items():
        feat = _fuzzy_match(raw_path, path_lookup)
        if feat:
            clean[feat.full_path] = dest
        else:
            unmatched += 1

    if unmatched:
        print(f"  Warning: {unmatched} paths from LLM didn't match any file — skipped")

    print(f"  Done: {len(clean)} files placed, "
          f"{len(features) - len(clean)} unplaced (will go to _Unsorted)")

    return clean