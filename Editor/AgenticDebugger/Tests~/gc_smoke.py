"""Read-only integration checks against existing, stopped CPU Profiler data."""
import argparse
import json
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", required=True)
    args = parser.parse_args()
    checks = []

    def call(action, **kwargs):
        argv = ["unity", "command", "agentic_gc", "--project-path", args.project,
                "--format", "json", "--action", action]
        for key, value in kwargs.items():
            argv.extend(["--" + key, str(value)])
        process = subprocess.run(argv, capture_output=True, text=True, encoding="utf-8", timeout=30)
        envelope = json.loads(process.stdout)
        if process.returncode or not envelope.get("success"):
            raise RuntimeError(envelope)
        return envelope["data"]["result"]

    def check(name, condition):
        if not condition:
            raise AssertionError(name)
        checks.append(name)

    before = call("status")
    if before.get("recording") or before.get("dataState") != "frames_available":
        raise RuntimeError("Requires existing stopped Profiler data; this script does not change recording.")
    result = call("frames", count=3)
    check("frames_read", result["success"] and bool(result["frames"]))
    for frame in result["frames"]:
        if frame["dataState"] != "complete":
            check("incomplete_total_null", frame["totalBytes"] is None)
        if frame["allocationState"] == "zero":
            check("zero_has_complete_evidence", frame["dataState"] == "complete" and frame["totalBytes"] == 0)
    candidate = next((f for f in result["frames"] if f["observedAllocations"] > 0), result["frames"][-1])
    index = candidate["profilerFrameIndex"]
    detail = call("frame", frame=index, limit=3)["frame"]
    check("summary_matches_frame", detail["totalBytes"] == candidate["totalBytes"] and detail["observedAllocations"] == candidate["observedAllocations"])
    check("thread_summary", len(detail["threads"]) == detail["threadCount"])
    check("thread_sum", sum(t["knownBytes"] for t in detail["threads"]) == detail["knownBytes"])
    check("allocation_page_bound", len(detail["allocations"]) <= 3)
    allocation = None
    if detail["allocations"]:
        row = detail["allocations"][0]
        parameters = {"frame": index, "thread": row["threadIndex"], "sample": row["sampleIndex"]}
        result = call("allocation", frame_token=detail["frameToken"], **parameters)
        check("allocation_read", result["success"])
        allocation = result["allocation"]
        check("allocation_size_matches", allocation["bytes"] == row["bytes"])
        check("stack_state_explicit", allocation["callstack"]["state"] in ("unavailable", "resolved", "partial_symbols"))
        check("missing_token_rejected", call("allocation", **parameters).get("code") == "frame_token_required")
        check("stale_token_rejected", call("allocation", frame_token="stale", **parameters).get("code") == "stale_frame")
        if detail["nextAllocationOffset"] != -1:
            page = call("frame", frame=index, offset=detail["nextAllocationOffset"], limit=1)["frame"]
            check("next_page", len(page["allocations"]) == 1 and page["allocations"][0]["sampleIndex"] != row["sampleIndex"])
    missing = call("frame", frame=before["lastFrameIndex"] + 100)["frame"]
    check("missing_not_zero", missing["dataState"] == "missing" and missing["allocationState"] == "unknown" and missing["totalBytes"] is None)
    if detail["scannedSamples"] > 1:
        partial = call("frame", frame=index, max_samples=1)["frame"]
        check("budget_not_zero", partial["totalBytes"] is None and "sample_budget_exhausted" in partial["issues"])
    check("invalid_action", call("invalid").get("code") == "invalid_action")
    after = call("status")
    check("settings_unchanged", all(before[k] == after[k] for k in ("recording", "profileEditor", "connectedProfiler", "firstFrameIndex", "lastFrameIndex")))
    print(json.dumps({"passed": checks, "frame": index, "totalBytes": detail["totalBytes"],
                      "allocationCount": detail["allocationCount"], "threadCount": detail["threadCount"],
                      "allocationStackState": allocation["callstack"]["state"] if allocation else "not_exercised"}, indent=2))


if __name__ == "__main__":
    main()
