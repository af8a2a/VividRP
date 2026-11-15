"""Percentiles cannot replace the all-point half-word identity proof."""
import unittest
from unittest.mock import patch

import numpy as np

from AnalyzeScreenProbeVarianceHistory import reference_output_identity
from AnalyzeScreenProbeAtrousExperiment import ARMS, FRAMES


class ReferenceIdentityTests(unittest.TestCase):
    def rows(self):
        return {(arm, frame, tag): dict(x=720, y=936, width=480, height=40, format="R16G16B16A16_SFloat",
            arm=arm, frame=frame, tag=tag) for arm in ARMS for frame in FRAMES
            for tag in ("final-emitter-gap", "control-emitter-gap")}

    def test_all_reference_outputs_equal(self):
        pixels = np.ones((40, 480, 4), dtype="<f2")
        with patch("AnalyzeScreenProbeVarianceHistory.read", return_value=pixels):
            report = reference_output_identity(self.rows())
        self.assertTrue(report["exact"])
        self.assertEqual(report["rgbaSamples"], 7168)

    def test_one_half_word_difference_rejects_identity(self):
        pixels = np.ones((40, 480, 4), dtype="<f2")
        changed = pixels.copy()
        changed.view("<u2")[16, 46 * 16 + 8 - 720, 0] += 1
        def load(row):
            return changed if row["tag"] == "control-emitter-gap" and row["frame"] == 1030 else pixels
        with patch("AnalyzeScreenProbeVarianceHistory.read", side_effect=load):
            report = reference_output_identity(self.rows())
        self.assertFalse(report["exact"])
        self.assertTrue(all(v["differentHalfWords"] == 1 for v in report["arms"].values()))


if __name__ == "__main__":
    unittest.main()
