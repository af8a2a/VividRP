"""Panel-edge chroma checks must see errors that a luminance-only test misses."""
import unittest

import numpy as np

from ScreenProbeAnchorWindowMotion import chroma_delta
from TemporalSamplingExperiment import LUMA


class MotionStatisticsTests(unittest.TestCase):
    def test_gray_brightness_offset_is_not_a_chroma_change(self):
        target = np.array([[2., 3., 4.], [5., 2., 1.]])
        np.testing.assert_array_equal(chroma_delta(target+8., target), np.zeros((2, 2)))

    def test_equal_luminance_color_error_is_detected(self):
        target = np.array([[10., 10., 10.]])
        shift = np.array([[1., -LUMA[0]/LUMA[1], 0.]])
        self.assertAlmostEqual(float((shift @ LUMA)[0]), 0., delta=1e-15)
        self.assertGreater(float(np.linalg.norm(chroma_delta(target+shift, target))), 1.)


if __name__ == "__main__":
    unittest.main()
