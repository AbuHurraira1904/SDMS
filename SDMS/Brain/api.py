"""
api.py — Flask wrapper around the brain pipeline.
Exposes two endpoints that BrainClient.cs talks to:
  GET  /status   → {"status": "ok"}
  POST /analyze  → plan_output dict
"""

import os
import tempfile
from flask import Flask, request, jsonify
from main import run_analysis

app = Flask(__name__)


@app.get("/status")
def status():
    return jsonify({"status": "ok"})


@app.post("/analyze")
def analyze():
    body = request.get_json(force=True)

    filetree_json_path = body.get("filetree_json_path")
    if not filetree_json_path:
        return jsonify({"error": "filetree_json_path is required"}), 400

    if not os.path.isfile(filetree_json_path):
        return jsonify({"error": f"File not found: {filetree_json_path}"}), 400

    allow_hidden = bool(body.get("allow_hidden", False))
    allow_system = bool(body.get("allow_system", False))

    # Write plan output next to the input file so paths stay predictable
    output_path = os.path.join(
        os.path.dirname(filetree_json_path), "plan_output.json"
    )

    try:
        plan = run_analysis(
            filetree_json_path=filetree_json_path,
            allow_hidden=allow_hidden,
            allow_system=allow_system,
            output_path=output_path,
        )
    except SystemExit as e:
        # run_analysis calls sys.exit(1) if GROQ_API_KEY is missing
        return jsonify({"error": "GROQ_API_KEY not set in .env"}), 500
    except Exception as e:
        return jsonify({"error": str(e)}), 500

    if not plan:
        return jsonify({"error": "Analysis produced empty plan"}), 500

    # Rename keys to match what PlanOutput.cs expects (camelCase / PascalCase
    # doesn't matter — BrainClient uses PropertyNameCaseInsensitive = true)
    return jsonify({
        "ScanRoot":           plan["scan_root"],
        "TotalFilesScanned": plan["total_files_scanned"],
        "Safety": {
            "TotalOperations":   plan["safety"]["total_operations"],
            "SafeOperations":    plan["safety"]["safe_operations"],
            "BlockedOperations": plan["safety"]["blocked_operations"],
            "AffectedFiles":     plan["safety"]["affected_files"],
        },
        "Operations": [
            {
                "OpType":     op["op_type"],
                "Source":      op["source"],
                "Destination": op.get("destination"),
                "Reason":      op["reason"],
                "Confidence":  op["confidence"],
                "Importance":  op["importance"],
                "Status":      op["status"],
            }
            for op in plan["operations"]
        ],
    })


if __name__ == "__main__":
    # debug=False in production; threaded=False keeps pipeline calls serial
    app.run(host="127.0.0.1", port=5000, debug=False, threaded=False)