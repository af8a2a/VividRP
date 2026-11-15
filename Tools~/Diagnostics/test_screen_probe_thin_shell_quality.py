"""Fail-closed checks for receiver-input qualification, independent of image quality."""
import copy
from pathlib import Path
import tempfile
import unittest

import numpy as np

from AnalyzeScreenProbeThinShellQuality import trace_input_qualification


class ReceiverInputQualificationTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix="vivid-thin-shell-qa-")
        self.addCleanup(self.directory.cleanup)
        self.geometry = Path(self.directory.name) / "geometry.bin"
        np.array([0, 3, 0, 100], dtype="<f4").tofile(self.geometry)
        rows, states = {}, {}
        for index in range(4):
            arm = f"{index:02}-r0-t0"
            for frame in range(1024, 1152):
                for tag in ("input-gbuffer0", "input-gbuffer1", "input-depth", "input-placement"):
                    rows[arm, frame, tag] = dict(sha256="same-input", width=1, height=1, format="same-format")
                rows[arm, frame, "geometry"] = dict(path=str(self.geometry), width=1, height=1,
                                                    format="R32G32B32A32_SFloat")
                states[arm, frame] = dict(inverseVp=np.eye(4).reshape(-1).tolist())
        self.before = dict(traceInputsRecorded=True), rows, states
        self.after = copy.deepcopy(self.before)

    def test_matching_inputs_and_geometry_qualify(self):
        result = trace_input_qualification(self.before, self.after)
        self.assertTrue(result["qualified"])
        self.assertEqual(result["receiverInputObservations"], 2048)

    def test_matching_geometry_cannot_excuse_motion_input_mismatch(self):
        self.after[1]["02-r0-t0", 1031, "input-gbuffer1"]["sha256"] = "changed-input"
        result = trace_input_qualification(self.before, self.after)
        self.assertFalse(result["qualified"])
        self.assertTrue(result["staticQualified"])
        self.assertFalse(result["motionQualified"])
        self.assertEqual(result["derivedGeometryDifferentFrames"], 0)

    def test_matching_textures_cannot_excuse_inverse_matrix_mismatch(self):
        self.after[2]["00-r0-t0", 1024]["inverseVp"][0] += .1
        result = trace_input_qualification(self.before, self.after)
        self.assertFalse(result["qualified"])
        self.assertFalse(result["staticQualified"])
        self.assertTrue(result["motionQualified"])

    def test_derived_position_bound_is_enforced(self):
        changed = Path(self.directory.name) / "changed-geometry.bin"
        np.array([4e-8, 3, 0, 100], dtype="<f4").tofile(changed)
        self.after[1]["00-r0-t0", 1024, "geometry"]["path"] = str(changed)
        result = trace_input_qualification(self.before, self.after)
        self.assertFalse(result["qualified"])
        self.assertGreater(result["derivedPositionMaxAbsMetres"], result["derivedPositionBoundMetres"])


if __name__ == "__main__":
    unittest.main()
