"""Check causal input isolation and reject leaks into the live guide channels."""
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

import numpy as np

from AnalyzeScreenProbeAtrousExperiment import ARMS, FRAMES, qualify_moment_channel_composition


class MomentChannelIsolationTests(unittest.TestCase):
    def fixture(self, channel, corrupt_component=None):
        rows = {}
        for arm in ARMS:
            for label in ("emitter-gap", "ceiling", "floor", "back"):
                tag = "native-moments" if label == "emitter-gap" else "native-moments-" + label
                for frame in FRAMES:
                    for name in (tag, "control-composed-moments-" + label, "final-" + label, "control-" + label):
                        rows[arm, frame, name] = dict(x=0, y=0, width=1, height=1, format="R16G16B16A16_SFloat",
                            frame=frame, tag=name, sha256="first-output-identical")

        def load(row):
            value = np.array([1 + (row["frame"] - FRAMES.start) / 64, .5 + (row["frame"] - FRAMES.start) / 128, 32, 1], dtype="<f2").reshape(1, 1, 4)
            if row["tag"].startswith("control-composed-"):
                value[..., 0 if channel == "mean" else 1] = 1 if channel == "mean" else .5
                if row["frame"] == 1030 and corrupt_component is not None:
                    value.view("<u2")[..., corrupt_component] += 1
            return value
        return rows, load

    def test_one_channel_frozen_and_all_live_channels_exact(self):
        for channel in ("mean", "variance"):
            rows, load = self.fixture(channel)
            with patch("AnalyzeScreenProbeAtrousExperiment.read", side_effect=load):
                report = qualify_moment_channel_composition(rows, channel)
            self.assertTrue(report["qualified"])
            self.assertEqual(report["checkedHalfWords"], 4096)

    def test_any_frozen_or_live_half_word_difference_fails(self):
        for channel in ("mean", "variance"):
            for component in range(4):
                rows, load = self.fixture(channel, component)
                with patch("AnalyzeScreenProbeAtrousExperiment.read", side_effect=load):
                    report = qualify_moment_channel_composition(rows, channel)
                self.assertFalse(report["qualified"])
                self.assertEqual(len(report["differentHalfWords"]), 8)

    def test_first_frame_filter_mismatch_fails(self):
        rows, load = self.fixture("mean")
        rows[ARMS[0], FRAMES.start, "control-ceiling"]["sha256"] = "changed"
        with patch("AnalyzeScreenProbeAtrousExperiment.read", side_effect=load):
            report = qualify_moment_channel_composition(rows, "mean")
        self.assertFalse(report["qualified"])
        self.assertEqual(report["firstFrameOutputDifferences"], [[ARMS[0], "ceiling"]])

    def test_identical_hash_from_a_different_roi_fails(self):
        rows, load = self.fixture("variance")
        rows[ARMS[0], FRAMES.start, "control-ceiling"]["x"] += 1
        with patch("AnalyzeScreenProbeAtrousExperiment.read", side_effect=load):
            report = qualify_moment_channel_composition(rows, "variance")
        self.assertFalse(report["qualified"])
        self.assertEqual(report["firstFrameOutputDifferences"], [[ARMS[0], "ceiling"]])

    def test_builder_pins_sources_and_never_overwrites_a_capture(self):
        script = Path(__file__).with_name("BuildScreenProbeMomentChannelIsolation.py")
        with tempfile.TemporaryDirectory(prefix="vivid-moment-isolation-") as temporary:
            root = Path(temporary) / "capture"
            args = [sys.executable, "-X", "utf8", "-B", str(script), str(root), "--frozen-channel", "mean", "--sample-frame-base", "4361", "--frozen-scales", "fine"]
            result = subprocess.run(args, capture_output=True, text=True, encoding="utf-8")
            self.assertEqual(result.returncode, 0, result.stderr)
            protocol = json.loads((root / "protocol.json").read_text(encoding="utf-8"))
            self.assertEqual(protocol["controlMomentChannelFrozen"], "mean")
            self.assertEqual(protocol["sampleFrameBase"], 4361)
            self.assertEqual(protocol["controlFrozenMomentScales"], [1, 2])
            for label, expected in protocol["referenceSourceSha256"].items():
                self.assertEqual(hashlib.sha256((root / f"reference-{label}.compute.txt").read_bytes()).hexdigest(), expected)
            source = (root / "capture-script.cs").read_text(encoding="utf-8")
            self.assertNotIn("1733", source)
            self.assertEqual(source.count("4361"), 2)
            before = {p.name: p.read_bytes() for p in root.iterdir()}
            result = subprocess.run(args, capture_output=True, text=True, encoding="utf-8")
            self.assertNotEqual(result.returncode, 0)
            self.assertEqual(before, {p.name: p.read_bytes() for p in root.iterdir()})


if __name__ == "__main__":
    unittest.main()
