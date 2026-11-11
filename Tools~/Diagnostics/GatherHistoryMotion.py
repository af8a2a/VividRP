"""Check disocclusion history inheritance from GatherHistory motion GPU readbacks."""

import argparse
import csv
import json
from pathlib import Path

import numpy as np

from GatherHistoryExperiment import source_cards, unpack_pixel_history
from ProbeAnchorCalibration import LUMA, normal_from_packed


def world(pixel, depth, inverse):
    ndc = np.stack([(pixel[..., 0] + .5) / 1920 * 2 - 1,
                    1 - (pixel[..., 1] + .5) / 1080 * 2, depth, np.ones_like(depth)], -1)
    point = ndc @ inverse.T
    return point[..., :3] / point[..., 3:4]


def analyze(root):
    records = list(csv.DictReader((root / "frames.csv").open(encoding="utf8")))
    arms = sorted({r["arm"] for r in records})
    yy, xx = np.mgrid[480:736, 940:1260]
    pixels = np.stack([xx, yy], -1)
    report = {}
    trajectories = []
    canonical_source = None
    for arm in arms:
        rows = sorted((r for r in records if r["arm"] == arm), key=lambda r: int(r["frame"]))
        assert [int(r["frame"]) for r in rows] == list(range(128))
        vp = np.array([np.fromstring(r["vp"], sep=" ").reshape(4, 4) for r in rows])
        trajectories.append(vp)
        inv = np.linalg.inv(vp)
        geometry = np.stack([np.fromfile(root / f"{arm}-{i:02}-edge-geometry.u32", "<u4").reshape(256, 320)
                             for i in range(128)])
        depth = (geometry & 65535).astype("<u2").view("<f2").astype(float)
        normals = normal_from_packed((geometry >> 16).astype(float))
        history = np.stack([np.fromfile(root / f"{arm}-{i:02}-edge-history.f16", "<f2").reshape(256, 320, 4)
                            for i in range(128)])
        resolve = np.stack([np.fromfile(root / f"{arm}-{i:02}-edge-resolve.f16", "<f2").reshape(256, 320, 4)
                            for i in range(128)]).astype(float)
        temporal = np.stack([np.fromfile(root / f"{arm}-{i:02}-edge-temporal.f16", "<f2").reshape(256, 320, 4)
                             for i in range(128)]).astype(float)
        age, _, status = unpack_pixel_history(history[..., 3])
        disoccluded = np.zeros(depth.shape, dtype=bool)
        for i in range(1, 128):
            position = world(pixels, depth[i], inv[i])
            clip = np.concatenate([position, np.ones((*position.shape[:-1], 1))], -1) @ vp[i - 1].T
            projected = clip[..., :3] / clip[..., 3:4]
            previous_pixel = np.stack([(projected[..., 0] + 1) * 960 - .5,
                                       (1 - projected[..., 1]) * 540 - .5], -1)
            nearest = np.rint(previous_pixel).astype(int)
            valid = ((depth[i] > 0) & (clip[..., 3] > 0) & (nearest[..., 0] >= 940)
                     & (nearest[..., 0] < 1260) & (nearest[..., 1] >= 480) & (nearest[..., 1] < 736))
            ix, iy = np.clip(nearest[..., 0] - 940, 0, 319), np.clip(nearest[..., 1] - 480, 0, 255)
            previous_depth = depth[i - 1, iy, ix]
            previous_position = world(nearest, previous_depth, inv[i - 1])
            expected_position = world(nearest, projected[..., 2], inv[i - 1])
            delta = previous_position - expected_position
            normal = normals[i - 1, iy, ix]
            plane = np.maximum(np.abs(np.sum(delta * normal, -1)), np.abs(np.sum(delta * normals[i], -1)))
            # Conservative, clear foreground occluder only: avoid half-depth quantization and mixed edge taps.
            disoccluded[i] = (valid & (previous_depth > projected[..., 2]) & (plane > .075)
                              & (np.linalg.norm(delta, axis=-1) > .10) & (np.abs(temporal[i, ..., 3]) > 0))
        raw_y = (resolve[..., :3] * resolve[..., 3:4]) @ LUMA
        temporal_y = (temporal[..., :3] * temporal[..., 3:4]) @ LUMA
        error = np.abs(temporal_y - raw_y)[disoccluded]
        first = source_cards(root / f"{arm}-00-radiosity.f16")
        last = source_cards(root / f"{arm}-127-radiosity.f16")
        assert np.array_equal(first.view("<u2"), last.view("<u2")), "Source changed during motion"
        if canonical_source is None:
            canonical_source = first
        assert np.array_equal(first.view("<u2"), canonical_source.view("<u2")), "Source differs between arms"
        assert disoccluded.any(), "Trajectory did not exercise disocclusion"
        report[arm.split("-spatial-")[1].removesuffix("-diagnostics")] = {
            "frames": 128, "disoccluded_samples": int(disoccluded.sum()),
            "disoccluded_frames": int(disoccluded.any((1, 2)).sum()),
            "history_age_gt_one_percent": float(np.mean(age[disoccluded] > 1) * 100),
            "history_status_accepted_percent": float(np.mean(status[disoccluded] == 1) * 100),
            "temporal_minus_current_y_p99": float(np.percentile(error, 99)),
            "temporal_minus_current_y_max": float(error.max()),
            "frozen_identical_radiosity": True,
        }
    assert np.array_equal(trajectories[0], trajectories[1]), "A/B camera trajectories differ"
    result = {"scope": "Pixel history inheritance, not GI ground truth or all probe-level ghosting",
              "identical_trajectory": True, "arms": report}
    (root / "motion-summary.json").write_text(json.dumps(result, indent=2), encoding="utf8")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    analyze(parser.parse_args().root)
