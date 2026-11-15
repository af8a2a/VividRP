"""Check the prefilter's immutable channels, fresh history and variance bounds."""
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

import numpy as np

from AnalyzeScreenProbeAtrousExperiment import ARMS, FRAMES, qualify_variance_prefilter


class VariancePrefilterTests(unittest.TestCase):
    def evaluate(self, mutate=None):
        arm, label = ARMS[0], "ceiling"
        rows = {(arm, frame, tag): dict(x=0, y=0, width=5, height=5, format="R16G16B16A16_SFloat", tag=tag)
                for frame in FRAMES for tag in ("native-moments-ceiling", "variance-prefiltered-moments-ceiling")}
        live = np.ones((5, 5, 4), dtype="<f2")
        live[..., 0] = 2
        live[..., 1] = np.arange(25).reshape(5, 5)
        live[..., 2] = 32
        filtered = live.copy(); filtered[..., 1] = 12
        if mutate:
            mutate(live, filtered)
        def read(row):
            return live if row["tag"].startswith("native-") else filtered
        with patch("AnalyzeScreenProbeAtrousExperiment.read", side_effect=read):
            return qualify_variance_prefilter(rows, (arm,), (label,))

    def test_bounded_variance_and_immutable_channels_pass(self):
        self.assertTrue(self.evaluate()["qualified"])

    def test_any_mean_age_or_coverage_word_change_fails(self):
        for component in (0, 2, 3):
            def mutate(live, filtered):
                filtered.view("<u2")[2, 2, component] += 1
            self.assertFalse(self.evaluate(mutate)["qualified"])

    def test_fresh_variance_must_remain_exact(self):
        def mutate(live, filtered):
            live[2, 2, 2] = filtered[2, 2, 2] = 1
            live[2, 2, 1] = 0; filtered[2, 2, 1] = 1
        self.assertFalse(self.evaluate(mutate)["qualified"])

    def test_fresh_variance_signed_zero_change_fails_word_identity(self):
        def mutate(live, filtered):
            live[2, 2, 2] = filtered[2, 2, 2] = 1
            live[2, 2, 1] = 0.0; filtered[2, 2, 1] = -0.0
        self.assertFalse(self.evaluate(mutate)["qualified"])

    def test_variance_cannot_exceed_dense_input_bounds(self):
        self.assertFalse(self.evaluate(lambda a, b: b.__setitem__((2, 2, 1), 100))["qualified"])

    def test_motion_builder_pins_written_sources(self):
        script = Path(__file__).with_name("BuildScreenProbeVariancePrefilter.py")
        with tempfile.TemporaryDirectory(prefix="vivid-variance-prefilter-") as temporary:
            root = Path(temporary) / "capture"
            result = subprocess.run([sys.executable, "-X", "utf8", "-B", str(script), str(root), "--motion-history", "--sample-frame-base", "4361"],
                capture_output=True, text=True, encoding="utf-8")
            self.assertEqual(result.returncode, 0, result.stderr)
            protocol = json.loads((root / "protocol.json").read_text(encoding="utf-8"))
            self.assertTrue(protocol["motionHistory"])
            self.assertTrue(protocol["controlMomentVariancePrefilter"])
            self.assertEqual(protocol["controlPrefilteredMomentScales"], [4, 8])
            for label, expected in protocol["referenceSourceSha256"].items():
                self.assertEqual(hashlib.sha256((root / f"reference-{label}.compute.txt").read_bytes()).hexdigest(), expected)


if __name__ == "__main__":
    unittest.main()
