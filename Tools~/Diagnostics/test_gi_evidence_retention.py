"""Filesystem safety regressions; no Unity Editor or live evidence is touched."""
import hashlib
import json
from pathlib import Path
import stat
import tempfile
from types import SimpleNamespace
import unittest
from unittest import mock

from GiEvidenceRetention import no_link, plan_runs, prune


class RetentionTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="vivid-evidence-retention-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).absolute()
        self.run = self.root / "Archived-20261114-000000-000"
        self.arm = self.run / "arm"
        self.arm.mkdir(parents=True)
        for frame in (10, 11, 12):
            for tag in ("raw", "final"):
                (self.arm / f"{frame:04d}-{tag}.bin").write_bytes(bytes([frame]) * 8)
        for name in ("analysis.json", "image.png", "compiled-kernel.bin", "profile-before.asset"):
            (self.run / name).write_bytes(b"preserve")
        self.policy = dict(protectedRuns=[], archiveRuns=[dict(run=self.run.name, reason="Superseded experiment", requiredFiles=["analysis.json"])])

    def test_planning_never_changes_evidence(self):
        before = {str(p): p.read_bytes() for p in self.run.rglob("*") if p.is_file()}
        plans = plan_runs(self.root, self.policy)
        self.assertEqual(plans[0]["files"], 2)
        self.assertEqual(plans[0]["bytes"], 16)
        self.assertEqual(before, {str(p): p.read_bytes() for p in self.run.rglob("*") if p.is_file()})

    def test_pruning_preserves_frame_bundles_and_non_frame_artifacts(self):
        plans = plan_runs(self.root, self.policy)
        prune(self.root, plans)
        for frame in (10, 12):
            for tag in ("raw", "final"):
                self.assertTrue((self.arm / f"{frame:04d}-{tag}.bin").exists())
        self.assertFalse((self.arm / "0011-raw.bin").exists())
        self.assertFalse((self.arm / "0011-final.bin").exists())
        for name in ("analysis.json", "image.png", "compiled-kernel.bin", "profile-before.asset"):
            self.assertEqual((self.run / name).read_bytes(), b"preserve")
        audit = json.loads((self.run / "raw-retention.json").read_text())
        self.assertTrue(audit["complete"])
        self.assertFalse(audit["rawReanalysisAvailable"])
        self.assertEqual((audit["removedFiles"], audit["freedBytes"]), (2, 16))
        records = [json.loads(line) for line in (self.run / "raw-retention-files.jsonl").read_text().splitlines()]
        self.assertEqual(records[0]["files"][0]["sha256"], hashlib.sha256(bytes([11]) * 8).hexdigest())
        self.assertEqual(records[-1]["deletion"], "complete")

    def test_protected_run_refused(self):
        self.policy["protectedRuns"] = [self.run.name]
        with self.assertRaises(ValueError):
            plan_runs(self.root, self.policy)

    def test_directory_escape_refused(self):
        for name in ("../outside", str(self.root), "..", "nested/run"):
            self.policy["archiveRuns"][0]["run"] = name
            with self.assertRaises(ValueError):
                plan_runs(self.root, self.policy)

    def test_archive_evidence_escape_refused(self):
        (self.root / "outside.json").write_text("sentinel")
        self.policy["archiveRuns"][0]["requiredFiles"] = ["../outside.json"]
        with self.assertRaises(ValueError):
            plan_runs(self.root, self.policy)
        self.assertEqual((self.root / "outside.json").read_text(), "sentinel")

    def test_changed_evidence_aborts_without_deleting(self):
        plans = plan_runs(self.root, self.policy)
        changed = self.arm / "0011-final.bin"
        changed.write_bytes(b"new evidence")
        with self.assertRaises(ValueError):
            prune(self.root, plans)
        self.assertTrue(changed.exists())
        self.assertTrue((self.arm / "0011-raw.bin").exists())
        self.assertFalse(json.loads((self.run / "raw-retention.json").read_text())["complete"])

    def test_previous_audit_not_overwritten(self):
        plans = plan_runs(self.root, self.policy)
        audit = self.run / "raw-retention.json"
        audit.write_text("original audit")
        with self.assertRaises(ValueError):
            prune(self.root, plans)
        self.assertEqual(audit.read_text(), "original audit")

    def test_windows_reparse_point_rejected_without_creating_links(self):
        with mock.patch.object(Path, "lstat", return_value=SimpleNamespace(st_file_attributes=stat.FILE_ATTRIBUTE_REPARSE_POINT)), \
             mock.patch.object(Path, "is_symlink", return_value=False):
            with self.assertRaises(ValueError):
                no_link(self.run)


if __name__ == "__main__":
    unittest.main()
