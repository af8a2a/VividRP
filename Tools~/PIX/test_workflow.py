"""Offline contract/failure tests. No Unity Editor or GPU calls."""
import json
from pathlib import Path
import tempfile
import time
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import vivid_pix_agent as agent


class WorkflowChecks(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.path = Path(self.temp.name)
        self.args = SimpleNamespace(project=str(self.path), pix=str(self.path / "PIX"), analyzer="analyzer",
                                    unity="unity", timeout=20, deadline=60, camera="Main Camera", marker="Pass",
                                    page_size=2, max_events=4, attempts=2, question="Inspect", queue=None, event=None,
                                    timing=False, accessed=False, occupancy=False, counter=[])
        self.workflow = agent.Workflow(self.args, self.path)
        self.context = {"graphicsApi": "Direct3D12", "pipelineType": "VividRP.Runtime.VividRenderPipelineAsset",
                        "nativeAvailable": True, "pixRuntime": str(self.path / "PIX/WinPixGpuCapturer.dll")}

    def assert_failure(self, code, function):
        with self.assertRaises(agent.Failure) as caught:
            function()
        self.assertEqual(code, caught.exception.code)

    def test_preflight(self):
        agent.validate_preflight(self.context, self.args.pix)
        for field, value, code in [("graphicsApi", "Vulkan", "wrong_render_environment"),
                                    ("nativeAvailable", False, "requires_early_injection"),
                                    ("pixRuntime", "other.dll", "pix_version_mismatch"),
                                    ("sessionOwned", True, "busy"), ("editorBusy", True, "editor_busy")]:
            with self.subTest(field=field):
                self.assert_failure(code, lambda: agent.validate_preflight(dict(self.context, **{field: value}), self.args.pix))

    def test_event_identity_and_actual_work(self):
        rows = [{"queueId": 1, "eventIndex": 9, "name": "Dispatch", "gpuWork": False},
                {"queueId": 2, "eventIndex": 3, "name": "Dispatch", "gpuWork": True},
                {"queueId": 1, "eventIndex": 3, "name": "Draw", "gpuWork": True}]
        self.assertEqual(rows[1], agent.select_event(rows))
        self.assertEqual(rows[2], agent.select_event(rows, 1, 3))
        self.assert_failure("target_event_missing", lambda: agent.select_event(rows, 1, 9))
        self.assert_failure("event_identity_required", lambda: agent.select_event(rows, 1))

    def test_unique_evidence(self):
        agent.save(self.path / "result.json", {"a": 1})
        with self.assertRaises(FileExistsError):
            agent.save(self.path / "result.json", {"a": 2})
        self.assertEqual({"a": 1}, json.loads((self.path / "result.json").read_text()))

    def test_pixtool_raw_options(self):
        text = agent.pix_command_line(["C:/Program Files/PIX/pixtool.exe", "launch", "Unity.exe", "--command-line=-projectPath test -force-d3d12"])
        self.assertIn('--command-line="-projectPath test -force-d3d12"', text)
        self.assertNotIn('"--command-line=', text)

    def test_analysis_identity_hash_and_exit(self):
        job = self.workflow
        job.capture = str(self.path / "capture.wpix")
        job.capture_session, job.mode = "session", "all"
        corrupt = {}
        exit_code = 0

        def child(arguments, **kwargs):
            request = arguments[arguments.index("--request_id") + 1]
            value = dict(schemaVersion=3, action="events", requestId=request, sessionId="session", expectedPass="Pass",
                         boundaryMode="all", queueId=None, eventIndex=None, capturePath=job.capture,
                         captureHash="a" * 64, success=True, code="ok", data={"items": [], "total": 0, "nextOffset": None})
            value.update(corrupt)
            agent.save(arguments[6], value)
            return SimpleNamespace(returncode=exit_code)

        with patch.object(agent.subprocess, "run", child):
            self.assertEqual(0, job.analyze("events")["total"])
            for field in ["requestId", "sessionId", "expectedPass", "boundaryMode", "action", "queueId"]:
                corrupt = {field: "stale"}
                self.assert_failure("analysis_identity_mismatch", lambda: job.analyze("events"))
            corrupt = {"captureHash": "b" * 64}
            self.assert_failure("capture_hash_mismatch", lambda: job.analyze("events"))
            corrupt, exit_code = {}, 2
            self.assert_failure("analysis_process_failed", lambda: job.analyze("events"))
            corrupt = {"success": False, "code": "get_occupancy_failed", "unsupported": True, "hresult": -2147467263}
            self.assertIsNone(job.analyze("events", optional=True))
            self.assertEqual("get_occupancy_failed", job.limitations[0]["code"])

    def test_semantic_capture_failure_never_retried(self):
        actions = []
        def command(action, **kwargs):
            actions.append(action)
            if action == "preflight": return self.context
            return {"success": False, "state": "failed", "code": "no_target_gpu_work", "sessionId": "session"}
        self.workflow.command = command
        self.workflow.console = lambda *args: {"cursor": 1, "session": "console"}
        released = []
        self.workflow.release = lambda: released.append(self.workflow.session)
        self.assert_failure("no_target_gpu_work", self.workflow.take_capture)
        self.assertEqual(1, actions.count("capture"))
        self.assertEqual(["session"], released)

    def test_bounded_marker_pages(self):
        job = self.workflow
        job.take_capture = lambda: None
        def page(action, **options):
            self.assertEqual("events", action)
            offset = options["offset"]
            self.assertLess(offset, self.args.max_events)
            return {"items": [{"eventIndex": offset, "gpuWork": False}] * 2, "total": 100, "nextOffset": offset + 2}
        job.analyze = page
        self.assert_failure("target_event_missing", job.inspect)

    def test_console_errors_reject_otherwise_valid_capture(self):
        self.workflow.command = lambda action, **kwargs: self.context if action == "preflight" else {
            "success": True, "ready": True, "state": "ready", "code": "ok", "sessionId": "session", "boundaryMode": "all"}
        self.workflow.console = lambda name, cursor=None: {"cursor": 2, "session": "console",
                                                           "entries": [{"message": "queue error"}] if cursor else []}
        self.assert_failure("editor_errors_during_capture", self.workflow.take_capture)
        self.assertEqual("session", self.workflow.session)  # outer finally still owns cleanup

    def test_transient_capture_precondition_retries(self):
        attempts = []
        def command(action, **kwargs):
            if action == "preflight": return self.context
            attempts.append(action)
            if len(attempts) == 1: return {"code": "editor_busy", "success": False}
            return {"success": True, "ready": True, "state": "ready", "code": "ok", "sessionId": "session", "boundaryMode": "all"}
        self.workflow.command = command
        self.workflow.console = lambda *args: {"cursor": 1, "session": "console", "entries": []}
        self.workflow.release = lambda: setattr(self.workflow, "session", None)
        with patch.object(agent.time, "sleep"):
            self.workflow.take_capture()
        self.assertEqual(["capture", "capture"], attempts)
        self.assertEqual("session", self.workflow.capture_session)

    def test_timeout_before_command(self):
        self.workflow.deadline = time.monotonic() - 1
        with patch.object(agent, "unity") as command:
            self.assert_failure("workflow_timeout", lambda: self.workflow.command("capture"))
            command.assert_not_called()

    def test_existing_editor_not_launched(self):
        (self.path / "Temp").mkdir()
        (self.path / "Temp/UnityLockfile").touch()
        with patch.object(agent.subprocess, "Popen") as process:
            self.assert_failure("editor_already_open", lambda: agent.launch(self.args, self.path))
            process.assert_not_called()


if __name__ == "__main__":
    unittest.main()
