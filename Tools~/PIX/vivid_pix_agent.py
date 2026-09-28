"""Bounded PIX agent workflow; Python standard library, Unity CLI and PIX only."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import time
import uuid


class Failure(RuntimeError):
    def __init__(self, code, message):
        super().__init__(message)
        self.code = code


def save(path, value):
    with Path(path).open("x", encoding="utf-8") as stream:
        json.dump(value, stream, ensure_ascii=False, indent=2)


def canonical(path):
    return os.path.normcase(os.path.realpath(path))


def pix_command_line(arguments):
    # pixtool parses its raw command line itself: quote the value after '='.
    # Quoting the entire --command-line=... token makes 2606 reject the option.
    tokens = []
    for argument in arguments:
        if argument.startswith("--") and "=" in argument:
            key, value = argument.split("=", 1)
            tokens.append(key + "=" + subprocess.list2cmdline([value]))
        else:
            tokens.append(subprocess.list2cmdline([argument]))
    return " ".join(tokens)


def run_json(arguments, timeout):
    try:
        result = subprocess.run(arguments, capture_output=True, text=True, encoding="utf-8",
                                errors="replace", timeout=timeout, creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
        data = json.loads(result.stdout)
    except subprocess.TimeoutExpired as e:
        raise Failure("command_timeout", "Command deadline exceeded: " + arguments[0]) from e
    except (OSError, ValueError) as e:
        raise Failure("invalid_command_result", str(e)) from e
    if result.returncode or data.get("success") is not True:
        raise Failure("command_failed", json.dumps(data.get("errors", data), ensure_ascii=False)[:4096])
    return data


def unity(args, action, _timeout=None, **parameters):
    command = [args.unity, "command", "agentic_gpu_debugger", "--project-path", args.project,
               "--backend", "pix", "--action", action, "--format", "json"]
    for key, value in parameters.items():
        if value is not None:
            command += ["--" + key, str(value)]
    result = run_json(command, min(45, args.timeout, _timeout if _timeout is not None else args.timeout))
    if result.get("data", {}).get("success") is False:
        raise Failure("editor_command_failed", str(result["data"])[:4096])
    return result["data"]["result"]


def validate_preflight(context, pix):
    if context.get("editorBusy"):
        raise Failure("editor_busy", "Wait for Editor compilation/import to finish.")
    if context.get("graphicsApi") != "Direct3D12" or context.get("pipelineType") != "VividRP.Runtime.VividRenderPipelineAsset":
        raise Failure("wrong_render_environment", "A D3D12 VividRP Editor is required.")
    if not context.get("nativeAvailable") or not context.get("pixRuntime"):
        raise Failure("requires_early_injection", "Launch the Editor through the PIX launch subcommand before D3D12 creation.")
    if canonical(context["pixRuntime"]) != canonical(Path(pix) / "WinPixGpuCapturer.dll"):
        raise Failure("pix_version_mismatch", "Injected PIX runtime and analyzer installation must match.")
    if context.get("sessionOwned"):
        raise Failure("busy", "Another capture owns this Editor; release it with its owner token first.")


def launch(args, directory):
    # Refuse an existing Editor, including one still starting up. No automatic
    # restart/kill of user sessions. The Unity lock is an additional safeguard.
    lock = Path(args.project) / "Temp/UnityLockfile"
    if lock.exists():
        raise Failure("editor_already_open", "UnityLockfile exists. Close the project normally before PIX launch.")
    editor = Path(args.editor).resolve()
    if not editor.is_file():
        raise Failure("editor_missing", str(editor))
    cli = Path(args.pix) / "pixtool.exe"
    arguments = [str(cli), "--log-file=" + str(directory / "pixtool.log"), "launch", str(editor),
                 "--working-directory=" + args.project,
                 "--command-line=" + subprocess.list2cmdline(["-projectPath", args.project, "-force-d3d12",
                                                              "-logFile", str(directory / "Editor.log")]),
                 "programmatic-capture", "--until-exit"]
    with (directory / "launch.stdout.log").open("wb") as stdout, (directory / "launch.stderr.log").open("wb") as stderr:
        process = subprocess.Popen(pix_command_line(arguments) if os.name == "nt" else arguments, stdout=stdout, stderr=stderr,
                                   creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
    save(directory / "launch.json", {"launcherPid": process.pid, "arguments": arguments,
                                     "project": args.project, "editor": str(editor)})
    deadline = time.monotonic() + args.deadline
    last = "Editor not connected"
    while time.monotonic() < deadline:
        if process.poll() is not None:
            raise Failure("pix_launch_exited", "Inspect launch.stdout.log and pixtool.log.")
        try:
            context = unity(args, "preflight")
            validate_preflight(context, args.pix)
            save(directory / "preflight.json", context)
            return {"success": True, "state": "ready", "editorPid": context["processId"],
                    "launcherPid": process.pid, "evidenceDirectory": str(directory)}
        except Failure as e:
            last = str(e)
        time.sleep(1)
    # The launcher may own a live Editor. Never kill it without inspecting state;
    # publish its PID/logs and don't launch an automatic duplicate on timeout.
    raise Failure("editor_start_timeout", last)


def select_event(rows, queue=None, event=None):
    if (queue is None) != (event is None):
        raise Failure("event_identity_required", "Supply both --queue and --event.")
    candidates = [row for row in rows if row.get("gpuWork") is True]
    if queue is not None:
        candidates = [row for row in candidates if row.get("queueId") == queue and row.get("eventIndex") == event]
    if not candidates:
        raise Failure("target_event_missing", "No validated GPU work matched the bounded marker query/explicit event.")
    # This is a reproducible inspection choice, not a claim about which event
    # dominates performance. Queue indices are never treated as a global clock.
    return candidates[0]


class Workflow:
    def __init__(self, args, directory):
        self.args, self.directory = args, directory
        self.deadline = time.monotonic() + args.deadline
        self.capture = self.session = self.capture_hash = None
        self.mode = None
        self.evidence, self.limitations, self.attempts = [], [], []
        self.sequence = 0

    def budget(self):
        remaining = self.deadline - time.monotonic()
        if remaining < 1:
            raise Failure("workflow_timeout", "Overall workflow deadline exceeded.")
        return min(self.args.timeout, int(remaining))

    def command(self, action, **kwargs):
        value = unity(self.args, action, _timeout=self.budget(), **kwargs)
        self.sequence += 1
        name = f"{self.sequence:03}-{action}.json"
        save(self.directory / name, value)
        return value

    def release(self):
        if not self.session:
            return
        until = time.monotonic() + 20
        while time.monotonic() < until:
            result = unity(self.args, "release", _timeout=max(1, until - time.monotonic()), session_id=self.session)
            if result.get("state") == "idle":
                self.session = None
                return
            if result.get("code") == "session_mismatch":
                raise Failure("owner_lost", "Capture ownership changed; do not release another session.")
            time.sleep(0.25)
        raise Failure("cleanup_pending", "Native cleanup is still pending; poll/release the retained session token.")

    def console(self, name, cursor=None):
        command = [self.args.unity, "command", "console", "--project-path", self.args.project,
                   "--level", "error", "--tail", "32", "--format", "json"]
        if cursor:
            command += ["--since", str(cursor["cursor"]), "--since_session", cursor["session"]]
        value = run_json(command, self.budget())["data"]["result"]
        save(self.directory / name, value)
        return value

    def take_capture(self):
        context = self.command("preflight")
        validate_preflight(context, self.args.pix)
        save(self.directory / "context.json", context)
        for attempt in range(self.args.attempts):
            cursor = self.console(f"console-before-{attempt + 1}.json")
            self.capture = str(self.directory / f"capture-{attempt + 1}.wpix")
            result = self.command("capture", path=self.capture, camera=self.args.camera,
                                  expected_pass=self.args.marker, timeout_seconds=self.budget(),
                                  pix_install=self.args.pix, analyzer_path=self.args.analyzer)
            self.session = result.get("sessionId")
            until = time.monotonic() + min(self.args.timeout + 5, self.deadline - time.monotonic())
            while result.get("pending") and time.monotonic() < until:
                time.sleep(0.3)
                result = self.command("status", session_id=self.session)
            self.attempts.append({"attempt": attempt + 1, "sessionId": self.session, "capturePath": self.capture,
                                  "state": result.get("state"), "code": result.get("code")})
            if result.get("ready") is True and result.get("success") is True:
                console = self.console(f"console-after-{attempt + 1}.json", cursor)
                if console.get("reset") or console.get("dropped") or console.get("session") != cursor.get("session"):
                    raise Failure("console_evidence_incomplete", "Console cursor changed/lost entries during capture.")
                if console.get("entries"):
                    raise Failure("editor_errors_during_capture", "New Unity errors occurred during capture; inspect console evidence.")
                self.mode = result["boundaryMode"]
                # Keep immutable analysis identity after releasing native ownership.
                captured_session = self.session
                save(self.directory / "capture-ready.json", result)
                self.release()
                self.capture_session = captured_session
                return
            code = result.get("code", "capture_timeout")
            self.release()
            # Never retry semantic/empty captures as though a later success could
            # erase evidence. Only an explicit transient Editor precondition retries.
            if code != "editor_busy" or attempt + 1 == self.args.attempts:
                raise Failure(code, "Capture did not pass the content gate; see capture/status evidence.")
            time.sleep(0.5)
        raise Failure("capture_failed", "Capture attempts exhausted.")

    def analyze(self, action, optional=False, **options):
        timeout = self.budget()
        self.sequence += 1
        request = uuid.uuid4().hex
        name = f"{self.sequence:03}-{action}.json"
        output = self.directory / name
        arguments = [self.args.analyzer, action, self.args.pix, self.capture, self.capture_session,
                     self.args.marker, str(output), self.mode, "--request_id", request,
                     "--timeout_seconds", str(timeout), "--count", str(self.args.page_size)]
        if self.capture_hash:
            arguments += ["--capture_hash", self.capture_hash]
        for key, value in options.items():
            if value is not None:
                arguments += ["--" + key, str(value)]
        try:
            with output.with_suffix(".stderr.log").open("wb") as errors:
                result = subprocess.run(arguments, stdout=subprocess.DEVNULL, stderr=errors, timeout=timeout + 5,
                                        creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
            if output.stat().st_size > 8 * 1024 * 1024:
                raise Failure("oversized_result", name)
            value = json.loads(output.read_text(encoding="utf-8-sig"))
        except (OSError, ValueError, subprocess.TimeoutExpired) as e:
            raise Failure("analysis_process_failed", f"{action}: {e}") from e
        expected = {"schemaVersion": 3, "action": action, "requestId": request, "sessionId": self.capture_session,
                    "expectedPass": self.args.marker, "boundaryMode": self.mode,
                    "queueId": options.get("queue"), "eventIndex": options.get("event")}
        if any(value.get(key) != required for key, required in expected.items()) or canonical(value.get("capturePath", "")) != canonical(self.capture):
            raise Failure("analysis_identity_mismatch", name)
        self.evidence.append({"action": action, "path": str(output), "code": value.get("code")})
        if result.returncode or value.get("success") is not True or value.get("code") != "ok":
            if optional and value.get("unsupported") is True and value.get("hresult", 0) < 0:
                self.limitations.append({"action": action, "code": value.get("code"), "hresult": value.get("hresult")})
                return None
            code = value.get("code", "analysis_failed")
            raise Failure("analysis_process_failed" if code == "ok" else code, name)
        digest = value.get("captureHash", "")
        if len(digest) != 64 or any(c not in "0123456789abcdef" for c in digest.lower()) or (self.capture_hash and self.capture_hash != digest):
            raise Failure("capture_hash_mismatch", name)
        self.capture_hash = digest
        return value["data"]

    def inspect(self):
        self.take_capture()
        rows, offset, total = [], 0, 0
        while len(rows) < self.args.max_events:
            page = self.analyze("events", marker=self.args.marker, offset=offset)
            rows.extend(page["items"][:self.args.max_events - len(rows)])
            total = page["total"]
            if page["nextOffset"] is None:
                break
            if page["nextOffset"] <= offset:
                raise Failure("invalid_pagination", "Events did not advance.")
            offset = page["nextOffset"]
        event = select_event(rows, self.args.queue, self.args.event)
        selection = {"queue": event["queueId"], "event": event["eventIndex"]}
        detail = self.analyze("event", **selection)
        pipeline = self.analyze("pipeline", **selection)
        resources = self.analyze("resources", **selection)
        timing = self.analyze("timing", optional=True, **selection) if self.args.timing else None
        accessed = self.analyze("accessed_resources", optional=True, **selection) if self.args.accessed else None
        occupancy = self.analyze("occupancy", optional=True, **selection) if self.args.occupancy else None
        counters = []
        wanted = set(self.args.counter)
        if wanted:
            offset, found = 0, {}
            while offset < 4096:
                page = self.analyze("counters", offset=offset)
                for counter in page["items"]:
                    if counter["name"] in wanted:
                        if counter["name"] in found:
                            raise Failure("ambiguous_counter", counter["name"])
                        found[counter["name"]] = counter["id"]
                if wanted.issubset(found):
                    break
                if page["nextOffset"] is None:
                    break
                if page["nextOffset"] <= offset:
                    raise Failure("invalid_pagination", "Counters did not advance.")
                offset = page["nextOffset"]
            for name in self.args.counter:
                if name not in found:
                    self.limitations.append({"action": "counters", "code": "counter_not_found_in_bounded_catalog", "name": name})
                    continue
                data = self.analyze("counters", optional=True, counter=found[name], **selection)
                counters.append(data)
                if data and data.get("available") and not data["value"].get("decoded"):
                    self.limitations.append({"action": "counters", "code": "unknown_counter_format", "name": name, "format": data["value"]["format"]})
        views = resources.get("views", {})
        root = pipeline.get("rootSignature") or {}
        resource_summary = []
        for view in views.get("items", [])[:8]:
            resource = view.get("resource") or {}
            resource_summary.append({"viewIndex": view.get("viewIndex"), "viewType": view.get("type"),
                                     "resourceId": resource.get("resourceId"), "name": resource.get("name", "")[:160],
                                     "format": resource.get("format"), "width": resource.get("width"), "height": resource.get("height"),
                                     "bindings": view.get("bindings", [])[:4]})
        return {"success": True, "state": "complete", "question": self.args.question,
                "capturePath": self.capture, "captureHash": self.capture_hash, "sessionId": self.capture_session,
                "marker": self.args.marker, "event": event, "selectionPolicy": "explicit" if self.args.event is not None else "first_validated_work_under_marker",
                "eventsReturned": len(rows), "eventTotal": total, "eventsTruncated": total > len(rows),
                "programType": pipeline.get("programType"), "shaderCount": pipeline.get("shaderCount"),
                "rootSignatureId": root.get("id"), "rootParameterCount": root.get("description", {}).get("parameterCount"),
                "shaders": pipeline.get("shaders", [])[:16], "resourceSummary": resource_summary,
                "resourceViews": views.get("total"), "resourcesReturned": len(views.get("items", [])),
                "resourcesTruncated": views.get("nextOffset") is not None, "resourceAccess": resources.get("access"),
                "timing": timing, "counters": counters, "accessedResourcesQueried": accessed is not None,
                "occupancyQueried": occupancy is not None,
                "assessment": "Capture content gate passed; selected event inspection completed. No bottleneck attribution is inferred from a single event or replay.",
                "apiCallDataTruncated": detail.get("apiCallDataTruncated", False)}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["launch", "diagnose"])
    parser.add_argument("--project", required=True)
    parser.add_argument("--pix", required=True)
    parser.add_argument("--unity", default="unity")
    parser.add_argument("--output", required=True, help="New evidence directory; never overwritten")
    parser.add_argument("--editor", help="Matching Unity.exe for launch")
    parser.add_argument("--analyzer", default=str(Path(__file__).parent / "bin/vivid-pix-analyzer.exe"))
    parser.add_argument("--camera", default="Main Camera")
    parser.add_argument("--marker")
    parser.add_argument("--question", default="Inspect the selected VividRP pass")
    parser.add_argument("--counter", action="append", default=[], help="Exact counter name, at most four; requested only when relevant")
    parser.add_argument("--timing", action="store_true")
    parser.add_argument("--accessed", action="store_true")
    parser.add_argument("--occupancy", action="store_true")
    parser.add_argument("--queue", type=int)
    parser.add_argument("--event", type=int)
    parser.add_argument("--page-size", type=int, default=128)
    parser.add_argument("--max-events", type=int, default=512)
    parser.add_argument("--timeout", type=int, default=120)
    parser.add_argument("--deadline", type=int, default=600)
    parser.add_argument("--attempts", type=int, default=2)
    args = parser.parse_args(argv)
    if not 1 <= args.page_size <= 256 or not args.page_size <= args.max_events <= 4096 or not 1 <= args.timeout <= 300 or not 1 <= args.deadline <= 1800 or not 1 <= args.attempts <= 3 or len(args.counter) > 4:
        parser.error("Invalid bounds: page 1..256, events page..4096, timeout 1..300, deadline 1..1800, attempts 1..3, counters <=4")
    if args.command == "launch" and not args.editor or args.command == "diagnose" and not args.marker:
        parser.error("launch needs --editor; diagnose needs --marker")
    args.project = str(Path(args.project).resolve())
    args.pix = str(Path(args.pix).resolve())
    args.analyzer = str(Path(args.analyzer).resolve())
    directory = Path(args.output).resolve()
    directory.mkdir(parents=True, exist_ok=False)
    save(directory / "request.json", vars(args))
    workflow = None
    report = {"success": False, "state": "failed", "code": "unexpected_failure"}
    try:
        if args.command == "launch":
            report = launch(args, directory)
        else:
            workflow = Workflow(args, directory)
            report = workflow.inspect()
    except (Failure, OSError, KeyboardInterrupt, KeyError, TypeError, ValueError) as e:
        report = {"success": False, "state": "failed", "code": getattr(e, "code", "interrupted" if isinstance(e, KeyboardInterrupt) else "protocol_or_io_error"), "message": str(e)}
    finally:
        if workflow and workflow.session:
            try:
                workflow.release()
            except Failure as e:
                report["cleanup"] = {"code": e.code, "message": str(e), "sessionId": workflow.session}
    report.update(schemaVersion=1, evidenceDirectory=str(directory))
    if workflow:
        report.update(attempts=workflow.attempts, evidence=workflow.evidence, limitations=workflow.limitations)
    save(directory / "diagnosis.json", report)
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0 if report["success"] else 2


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main())
