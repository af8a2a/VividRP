"""Numerical response invariants; no Unity Editor or scene mutation."""
import unittest

import numpy as np

from ScreenProbeAnchorWindowRelight import response_metrics


class ResponseStatisticsTests(unittest.TestCase):
    def test_uniform_physical_scaling_has_zero_response_error(self):
        # Vary time, pixel energy and dynamic range; no output-gain fitting.
        control = np.geomspace(.001, 5000, 320*17).reshape(320,17)
        for scale in [0, .25, 4]:
            result = response_metrics(control, control*scale, scale)
            self.assertEqual(result['first_frame_error_percent'], 0)
            self.assertEqual(result['tail_64_mean_error_percent'], 0)
            self.assertEqual(result['first_eight_consecutive_frames_within_one_percent'], 0)

    def test_first_frame_deficit_and_off_residue_are_observable(self):
        control = np.full((320,17), 1e4)
        value = control*4
        value[0] *= .86
        result = response_metrics(control, value, 4)
        self.assertAlmostEqual(result['first_frame_error_percent'], -14)
        self.assertAlmostEqual(result['first_32_mean_abs_error_percent'], 14/32)
        self.assertEqual(result['first_eight_consecutive_frames_within_one_percent'], 1)
        value = np.zeros_like(control); value[0,0] = .1
        result = response_metrics(control, value, 0)
        self.assertEqual(result['off_max_absolute_y'], .1)


if __name__ == '__main__':
    unittest.main()
