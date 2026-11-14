"""Large RGB reductions must preserve energy and agree with luminance metrics."""
import unittest

import numpy as np

from ScreenProbeAnchorWindowExperiment import LUMA, mean_rgb


class WindowStatisticsTests(unittest.TestCase):
    def test_constant_hdr_color_survives_millions_of_pixels(self):
        color = np.array([14.2, 21.1, 9.3], np.float32)
        values = np.broadcast_to(color, (256, 9216, 3)).copy()
        np.testing.assert_array_equal(mean_rgb(values), color.astype(np.float64))

    def test_color_mean_and_luminance_mean_agree(self):
        rng = np.random.default_rng(20261114)
        values = rng.uniform(.01, 100, (128, 4096, 3)).astype(np.float32)
        actual = float(mean_rgb(values) @ LUMA)
        expected = float((values @ LUMA).mean(dtype=np.float64))
        self.assertAlmostEqual(actual, expected, delta=1e-11)


if __name__ == "__main__":
    unittest.main()
