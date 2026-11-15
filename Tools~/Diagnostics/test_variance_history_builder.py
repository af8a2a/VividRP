"""Provenance and evidence ownership checks for independent moment replay."""
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


SCRIPT = Path(__file__).with_name("BuildScreenProbeVarianceHistory.py").resolve()


class VarianceHistoryBuilderTests(unittest.TestCase):
    def generate(self, output, control, *options):
        return subprocess.run([sys.executable, "-X", "utf8", "-B", str(SCRIPT), str(output),
            "--control-temporal", str(control), *options], capture_output=True, text=True, encoding="utf-8")

    def test_holdout_sequence_and_packed_layout_are_recorded(self):
        with tempfile.TemporaryDirectory(prefix="vivid-variance-holdout-") as temporary:
            root = Path(temporary)
            control = root / "control.compute"
            control.write_text("// packed control\n", encoding="utf-8")
            output = root / "capture"
            result = self.generate(output, control, "--packed-dual-variance", "--sample-frame-base", "4361", "--motion-history")
            self.assertEqual(result.returncode, 0, result.stderr)
            protocol = json.loads((output / "protocol.json").read_text(encoding="utf-8"))
            self.assertEqual(protocol["sampleFrameBase"], 4361)
            self.assertEqual(protocol["controlMomentLayout"], "packed-dual-variance")
            self.assertTrue(protocol["motionHistory"])
            script = (output / "capture-script.cs").read_text(encoding="utf-8")
            self.assertNotIn("1733", script)
            self.assertEqual(script.count("4361"), 2)
            self.assertIn("SdfRoundToNearestHalf", (output / "temporal-control.compute.txt").read_text(encoding="utf-8"))

    def test_ambiguous_layout_rejected_before_creating_output(self):
        with tempfile.TemporaryDirectory(prefix="vivid-variance-layout-") as temporary:
            root = Path(temporary)
            control = root / "control.compute"
            control.write_text("// control\n", encoding="utf-8")
            output = root / "capture"
            result = self.generate(output, control, "--dual-variance", "--packed-dual-variance")
            self.assertNotEqual(result.returncode, 0)
            self.assertFalse(output.exists())

    def test_temporal_hashes_pin_actual_written_bytes(self):
        with tempfile.TemporaryDirectory(prefix="vivid-variance-history-") as temporary:
            root = Path(temporary)
            control = root / "control.compute"
            control.write_bytes('// 方差 reference\r\n#include "SdfScreenProbeCommon.hlsl"\n'.encode("utf-8"))
            output = root / "capture"
            result = self.generate(output, control)
            self.assertEqual(result.returncode, 0, result.stderr)
            protocol = json.loads((output / "protocol.json").read_text(encoding="utf-8"))
            for group, prefix in (("referenceSourceSha256", "reference"), ("temporalSourceSha256", "temporal")):
                for label, expected in protocol[group].items():
                    self.assertEqual(hashlib.sha256((output / f"{prefix}-{label}.compute.txt").read_bytes()).hexdigest(), expected)
            self.assertIn("方差 reference", (output / "temporal-control.compute.txt").read_text(encoding="utf-8"))

    def test_existing_temporal_capture_is_not_overwritten(self):
        with tempfile.TemporaryDirectory(prefix="vivid-variance-history-") as temporary:
            root = Path(temporary)
            control = root / "control.compute"
            control.write_text("// first control\n", encoding="utf-8")
            output = root / "capture"
            result = self.generate(output, control)
            self.assertEqual(result.returncode, 0, result.stderr)
            before = {p.name: p.read_bytes() for p in output.iterdir()}
            control.write_text("// changed control\n", encoding="utf-8")
            result = self.generate(output, control)
            self.assertNotEqual(result.returncode, 0)
            self.assertEqual(before, {p.name: p.read_bytes() for p in output.iterdir()})


if __name__ == "__main__":
    unittest.main()
