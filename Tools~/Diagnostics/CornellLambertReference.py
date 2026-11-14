"""Independent Lambert box/quad reference: scene-linear incident diffuse E/pi.

Uses exact fixture geometry, constant linear reflectance and area-light next-event
estimation. No SDF, cards, SH, exposure or fitted gain. Not an OpenPBR reference.
"""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np


FIXTURE = Path(__file__).parents[1] / "UnrealCornellReference/unity_fixture.json"
EPS = 1e-6


class Scene:
    def __init__(self, fixture):
        self.items = fixture["geometry"]
        self.centers = np.array([o["position"] for o in self.items], float)
        self.half = np.array([np.multiply(o["meshBounds"], o["scale"])*.5 for o in self.items])
        self.albedo = np.array([o["baseColorLinear"] for o in self.items], float)
        self.emission = np.array([o["emissionLinear"] for o in self.items], float)
        assert np.all((self.albedo >= 0) & (self.albedo <= 1))
        assert np.isfinite(self.centers).all() and np.isfinite(self.half).all()
        assert np.isfinite(self.emission).all() and np.all(self.emission >= 0)
        self.rotations = []
        for o in self.items:
            x, y, z = np.radians(o["rotation"])
            if abs(x) > 1e-10 or abs(z) > 1e-10:
                raise ValueError("This exported fixture supports Y-rotated boxes only")
            c, s = np.cos(y), np.sin(y)
            self.rotations.append(np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]]))
        lights = np.flatnonzero(np.any(self.emission > 0, axis=1))
        assert len(lights) == 1
        self.light = int(lights[0])
        assert self.half[self.light, 1] == 0 and np.all(self.half[self.light, [0, 2]] > 0)
        assert all(np.all(h > 0) for i, h in enumerate(self.half) if i != self.light)
        self.light_normal = np.array([0., -1., 0.]) @ self.rotations[self.light].T
        self.area = 4*self.half[self.light, 0]*self.half[self.light, 2]

    def intersect(self, origins, directions):
        """Closest opaque hit; the emitter blocks on both sides and emits downward."""
        count = len(origins)
        nearest = np.full(count, np.inf)
        indices = np.full(count, -1, int)
        normals = np.zeros((count, 3))
        for i, (center, half, rotation) in enumerate(zip(self.centers, self.half, self.rotations)):
            o, d = (origins-center)@rotation, directions@rotation
            if i == self.light:
                t = np.divide(-o[:, 1], d[:, 1], out=np.full(count, np.inf), where=abs(d[:, 1]) > 1e-14)
                # Finite placeholder avoids inf*0 for parallel rays.
                p = o+d*np.where(np.isfinite(t), t, 0)[:, None]
                valid = (t > EPS*.1) & (abs(p[:, 0]) <= half[0]) & (abs(p[:, 2]) <= half[2])
                n = np.broadcast_to(self.light_normal, (count, 3)).copy()
            else:
                inverse = np.divide(1., d, out=np.zeros_like(d), where=abs(d) > 1e-14)
                lo, hi = (-half-o)*inverse, (half-o)*inverse
                near, far = np.minimum(lo, hi), np.maximum(lo, hi)
                parallel = abs(d) <= 1e-14
                inside = abs(o) <= half
                near = np.where(parallel, np.where(inside, -np.inf, np.inf), near)
                far = np.where(parallel, np.where(inside, np.inf, -np.inf), far)
                enter, leave = near.max(1), far.min(1)
                t = np.where(enter > EPS*.1, enter, leave)
                valid = (leave >= np.maximum(enter, EPS*.1)) & (t > EPS*.1)
                p = o+d*np.where(np.isfinite(t), t, 0)[:, None]
                axis = np.where(enter > EPS*.1, near.argmax(1), far.argmin(1))
                n = np.zeros_like(p)
                n[np.arange(count), axis] = np.where(p[np.arange(count), axis] >= 0, 1., -1.)
                n = n@rotation.T
            select = valid & (t < nearest)
            nearest[select], indices[select], normals[select] = t[select], i, n[select]
        # Opaque diffuse reflection is two-sided; emission has its own facing rule.
        normals *= np.where(np.sum(normals*directions, 1) > 0, -1., 1.)[:, None]
        return nearest, indices, normals

    def light_points(self, uv):
        local = np.zeros((len(uv), 3))
        local[:, [0, 2]] = (2*uv-1)*self.half[self.light, [0, 2]]
        return local@self.rotations[self.light].T+self.centers[self.light]

    def direct(self, positions, normals, uv):
        light = self.light_points(uv)
        delta = light-positions
        distance2 = np.sum(delta*delta, 1)
        directions = delta/np.sqrt(np.maximum(distance2, 1e-20))[:, None]
        cosine = np.maximum(0, np.sum(normals*directions, 1))
        facing = np.maximum(0, -directions@self.light_normal)
        factor = self.area*cosine*facing/(np.pi*np.maximum(distance2, 1e-20))
        selected = factor > 0
        visible = np.zeros(len(positions), bool)
        if selected.any():
            start = positions[selected]+EPS*normals[selected]
            direction = light[selected]-start
            direction /= np.linalg.norm(direction, axis=1)[:, None]
            _, hit, _ = self.intersect(start, direction)
            visible[selected] = hit == self.light
        return self.emission[self.light]*(factor*visible)[:, None]


def cosine_sample(normals, uv):
    tangent = np.cross(normals, np.where((abs(normals[:, 1]) < .9)[:, None], [0., 1., 0.], [1., 0., 0.]))
    tangent /= np.linalg.norm(tangent, axis=1)[:, None]
    bitangent = np.cross(normals, tangent)
    r, phi = np.sqrt(uv[:, 0]), 2*np.pi*uv[:, 1]
    return (tangent*(r*np.cos(phi))[:, None]+bitangent*(r*np.sin(phi))[:, None]
            + normals*np.sqrt(1-uv[:, 0])[:, None])


def estimate(scene, position, normal, samples, bounces, seed):
    """bounces=0 includes direct emission; bounces=8 adds eight diffuse reflections."""
    assert samples >= 2 and 0 <= bounces <= 32
    position = np.asarray(position, float)
    normal = np.array(normal, dtype=float, copy=True)
    assert position.shape == normal.shape == (3,) and np.isfinite(position).all() and np.isfinite(normal).all()
    assert np.linalg.norm(normal) > 1e-12
    normal /= np.linalg.norm(normal)
    p = np.broadcast_to(position, (samples, 3)).copy()
    n = np.broadcast_to(normal, p.shape).copy()
    throughput = np.ones_like(p)
    active = np.arange(samples)
    by_order = np.zeros((samples, bounces+1, 3))
    rng = np.random.default_rng(seed)
    for order in range(bounces+1):
        by_order[active, order] = throughput*scene.direct(p, n, rng.random((len(active), 2)))
        if order == bounces:
            break
        direction = cosine_sample(n, rng.random((len(active), 2)))
        start = p+EPS*n
        distance, hit, new_normal = scene.intersect(start, direction)
        keep = hit >= 0
        if not keep.any():
            break
        # Emission is accounted for at the preceding vertex by area sampling.
        # A BSDF hit on the emitter can still reflect diffuse light; do not add Le twice.
        active = active[keep]
        p = start[keep]+direction[keep]*distance[keep, None]
        n = new_normal[keep]
        throughput = throughput[keep]*scene.albedo[hit[keep]]
    values = by_order.sum(1)
    assert np.isfinite(values).all() and np.all(values >= 0)
    return dict(samples=samples, diffuseReflections=bounces, seed=seed,
                meanRgb=values.mean(0).tolist(), iidStandardErrorRgb=(values.std(0, ddof=1)/np.sqrt(samples)).tolist(),
                meanRgbByOrder=by_order.mean(0).tolist())


def camera_points(scene, fixture):
    # GPU readback Y is inverted relative to normalized EXR/camera rows.
    points = []
    camera = fixture["camera"]
    assert np.max(abs(np.array(camera["euler"]))) < 1e-8
    scale = np.tan(np.radians(camera["verticalFov"])*.5)
    for name, x, gpu_y in (("left", 430, 562), ("right", 1490, 562), ("back", 1180, 660),
                            ("floor", 960, 162), ("ceiling", 960, 994)):
        direction = np.array([((x+.5)/1920*2-1)*scale*camera["aspect"],
                              ((gpu_y+.5)/1080*2-1)*scale, 1.])
        direction /= np.linalg.norm(direction)
        start = np.array(camera["position"], float)
        distance, hit, normal = scene.intersect(start[None], direction[None])
        assert hit[0] >= 0
        points.append(dict(name=name, pixelGpu=[x, gpu_y], object=scene.items[hit[0]]["name"],
                           position=(start+direction*distance[0]).tolist(), normal=normal[0].tolist()))
    return points


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("--fixture", type=Path, default=FIXTURE)
    parser.add_argument("--points", type=Path)
    parser.add_argument("--samples", type=int, default=16384)
    parser.add_argument("--bounces", type=int, default=8)
    parser.add_argument("--seed", type=int, default=20261114)
    args = parser.parse_args()
    assert not args.output.exists(), "Refusing to overwrite reference evidence"
    fixture = json.loads(args.fixture.read_text(encoding="utf-8-sig"))
    scene = Scene(fixture)
    points = json.loads(args.points.read_text()) if args.points else camera_points(scene, fixture)
    report = dict(schema="cornell-lambert-reference.v1", quantity="Incident diffuse E/pi, linear RGB",
        scope="Independent exact box/quad Lambert model; no SDF, cache or reconstruction. Not OpenPBR or an engine-equivalence claim.",
        emitterFacing="Downward emission; opaque diffuse reflection on both sides, including the emitter",
        fixtureSha256=hashlib.sha256(args.fixture.read_bytes()).hexdigest(), points=[])
    for i, point in enumerate(points):
        report["points"].append(dict(point, estimate=estimate(scene, point["position"], point["normal"], args.samples, args.bounces, args.seed+i)))
    args.output.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))
