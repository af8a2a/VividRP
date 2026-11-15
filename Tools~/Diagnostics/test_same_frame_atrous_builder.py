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
    def generate(self, output, control):
        return subprocess.run([sys.executable, "-X", "utf8", "-B", str(SCRIPT), str(output), "--control-body", str(control)],
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


if __name__ == "__main__":
    unittest.main()
