"""Opt-in CLI integration check; never launches Unity Test Framework or changes scenes."""
import argparse
import json
import subprocess
import time


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", required=True)
    parser.add_argument("--source", choices=("game_view", "camera"), default="game_view")
    args = parser.parse_args()
    checks = []

    def call(action, **kwargs):
        argv = ["unity", "command", "agentic_frame_debugger", "--project-path", args.project,
                "--format", "json", "--action", action]
        for key, value in kwargs.items():
            argv.extend(["--" + key, str(value).lower() if isinstance(value, bool) else str(value)])
        process = subprocess.run(argv, capture_output=True, text=True, encoding="utf-8", timeout=25)
        envelope = json.loads(process.stdout)
        if process.returncode or not envelope.get("success"):
            raise RuntimeError(envelope)
        return envelope["data"]["result"]

    def check(name, condition):
        if not condition:
            raise AssertionError(name)
        checks.append(name)

    before = call("status")
    check("initially_disabled", before["success"] and not before["enabled"])
    check("reject_invalid_timeout", not call("capture", timeout_seconds=0)["success"])
    check("texture_export_disabled", call("export").get("code") == "raster_inspection_disabled")
    started = call("capture", timeout_seconds=120, source=args.source)
    check("capture_started", started["success"] and bool(started.get("sessionId")))
    session = started["sessionId"]
    try:
        check("reject_wrong_owner", not call("release", session_id="wrong")["success"])
        check("reject_duplicate_capture", call("capture").get("code") == "busy")
        deadline = time.monotonic() + 30
        while True:
            status = call("status")
            if status["state"] == "ready":
                break
            if time.monotonic() >= deadline:
                raise TimeoutError(status)
            time.sleep(0.3)
        page = call("events", count=8)
        check("event_page", page["success"] and 0 < len(page["events"]) <= 8)
        indices = [e["eventIndex"] for e in page["events"]]
        check("ordered_native_indices", indices == sorted(set(indices)) and indices[0] >= 0)
        check("no_raster_events", all(e["m_Type"] in ("ComputeDispatch", "RayTracingDispatch") for e in page["events"]))
        check("reject_stale_hash", not call("events", events_hash="invalid")["success"])
        check("reject_invalid_index", not call("select", session_id=session, event_index=page["total"])["success"])
        index = page["events"][-1]["eventIndex"]
        selected = call("select", session_id=session, event_index=index)
        check("selection_accepted", selected["success"])
        deadline = time.monotonic() + 20
        while True:
            detail = call("event", event_index=index, include_shader_properties=True)
            if detail["success"]:
                break
            if detail.get("code") != "pending" or time.monotonic() >= deadline:
                raise RuntimeError(detail)
            time.sleep(0.3)
        check("matching_event_data", detail["details"]["m_FrameEventIndex"] == index)
        check("shader_properties", "m_ShaderInfo" in detail["details"])
    finally:
        released = call("release", session_id=session)
        if not released["success"]:
            raise RuntimeError(released)
    after = call("status")
    check("state_restored", all(after[k] == before[k] for k in ("enabled", "playing", "paused")))
    check("session_released", after["sessionId"] is None)
    print(json.dumps({"passed": checks, "unityVersion": after["unityVersion"]}, indent=2))


if __name__ == "__main__":
    main()
