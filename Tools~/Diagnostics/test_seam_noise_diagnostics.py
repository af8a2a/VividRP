"""Standalone numerical contract tests for SeamNoiseDiagnostics. Does not launch Unity or Unreal."""
import importlib.util
from pathlib import Path
import unittest

import numpy as np

spec = importlib.util.spec_from_file_location("seam_diag", Path(__file__).with_name("SeamNoiseDiagnostics.py"))
seam_diag = importlib.util.module_from_spec(spec)
spec.loader.exec_module(seam_diag)


def _halton(index: int, base: int) -> float:
    f, r = 1.0, 0.0
    while index > 0:
        f /= base
        r += f * (index % base)
        index //= base
    return r


class SeamNoiseDiagnosticsContractTests(unittest.TestCase):
    def test_halton3_period8_captures_all_harmonics_missed_by_fundamental_k8(self):
        # 8-phase Halton(3) vertical jitter over 64 frames: k=24 holds 72.92% of cyclic diff power, k=8 only 2.08%.
        h3 = np.array([_halton((t % 8) + 1, 3) - 0.5 for t in range(64)], dtype=np.float64)
        bp = np.broadcast_to(h3[:, None, None], (64, 4, 6)).copy()
        raw = bp + 2.0
        mask = np.ones((4, 6), dtype=bool)

        summary = seam_diag.summarize_stage_regions(raw, bp, bp, {"roi": mask}, period=8)["roi"]

        # Legacy single-bin k=8 undercounts period-8 variance; full per8 captures 100%.
        self.assertLess(summary["detrended_8_frame_power_pct"], 15.0)
        self.assertAlmostEqual(summary["detrended_per8_power_pct"], 100.0, places=3)
        self.assertAlmostEqual(summary["per8_frame_difference_power_pct"], 100.0, places=10)
        self.assertAlmostEqual(summary["stoch_frame_difference_rms"], 0.0, places=13)

        harmonics = summary["per8_harmonic_diff_power_pct"]
        spec8 = np.fft.rfft(h3[:8] - h3[:8].mean())
        expected = [
            (1.0 if m == 4 else 2.0) * (abs(spec8[m]) / 8.0) ** 2 * 4.0 * np.sin(np.pi * m / 8.0) ** 2
            for m in range(1, 5)
        ]
        expected_cyclic_var = np.mean((np.roll(h3[:8], -1) - h3[:8]) ** 2)
        self.assertAlmostEqual(sum(expected), expected_cyclic_var, places=14)
        self.assertAlmostEqual(sum(harmonics.values()), 100.0, places=10)
        for key, exp_val in zip(("k8", "k16", "k24", "k32"), expected):
            self.assertAlmostEqual(harmonics[key], 100.0 * exp_val / expected_cyclic_var, places=10)
        # Verify k=24 dominates (>70%) while k=8 is tiny (<3%).
        self.assertGreater(harmonics["k24"], 70.0)
        self.assertLess(harmonics["k8"], 3.0)

    def test_nyquist_alternating_signal_uses_unit_parseval_weight(self):
        alt = np.array([1.0 if (t % 2 == 0) else -1.0 for t in range(64)], dtype=np.float64)
        bp = alt[:, None, None] * np.ones((1, 2, 2), dtype=np.float64)
        raw = bp + 5.0
        mask = np.ones((2, 2), dtype=bool)

        parts = seam_diag.decompose_period(bp, period=8)
        np.testing.assert_allclose(parts["seq_harmonic_power"].sum(axis=0), 1.0, rtol=0, atol=1e-14)
        np.testing.assert_allclose(parts["diff_harmonic_power"][3], 4.0, rtol=0, atol=1e-14)

        summary = seam_diag.summarize_stage_regions(raw, bp, bp, {"roi": mask}, period=8)["roi"]
        self.assertAlmostEqual(summary["detrended_per8_power_pct"], 100.0, places=5)
        self.assertAlmostEqual(summary["per8_harmonic_diff_power_pct"]["k32"], 100.0, places=10)

    def test_linear_drift_does_not_leak_into_per8_component(self):
        t = np.arange(64, dtype=np.float64)[:, None, None]
        bp = 0.03 * t * np.ones((1, 3, 5), dtype=np.float64)
        raw = bp + 10.0
        mask = np.ones((3, 5), dtype=bool)

        parts = seam_diag.decompose_period(bp, period=8)
        np.testing.assert_allclose(parts["per8_wave"], 0.0, rtol=0, atol=1e-14)
        np.testing.assert_allclose(parts["per8_seq"] + parts["stoch_seq"], bp, rtol=0, atol=1e-14)

        summary = seam_diag.summarize_stage_regions(raw, bp, bp, {"roi": mask}, period=8)["roi"]
        self.assertAlmostEqual(summary["per8_frame_difference_rms"], 0.0, places=13)
        self.assertAlmostEqual(summary["stoch_frame_difference_rms"], 0.03, places=13)

    def test_kx_spatial_split_satisfies_exact_rowwise_pythagoras(self):
        rng = np.random.default_rng(42)
        stripe = rng.standard_normal((64, 12, 1)) * 0.4
        blotch = rng.standard_normal((64, 12, 20)) * 0.7
        blotch -= blotch.mean(axis=-1, keepdims=True)
        bp = stripe + blotch
        raw = bp + 4.0

        # Non-trivial mask with varying active columns per row.
        mask = np.ones((12, 20), dtype=bool)
        mask[:4, :3] = False
        summary_masked = seam_diag.summarize_stage_regions(raw, bp, bp, {"roi": mask}, period=8)["roi"]
        lhs = summary_masked["kx0_frame_difference_rms"] ** 2 + summary_masked["kx_nonzero_frame_difference_rms"] ** 2
        rhs = summary_masked["band_frame_difference_rms"] ** 2
        self.assertAlmostEqual(lhs, rhs, places=14)
        self.assertAlmostEqual(
            summary_masked["kx0_frame_difference_power_pct"] + summary_masked["kx_nonzero_frame_difference_power_pct"],
            100.0,
            places=11,
        )

        # On full rows where blotch has zero row mean, stripe and blotch separate cleanly.
        full_mask = np.ones((12, 20), dtype=bool)
        s_stripe = seam_diag.summarize_stage_regions(raw, stripe * np.ones((1, 1, 20)), stripe * np.ones((1, 1, 20)), {"r": full_mask})["r"]
        s_blotch = seam_diag.summarize_stage_regions(raw, blotch, blotch, {"r": full_mask})["r"]
        self.assertAlmostEqual(s_stripe["kx_nonzero_frame_difference_rms"], 0.0, places=14)
        self.assertAlmostEqual(s_blotch["kx0_frame_difference_rms"], 0.0, places=14)

    def test_surface_isolated_bandpass_blocks_seam_and_cross_surface_ringing(self):
        rng = np.random.default_rng(7)
        height, width = 96, 96
        seam_y = 47.5
        yy = np.arange(height)[:, None]
        ceiling = np.broadcast_to(yy < seam_y, (height, width))
        wall = ~ceiling
        stable = np.broadcast_to(np.abs(yy - seam_y) >= 2.0, (height, width))
        x_mask = np.ones(width, dtype=bool)
        near_ceiling = ceiling & stable & (np.abs(yy - seam_y) <= 16.0)

        # Ceiling is completely static (constant 3.0); seam rows oscillate wildly and wall has heavy 2D noise.
        frames = np.full((16, height, width), 3.0, dtype=np.float32)
        seam_rows = ~stable[:, 0]
        alt = np.array([1.5 if (t % 2 == 0) else -1.5 for t in range(16)], dtype=np.float32)[:, None, None]
        frames[:, seam_rows, :] += alt
        frames[:, wall] += rng.standard_normal((16, int(wall.sum()))).astype(np.float32) * 0.8

        transfer = seam_diag.radial_bandpass_transfer((height + 64, width + 64))
        bp_raw = seam_diag.apply_bandpass_2d(frames, transfer=transfer, pad=32)
        bp_iso, bounds = seam_diag.compute_surface_isolated_bandpass(
            frames, ceiling, wall, stable, x_mask, seam_local_y=seam_y, transfer=transfer, pad=32
        )

        self.assertEqual(bounds["ceiling_last_stable_row"], 45)
        self.assertEqual(bounds["wall_first_stable_row"], 50)

        summary = seam_diag.summarize_stage_regions(frames, bp_raw, bp_iso, {"near_ceiling": near_ceiling}, period=8)[
            "near_ceiling"
        ]
        # Raw 2D bandpass leaks massive cross-seam ringing onto the static ceiling; surface_isolated is identically zero.
        self.assertGreater(summary["band_frame_difference_rms"], 0.05)
        self.assertAlmostEqual(summary["surface_isolated"]["band_frame_difference_rms"], 0.0, places=6)


if __name__ == "__main__":
    unittest.main()
