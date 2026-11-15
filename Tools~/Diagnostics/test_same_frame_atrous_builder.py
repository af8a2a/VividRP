"""Regression checks for exact replay-source provenance and immutable evidence."""
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


SCRIPT = Path(__file__).with_name("BuildScreenProbeSameFrameAtrous.py").resolve()


class SameFrameAtrousBuilderTests(unittest.TestCase):
    def generate(self, output, control, motion=False):
        return subprocess.run([sys.executable, "-X", "utf8", "-B", str(SCRIPT), str(output), "--control-body", str(control)]
                              + (["--motion-history"] if motion else []),
                              capture_output=True, text=True, encoding="utf-8")

    def test_reference_hashes_describe_written_bytes(self):
        with tempfile.TemporaryDirectory(prefix="vivid-atrous-provenance-") as temporary:
            root = Path(temporary)
            control = root / "control.hlsl"
            control.write_bytes("// 探针 reference\r\nfloat4 ReplayValue;\n".encode("utf-8"))
            output = root / "capture"
            result = self.generate(output, control)
            self.assertEqual(result.returncode, 0, result.stderr)
            protocol = json.loads((output / "protocol.json").read_text(encoding="utf-8"))
            for name, expected in protocol["referenceSourceSha256"].items():
                actual = (output / f"reference-{name}.compute.txt").read_bytes()
                self.assertEqual(hashlib.sha256(actual).hexdigest(), expected)
            self.assertIn("探针 reference", (output / "reference-control.compute.txt").read_text(encoding="utf-8"))

    def test_existing_capture_is_not_overwritten(self):
        with tempfile.TemporaryDirectory(prefix="vivid-atrous-immutable-") as temporary:
            root = Path(temporary)
            control = root / "control.hlsl"
            control.write_text("// first reference\n", encoding="utf-8")
            output = root / "capture"
            result = self.generate(output, control)
            self.assertEqual(result.returncode, 0, result.stderr)
            before = {p.name: p.read_bytes() for p in output.iterdir()}
            control.write_text("// changed reference\n", encoding="utf-8")
            result = self.generate(output, control)
            self.assertNotEqual(result.returncode, 0)
            self.assertEqual(before, {p.name: p.read_bytes() for p in output.iterdir()})

    def test_motion_protocol_uses_fixed_trajectory_and_moving_edge(self):
        with tempfile.TemporaryDirectory(prefix="vivid-atrous-motion-") as temporary:
            root = Path(temporary)
            control = root / "control.hlsl"
            control.write_text("// motion reference\n", encoding="utf-8")
            output = root / "capture"
            result = self.generate(output, control, motion=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            protocol = json.loads((output / "protocol.json").read_text(encoding="utf-8"))
            self.assertEqual(protocol["arms"], ["02-r0-t0", "03-r0-t0"])
            self.assertTrue(protocol["motionHistory"])
            source = (output / "capture-script.cs").read_text(encoding="utf-8")
            self.assertIn('schema="screen-probe-atrous-same-frame-motion.v1",motionHistoryRecorded=true,', source)
            self.assertIn('var replayRois=new[]{("edge",880,414,160,128)};', source)
            self.assertEqual(source.count('ReadRegion(cmd,texture,"temporal-edge",880,414,160,128);'), 1)
            self.assertIn('EditorApplication.isPaused=true;', source)

    def test_replay_rejects_changed_hash_or_coordinates(self):
        from AnalyzeScreenProbeAtrousExperiment import FRAMES, replay_hash_differences
        arm = "02-r0-t0"
        rows = {(arm, frame, tag): dict(sha256="matching", x=880, y=414, width=160, height=128, format="RGBA16F")
                for frame in FRAMES for tag in ("temporal-edge", "replay-input-edge", "final-edge", "replay-aa-edge")}
        self.assertEqual(replay_hash_differences(rows, (arm,), ("edge",)), [])
        rows[arm, 1029, "replay-aa-edge"]["sha256"] = "changed"
        rows[arm, 1030, "replay-input-edge"]["x"] += 1
        self.assertEqual(replay_hash_differences(rows, (arm,), ("edge",)),
                         [[arm, 1029, "final-edge"], [arm, 1030, "temporal-edge"]])

    def test_frozen_moments_cannot_be_used_for_moving_history_validation(self):
        from BuildScreenProbeSameFrameAtrous import build
        with self.assertRaisesRegex(ValueError, "static diagnostic"):
            build("", Path("unused"), motion_history=True, freeze_control_moments=True)


if __name__ == "__main__":
    unittest.main()
