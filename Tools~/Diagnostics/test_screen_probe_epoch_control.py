"""Fail-closed checks for the producer qualification report; no Unity session."""
import copy
import json
from pathlib import Path
import tempfile
import unittest

from ScreenProbeEpochControlExperiment import SOURCE_TAGS, analyze


class EpochControlTests(unittest.TestCase):
    def fixture(self):
        reads, cpu = [], []
        for arm in ("half128-a", "half128-b"):
            for frame in range(2):
                for tag in (*SOURCE_TAGS, "raw"):
                    reads.append(dict(arm=arm, frame=frame, tag=tag, width=1, height=1,
                                      format="R16G16B16A16_SFloat", sha256="same"))
                cpu.append(dict(arm=arm, frame=frame, current=dict(m_HasHistory=frame > 0,
                                m_RadiosityPublishedOrder=8, m_RadiosityBatch=32, m_RadiosityUpdateCards=0),
                                previous={}, scene={}, isFirstFrame=False,
                                resetPostProcessingHistory=False, configuredFrame=1733 + frame))
        return dict(schema="screen-probe-epoch-control.v1", complete=True, spacing=16,
                    rays=8, measuredFrames=2, records=reads, cpuFrames=cpu, errors=[])

    def report(self, manifest):
        with tempfile.TemporaryDirectory(prefix="vivid-epoch-report-") as directory:
            root = Path(directory)
            (root / "capture.json").write_text(json.dumps(manifest), encoding="utf-8")
            return analyze(root)

    def test_complete_static_control(self):
        self.assertTrue(self.report(self.fixture())["qualified"])

    def test_missing_gpu_checkpoint(self):
        manifest = self.fixture()
        manifest["records"].pop()
        self.assertFalse(self.report(manifest)["qualified"])

    def test_matching_but_changing_sources(self):
        manifest = self.fixture()
        for row in manifest["records"]:
            if row["frame"] == 1 and row["tag"] == "radiosity":
                row["sha256"] = "source-rebuilt"
        report = self.report(manifest)
        self.assertEqual(report["mismatches"], 0)
        self.assertFalse(report["qualified"])

    def test_unobserved_cpu_interval(self):
        manifest = self.fixture()
        manifest["cpuFrames"] = [r for r in manifest["cpuFrames"] if r["frame"] == 0]
        self.assertFalse(self.report(manifest)["qualified"])

    def test_matching_history_reset(self):
        manifest = self.fixture()
        for row in manifest["cpuFrames"]:
            row["current"]["m_HasHistory"] = False
        report = self.report(manifest)
        self.assertEqual(report["cpuDifferenceCount"], 0)
        self.assertEqual(len(report["cpuEvents"]), 2)
        self.assertFalse(report["qualified"])

    def test_duplicate_cpu_frame(self):
        manifest = self.fixture()
        manifest["cpuFrames"].append(copy.deepcopy(manifest["cpuFrames"][0]))
        with self.assertRaisesRegex(ValueError, "Duplicate CPU frame"):
            self.report(manifest)

    def test_absent_source_is_not_a_static_source(self):
        manifest = self.fixture()
        manifest["records"] = [r for r in manifest["records"] if r["tag"] != "radiosity"]
        self.assertFalse(self.report(manifest)["qualified"])


if __name__ == "__main__":
    unittest.main()
