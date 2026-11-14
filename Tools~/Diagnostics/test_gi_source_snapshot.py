"""External script/reference mutation must invalidate a captured GI comparison."""
import contextlib
import hashlib
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from VerifyGiSourceSnapshot import create, verify


class SourceSnapshotTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.workspace = self.root / "workspace"
        self.source = self.workspace / "Runtime" / "test.hlsl"
        self.source.parent.mkdir(parents=True)
        self.source.write_text("source", encoding="utf-8")
        self.reference = self.root / "reference.bin"
        self.reference.write_bytes(b"reference")
        self.script = self.root / "actual-script.cs"
        self.script.write_text("script", encoding="utf-8")
        self.snapshot = self.root / "before.json"
        self.evidence = self.root / "evidence"
        self.evidence.mkdir()

    def capture(self):
        with patch("VerifyGiSourceSnapshot.subprocess.check_output", return_value="Runtime/test.hlsl\n"):
            create(self.snapshot, self.workspace, [], [self.reference], [self.script])

    def test_unchanged_external_inputs_and_script_are_counted(self):
        self.capture()
        verify(self.snapshot, self.workspace, self.evidence)
        result = json.loads((self.evidence / "source-verification.json").read_text())
        self.assertTrue(result["unchanged"])
        self.assertEqual((result["files"], result["inputs"], result["scripts"]), (1, 1, 1))
        self.assertEqual((self.evidence / "source-before-capture.json").read_bytes(), self.snapshot.read_bytes())

    def test_mutated_reference_rejects_comparison(self):
        self.capture()
        self.reference.write_bytes(b"changed")
        with self.assertRaises(AssertionError):
            verify(self.snapshot, self.workspace, self.evidence)
        result = json.loads((self.evidence / "source-verification.json").read_text())
        self.assertFalse(result["unchanged"])
        self.assertEqual(result["changedSources"], ["inputs:" + str(self.reference.resolve())])

    def test_removed_executed_script_rejects_comparison(self):
        self.capture()
        self.script.unlink()
        with self.assertRaises(AssertionError):
            verify(self.snapshot, self.workspace, self.evidence)
        result = json.loads((self.evidence / "source-verification.json").read_text())
        self.assertFalse(result["unchanged"])
        self.assertEqual(result["changedSources"], ["scripts:" + str(self.script.resolve())])

    def test_original_file_only_snapshot_remains_supported(self):
        self.snapshot.write_text(json.dumps(dict(files=[dict(path="Runtime/test.hlsl",
            sha256=hashlib.sha256(self.source.read_bytes()).hexdigest())])), encoding="utf-8")
        verify(self.snapshot, self.workspace, self.evidence)
        result = json.loads((self.evidence / "source-verification.json").read_text())
        self.assertTrue(result["unchanged"])
        self.assertEqual((result["inputs"], result["scripts"]), (0, 0))

    def test_different_preexisting_snapshot_is_not_overwritten(self):
        self.capture()
        target = self.evidence / "source-before-capture.json"
        target.write_bytes(b"another experiment")
        with self.assertRaises(AssertionError):
            verify(self.snapshot, self.workspace, self.evidence)
        self.assertEqual(target.read_bytes(), b"another experiment")


if __name__ == "__main__":
    with contextlib.redirect_stdout(io.StringIO()):
        unittest.main()
