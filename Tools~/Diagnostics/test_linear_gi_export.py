"""Standalone numerical contract tests. Does not start Unity or Unreal tests."""
import importlib.util
from pathlib import Path
import tempfile
import unittest

import numpy as np

spec = importlib.util.spec_from_file_location("linear_gi", Path(__file__).with_name("LinearGiExport.py"))
linear = importlib.util.module_from_spec(spec)
spec.loader.exec_module(linear)


class LinearGiContractTests(unittest.TestCase):
    def test_unsigned_gpu_float_known_values(self):
        for mantissa in (5, 6):
            bits = np.array([0, 15 << mantissa, 16 << mantissa, 1, 31 << mantissa, (31 << mantissa) | 1], np.uint32)
            values = linear.unpack_unsigned_float(bits, mantissa)
            np.testing.assert_array_equal(values[:3], [0, 1, 2])
            self.assertEqual(values[3], 2.0 ** (1 - 15 - mantissa))
            self.assertTrue(np.isinf(values[4]))
            self.assertTrue(np.isnan(values[5]))

    def test_exr_preserves_hdr_and_signed_float_values(self):
        values = np.array([[[0, 0.18, 200], [-1, 1.125, 1e-7]]], np.float32)
        with tempfile.NamedTemporaryFile(prefix="vivid-linear-exr-", suffix=".exr", delete=False) as temp:
            path = Path(temp.name)
        try:
            linear.exr_write(path, values)
            np.testing.assert_array_equal(linear.exr_read(path), values)
        finally:
            path.unlink(missing_ok=True)

    def test_reference_box_mask_and_single_sided_light(self):
        import json
        fixture = json.loads(linear.FIXTURE.read_text())
        labels, normals, depth, _, _, receiver, emitter = linear.expected_geometry(fixture, 192, 108)
        self.assertEqual(emitter, 8)
        self.assertEqual(labels[16, 95], emitter)
        np.testing.assert_array_equal(normals[16, 95], [0, -1, 0])
        self.assertFalse(receiver[16, 95])
        self.assertEqual(labels[100, 100], 1)
        np.testing.assert_array_equal(normals[100, 100], [0, 1, 0])
        self.assertTrue(np.isfinite(depth).all())


if __name__ == "__main__":
    unittest.main()
