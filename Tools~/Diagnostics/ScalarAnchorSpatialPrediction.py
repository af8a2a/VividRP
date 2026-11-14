"""Offline scalar-flux reconstruction hypothesis; validate against real one-pass SH first.

Uses the analytic Cornell geometry only for the existing connection visibility
test, never to synthesize GI. All radiance inputs are actual GPU readbacks.
"""
import argparse
import json
from pathlib import Path

import numpy as np

from EmissiveRadiosityConvergence import plane_masks
from LinearGiExport import FIXTURE, expected_geometry
from ProbeAnchorCalibration import evaluate_sh, normal_from_packed
from TemporalSamplingExperiment import LUMA, metrics, read


def connections(geometry, vp, radius_scale=1.0):
    g = geometry[:68, :120]
    h, w = g.shape[:2]
    normal = normal_from_packed(np.maximum(g[..., 3], 0))
    fixture = json.loads(FIXTURE.read_text(encoding="utf-8"))
    ids, full_normal, depth, _, _, _, _ = expected_geometry(fixture, 1920, 1080)
    tangent = np.tan(np.deg2rad(fixture["camera"]["verticalFov"]) * .5)
    yy, xx = np.mgrid[:1080, :1920]
    rays = np.stack([((xx+.5)*2/1920-1)*tangent*(1920/1080), (1-(yy+.5)*2/1080)*tangent, np.ones_like(xx)], -1)
    positions = (np.asarray(fixture["camera"]["position"]) + rays*depth[..., None])[::-1]
    full_normal = full_normal[::-1]
    clip = np.concatenate([g[..., :3], np.ones((h, w, 1))], -1) @ vp.T
    uv = np.stack([.5+.5*clip[..., 0]/clip[..., 3], .5-.5*clip[..., 1]/clip[..., 3]], -1)
    yy, xx = np.mgrid[:h, :w]
    center_id = (yy*w+xx).ravel()
    result = []
    for dy in range(-2, 3):
        for dx in range(-2, 3):
            if dx == dy == 0:
                continue
            ox, oy = xx+dx, yy+dy
            inside = (ox >= 0) & (ox < w) & (oy >= 0) & (oy < h)
            ox, oy = np.clip(ox, 0, w-1), np.clip(oy, 0, h-1)
            other, on = g[oy, ox], normal[oy, ox]
            delta = other[..., :3] - g[..., :3]
            distance = np.linalg.norm(delta, axis=-1)
            tolerance = .002+.008*distance
            alignment = np.sum(normal*on, -1)
            plane = np.maximum(abs(np.sum(delta*normal, -1)), abs(np.sum(delta*on, -1)))
            radius = np.clip(.08*np.minimum(g[..., 2]+3.8, other[..., 2]+3.8), .08, .35)*radius_scale
            valid = inside & (g[..., 3] >= 0) & (other[..., 3] >= 0) & (alignment >= .995) & (plane < tolerance) & (distance < radius)
            for fraction in [.25, .5, .75]:
                point = uv*(1-fraction) + uv[oy, ox]*fraction
                ix, iy = np.floor(point*np.array([1920, 1080])).astype(int).transpose(2, 0, 1)
                visible = (ix >= 0) & (ix < 1920) & (iy >= 0) & (iy < 1080)
                ix, iy = np.clip(ix, 0, 1919), np.clip(iy, 0, 1079)
                pos, n = positions[iy, ix], full_normal[iy, ix]
                valid &= visible & (np.minimum(np.sum(n*normal, -1), np.sum(n*on, -1)) >= .995)
                valid &= (abs(np.sum((pos-g[..., :3])*normal, -1)) < tolerance)
                valid &= (abs(np.sum((pos-other[..., :3])*on, -1)) < tolerance)
            p = np.clip(1-plane/tolerance, 0, 1)
            a = np.clip((alignment-.995)/(.9999-.995), 0, 1)
            weight = p*p*a*a*(3-2*a) * np.exp2(-2*distance**2/radius**2)
            weight *= .12743124892*np.exp2(-.55*(dx*dx+dy*dy))
            valid = valid.ravel()
            result.append((center_id[valid], (oy*w+ox).ravel()[valid], weight.ravel()[valid]))
    return result


def filter_flux(anchors, statistics, links):
    shape = anchors.shape
    a = anchors.reshape(len(anchors), -1, 4)
    status = statistics.reshape(len(anchors), -1, 4)
    energy = a[..., :3]*a[..., 3:4]
    output = energy.copy()
    for center, other, weight in links:
        valid = (a[:, center, 3] >= .25) & (a[:, other, 3] >= .25) & (status[:, center, 3] != 3) & (status[:, other, 3] != 3)
        output[:, center] += weight[None, :, None]*valid[..., None]*(energy[:, other]-energy[:, center])
    return np.concatenate([output/np.maximum(a[..., 3:4], 1e-5), a[..., 3:4]], -1).reshape(shape)


def predict(main, snapshot, output, reference=None):
    m = json.loads((main / "capture.json").read_text(encoding="utf-8"))
    s = json.loads((snapshot / "snapshot.json").read_text(encoding="utf-8"))
    assert m["complete"] and not m["errors"] and not s["errors"]
    files = {(r["arm"], r["frame"], r["tag"]): r for r in m["records"] if "tag" in r}
    sf = {r["tag"]: r for r in s["records"] if "tag" in r}
    vp = np.asarray(next(r["vp"] for r in m["records"] if r.get("arm") == "age128" and "vp" in r)).reshape(4, 4)
    geometry = read(files["age128", 512, "geometry"])
    masks = plane_masks(geometry)
    clip = np.concatenate([geometry[:68, :120, :3], np.ones((68, 120, 1))], -1) @ vp.T
    pixel = np.stack([.5+.5*clip[..., 0]/clip[..., 3], .5-.5*clip[..., 1]/clip[..., 3]], -1)*[1920, 1080]
    rois = {}
    for name, mask in masks.items():
        r = files["age128", 512, "final-"+name]
        selected = mask & np.all(pixel >= [r["x"], r["y"]], -1) & np.all(pixel < [r["x"]+r["width"], r["y"]+r["height"]], -1)
        assert selected.any(), name
        rois["roi-"+name] = selected
    links = connections(geometry, vp)
    report = {"scope": "CPU prediction only; requires actual GPU A/B before acceptance", "snapshot_validation": {}, "passes": {}, "reference": {}}
    sg = read(sf["geometry"])
    assert np.allclose(sg[..., :3], geometry[..., :3], atol=1e-5, rtol=0) and np.array_equal(sg[..., 3], geometry[..., 3])
    anchor = read(sf["anchor"])[:68, :120][None]
    statistics = read(sf["statistics"])[:68, :120][None]
    reconstructed = filter_flux(anchor, statistics, links)
    sh = read(sf["sh"])[:68].reshape(68, 7, 120, 4).transpose(1, 0, 2, 3).reshape(1, 7, -1, 4)
    normal = normal_from_packed(np.maximum(sg[:68, :120, 3], 0)).reshape(1, -1, 3)
    actual = evaluate_sh(sh, normal).reshape(1, 68, 120, 3)*anchor[..., 3:4]
    predicted = reconstructed[..., :3]*reconstructed[..., 3:4]
    for name, mask in masks.items():
        delta = (predicted-actual)[0, mask] @ LUMA
        denominator = np.mean(actual[0, mask]@LUMA)
        report["snapshot_validation"][name] = {"mean_error_percent": float(100*delta.mean()/denominator),
            "normalized_rms_percent": float(100*np.sqrt(np.mean(delta**2))/denominator)}
    a = np.stack([read(files["age128", f, "anchor"])[:68, :120] for f in range(512, 768)])
    stats = np.stack([read(files["age128", f, "statistics"])[:68, :120] for f in range(512, 768)])
    target = target_variance = None
    if reference is not None:
        r = json.loads((reference / "capture.json").read_text(encoding="utf-8"))
        assert r["complete"] and not r["errors"] and r["schema"] == "scalar-anchor-grid-reference.v1"
        assert (r["spacing"], r["rays"], r["measuredFrames"]) == (16, 8, 1024)
        rf = {(v["arm"], v["frame"], v["tag"]): v for v in r["records"] if "tag" in v}
        blocks = []
        source = Path(files["age128", 512, "radiosity"]["path"]).read_bytes()
        for arm in ["uniform-a", "uniform-b"]:
            states = [v["state"] for v in r["records"] if v.get("arm") == arm and "state" in v]
            assert len(states) == 1024 and all(v["order"] == 8 and v["batch"] == 32 and v["samples"] == 64 and v["seed"] == 0
                and not v["guide"] and not v["restir"] and v["sky"] == 0 and v["sun"] == [0, 0, 0] for v in states)
            for frame in [512, 1535]:
                assert np.array_equal(read(rf[arm, frame, "geometry"]), geometry)
                assert Path(rf[arm, frame, "radiosity"]["path"]).read_bytes() == source
                state = read(rf[arm, frame, "statistics"])
                assert np.all(state[..., 0][state[..., 0] > 0] == 1)
            for first in range(512, 1536, 64):
                raw = np.stack([read(rf[arm, f, "raw"])[:68, :120] for f in range(first, first+64)])
                blocks.append((raw[..., :3]*raw[..., 3:4]).mean(0, dtype=float))
        block_y = np.asarray(blocks) @ LUMA
        target = np.asarray(blocks).mean(0); target_variance = block_y.var(0, ddof=1)/len(blocks)
        report["reference"] = {"rays_per_probe": 16384, "scope": "Independent current-frame same-renderer grid, not a physical truth"}
    values = a.astype(float)
    for iteration in range(3):
        if iteration:
            values = filter_flux(values, stats, links)
        y = (values[..., :3]*values[..., 3:4])@LUMA
        report["passes"][str(iteration)] = {}
        for name, mask in (masks | rois).items():
            row = metrics(y[:, mask])
            if target is not None:
                delta = y[:, mask] - (target @ LUMA)[mask]
                denominator = (target @ LUMA)[mask].mean()
                row.update(mean_error_percent=float(100*delta.mean()/denominator),
                    per_frame_rmse=float(np.sqrt(np.mean(delta**2))),
                    mean_field_rmse=float(np.sqrt(np.mean(delta.mean(0)**2))),
                    reference_se_rms=float(np.sqrt(target_variance[mask].mean())))
            report["passes"][str(iteration)][name] = row
    output.mkdir(parents=True, exist_ok=True)
    (output / "prediction.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path); parser.add_argument("snapshot", type=Path); parser.add_argument("output", type=Path)
    parser.add_argument("--reference", type=Path)
    args = parser.parse_args(); predict(args.capture, args.snapshot, args.output, args.reference)
