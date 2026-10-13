"""Independent FP64 geometry oracles for the production meshlet GPU predicates.

Run Validate-MeshletCulling.ps1; no Unity tests or third-party Python packages.
Inputs are rounded to FP32 before deriving the reference geometry. FP16 may keep
extra geometry, but must never reject a potentially visible box or normal cone.
"""
import argparse
import itertools
import json
import math
from pathlib import Path
import random
import struct

CASE = struct.Struct('<108f')
RESULT = struct.Struct('<8I4f4I')
IDENTITY = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]
PLANES = [1, 0, 0, 10, -1, 0, 0, 10, 0, 1, 0, 10,
          0, -1, 0, 10, 0, 0, 1, 10, 0, 0, -1, 10]


def dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def transform(m, p):
    return [dot(m[r:r + 4], p) for r in range(0, 16, 4)]


def norm(v):
    return math.hypot(*v)


def unit(v):
    n = norm(v)
    return [x / n for x in v] if n else [0, 0, 0]


def finite(v):
    return all(math.isfinite(x) for x in v)


def determinant(m):
    return (m[0] * (m[5] * m[10] - m[6] * m[9])
            - m[1] * (m[4] * m[10] - m[6] * m[8])
            + m[2] * (m[4] * m[9] - m[5] * m[8]))


def case(sphere=(0, 0, 5, 0.2), matrix=IDENTITY, planes=PLANES, vp=None,
         inverse=IDENTITY, cone=(0, 0, 1, 0.2), camera=(0, 0, 0, 1),
         forward=(0, 0, 1, 0), settings=(1024, 1024, 1, 0), depths=(0.8,) * 4,
         test=(0.5, 0, 1, 0), pixels=(10, 10, 20, 20), texture=(1024, 1024, 11, 0)):
    if vp is None:
        vp = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0.1, 0, 0, 1, 0]
    return CASE.unpack(CASE.pack(*sphere, *matrix, *planes, *vp, *inverse, *cone,
                                 *camera, *forward, *settings, *depths, *test, *pixels, *texture))


def points(c):
    if not finite(c[:20]) or c[3] < 0 or c[16:20] != (0, 0, 0, 1):
        return None
    return [transform(c[4:20], [*(c[i] + sign[i] * c[3] for i in range(3)), 1])
            for sign in itertools.product((-1, 1), repeat=3)]


def frustum_visible(c, pts):
    if pts is None:
        return True
    for start in range(20, 44, 4):
        plane = c[start:start + 4]
        if finite(plane) and max(map(abs, plane[:3])) > 0:
            if max(dot(plane, p) for p in pts) < 0:
                return False
    return True


def cone_visible(c):
    sphere, inv, cone = c[:4], c[60:76], c[76:80]
    if (not c[98] or not finite(sphere + inv + cone) or sphere[3] < 0
            or not 0 <= cone[3] < 1 or inv[12:16] != (0, 0, 0, 1)
            or determinant(inv) == 0 or norm(cone[:3]) <= 1e-20):
        return True
    if c[83]:
        camera = transform(inv, [*c[80:83], 1])
        view = [sphere[i] - camera[i] for i in range(3)]
    else:
        view = transform(inv, [*c[84:87], 0])[:3]
    distance = norm(view)
    if not finite(view) or not math.isfinite(distance) or distance == 0:
        return True
    cutoff = cone[3]
    if c[83]:
        # Exact angular composition of the normal cone and all rays through the
        # local sphere; independent of the shader's simpler sufficient condition.
        sin_view = sphere[3] / distance
        cos_normal = math.sqrt(1 - cutoff * cutoff)
        if sin_view >= cos_normal:
            return True
        cutoff = cutoff * math.sqrt(1 - sin_view * sin_view) + cos_normal * sin_view
    sign = -1 if c[87] else 1
    return sign * dot(unit(view), unit(cone[:3])) <= cutoff


def project(c, pts):
    if pts is None or not finite(c[44:60]) or not finite(c[88:90]) or min(c[88:90]) < 1:
        return None
    clips = [transform(c[44:60], p) for p in pts]
    if any(not finite(p) or p[3] <= 0 or p[2] <= 0 or p[2] >= p[3] for p in clips):
        return None
    ndc = [[p[0] / p[3], p[1] / p[3] * (-1 if c[90] else 1), p[2] / p[3]] for p in clips]
    lo = [(min(p[i] for p in ndc) * 0.5 + 0.5) * c[88 + i] for i in range(2)]
    hi = [(max(p[i] for p in ndc) * 0.5 + 0.5) * c[88 + i] for i in range(2)]
    return lo, hi, max(p[2] for p in ndc)


def depth_visible(c):
    depths, depth, bias = c[92:96], c[96], c[97]
    return (not finite((*depths, depth, bias)) or min(depths) < 0 or max(depths) > 1
            or max(0, min(1, depth + max(0, bias))) >= min(depths))


def generate(root):
    cases, controls, groups = [], {}, {}

    def add(c, group, **expected):
        if expected:
            controls[str(len(cases))] = expected
        cases.append(c)
        groups[group] = groups.get(group, 0) + 1

    add(case(), 'inside_occluded_backface', frustum=1, cone=0, query=1, occlusion=0, footprint=1)
    add(case(forward=(0, 0, 1, 1)), 'front_cull', cone=1)
    add(case(cone=(0, 0, -1, 0.2)), 'facing_camera', cone=1)
    add(case(sphere=(0, 0, 0, 1)), 'camera_inside', cone=1, query=0)
    add(case(sphere=(0, 0, 0.12, 0.04)), 'near_crossing', query=0, occlusion=1)
    add(case(sphere=(0, 0, -2, 0.2)), 'behind_camera', query=0, occlusion=1)
    add(case(sphere=(0, 0, 5, 0)), 'point_meshlet', query=1)
    add(case(sphere=(4.9, 0, 5, 0.3)), 'previous_view_edge', query=0, occlusion=1)
    add(case(sphere=(0, 0, 5, 2)), 'large_rectangle', query=1, occlusion=0)
    for axis in range(3):
        for sign in (-1, 1):
            center = [0, 0, 0]
            center[axis] = sign * 11
            add(case(sphere=(*center, 1)), 'frustum_contact', frustum=1)
            center[axis] = sign * 20
            add(case(sphere=(*center, 1)), 'outside_each_plane', frustum=0)
    shear = [1, 1, 1, 12.8, 0, 0.01, 0, 0, 0, 0, 0.01, 0, 0, 0, 0, 1]
    add(case(sphere=(0, 0, 0, 1), matrix=shear), 'shear_contact', frustum=1)
    for sphere in ((math.nan, 0, 0, 1), (0, 0, 0, -1), (0, 0, 0, math.inf)):
        add(case(sphere=sphere), 'invalid_sphere', frustum=1, cone=1, query=0)
    for bad in (math.nan, math.inf):
        add(case(vp=[bad] * 16), 'invalid_projection', query=0)
        add(case(cone=(bad, 0, 1, 0.2)), 'invalid_cone', cone=1)
        add(case(inverse=[bad] * 16), 'invalid_inverse', cone=1)
        add(case(depths=(0.8, bad, 0.8, 0.8)), 'invalid_depth', depth=1, occlusion=1)
    add(case(inverse=[0] * 12 + [0, 0, 0, 1]), 'singular_inverse', cone=1)
    add(case(cone=(0, 0, 0, 0.2)), 'zero_axis', cone=1)
    add(case(camera=(0, 0, 0, 0), forward=(0, 0, 0, 0)), 'zero_direction', cone=1)
    add(case(test=(0.5, 0, 0, 0)), 'disabled_cone', cone=1)
    add(case(cone=(0, 0, 1, 1)), 'wide_cone', cone=1)
    for depth, depths, bias, expected in (
        (0.5, (0.5,) * 4, 0, 1), (0, (0,) * 4, 0, 1),
        (0.1, (0.8,) * 4, 0, 0), (0.9, (0.8,) * 4, 0, 1),
        (0.5, (0.6,) * 4, 0.2, 1), (0.5, (0.5,) * 4, -1, 1),
        (0.5, (0.8, 0, 0.8, 0.8), 0, 1), (0.5, (1.1,) * 4, 0, 1),
        (math.nan, (0.8,) * 4, 0, 1), (0.5, (0.8,) * 4, math.nan, 1)):
        add(case(depths=depths, test=(depth, bias, 1, 0)), 'depth_comparison', depth=expected)
    for pixels, texture, expected in (
        ((10, 10, 11, 11), (1024, 1024, 11, 0), 1),
        ((10, 10, 20, 20), (1024, 1024, 1, 0), 0),
        ((1, 1, 8, 8), (9, 9, 4, 0), 0),
        ((0, 0, 6, 6), (9, 9, 4, 0), 1),
        ((0, 0, 100, 100), (1024, 1024, 0, 0), 0),
        ((20, 20, 10, 10), (1024, 1024, 11, 0), 0)):
        add(case(pixels=pixels, texture=texture, test=(0.5, 0, 1, 1)), 'hzb_footprint', footprint=expected)

    rng = random.Random(1639041)
    for i in range(8192):
        scale = 2.0 ** rng.randint(-100, 65)
        m = [rng.uniform(-2, 2) * scale * (2 ** rng.randint(0, 20) if col == 3 else 1)
             for row in range(3) for col in range(4)] + [0, 0, 0, 1]
        sphere = [rng.uniform(-5, 5) for _ in range(3)] + [2.0 ** rng.randint(-14, 5)]
        base = case(sphere=sphere, matrix=m)
        n = unit([rng.uniform(-1, 1) for _ in range(3)])
        if i % 11 == 0:
            n = [x * 4 for x in n]
        support = max(dot(n, p) for p in points(base))
        offset = -support + rng.choice((-1, 0, 1)) * scale * 2.0 ** rng.randint(-20, -1)
        planes = [0] * 24
        planes[(i % 6) * 4:(i % 6) * 4 + 4] = [*n, offset]
        add(case(sphere=base[:4], matrix=base[4:20], planes=planes), 'affine_frustum_boundary')

    for i in range(8192):
        # Perspective/orthographic, jitter, shear, reflected winding and large
        # opposing translations in model and camera transforms.
        distance = 2.0 ** rng.uniform(-2, 10)
        scale = distance * rng.uniform(0.001, 0.06)
        offset = 2.0 ** rng.randint(0, 25) if i % 7 == 0 else 0
        m = [rng.uniform(-2, 2) * scale if col < 3 else 0 for row in range(3) for col in range(4)] + [0, 0, 0, 1]
        m[3], m[7], m[11] = rng.uniform(-0.9, 0.9) * distance + offset, rng.uniform(-0.9, 0.9) * distance, distance
        vp = [1, 0, rng.uniform(-0.1, 0.1), -offset, 0, 1, rng.uniform(-0.1, 0.1), 0,
              0, 0, 0, 0.1, 0, 0, 1, 0]
        if i % 3 == 0:
            vp = [1 / distance, 0, 0, -offset / distance, 0, 1 / distance, 0, 0,
                  0, 0, -1 / (distance * 4), 1, 0, 0, 0, 1]
        add(case(sphere=(0, 0, 0, 1), matrix=m, vp=vp,
                 settings=(1024, 768, i % 2, 0), texture=(1024, 768, 11, 0)), 'affine_projection')

    for i in range(8192):
        inv_scale = 2.0 ** rng.randint(-60, 60)
        inv = [rng.uniform(-2, 2) * inv_scale for _ in range(12)] + [0, 0, 0, 1]
        sphere = [rng.uniform(-10, 10) * inv_scale for _ in range(3)] + [rng.uniform(0, 0.1) * inv_scale]
        camera = [rng.uniform(-2, 2) for _ in range(3)] + [i % 2]
        forward = unit([rng.uniform(-1, 1) for _ in range(3)]) + [int(i % 3 == 0)]
        v = ([sphere[j] - transform(inv, [*camera[:3], 1])[j] for j in range(3)]
             if camera[3] else transform(inv, [*forward[:3], 0])[:3])
        axis = unit([x + rng.uniform(-1, 1) * norm(v) * 0.3 for x in v])
        sign = -1 if forward[3] else 1
        # Cutoffs dense around the silhouette, plus well-separated rejection cases.
        cutoff = max(0, min(0.9999, sign * dot(unit(v), axis) + rng.choice((-0.2, -0.0001, 0, 0.0001))))
        add(case(sphere=sphere, inverse=inv, camera=camera, forward=forward,
                 cone=(*axis, cutoff)), 'affine_cone_boundary')

    while len(cases) % 64:
        add(case(), 'padding')
    root.mkdir(parents=True, exist_ok=True)
    (root / 'cases.bin').write_bytes(b''.join(CASE.pack(*c) for c in cases))
    (root / 'controls.json').write_text(json.dumps(controls))
    (root / 'groups.json').write_text(json.dumps(groups, indent=2))
    print(f'Generated {len(cases)} cases: {json.dumps(groups)}')


def check(root, result_path):
    cases = list(CASE.iter_unpack((root / 'cases.bin').read_bytes()))
    results = list(RESULT.iter_unpack(result_path.read_bytes()))
    controls = json.loads((root / 'controls.json').read_text())
    assert len(cases) == len(results), 'GPU result count mismatch'
    rejected_frustum = rejected_cone = queries = occluded = 0
    for index, (c, r) in enumerate(zip(cases, results)):
        def require(ok, reason):
            if not ok:
                raise AssertionError(f'case {index}: {reason}; result={r}; input={c}')
        pts = points(c)
        require(r[0] or not frustum_visible(c, pts), 'false frustum rejection')
        require(r[1] or not cone_visible(c), 'false cone rejection')
        require(bool(r[9]) == depth_visible(c), 'depth comparison')
        exact_projection = project(c, pts)
        if r[2]:
            queries += 1
            require(exact_projection is not None, 'query crossing clip plane/invalid input')
            lo, hi, closest = exact_projection
            require(all(r[4 + j] <= math.floor(lo[j]) and r[6 + j] >= math.floor(hi[j]) for j in range(2)),
                    'screen rectangle does not enclose geometry')
            require(r[8] >= closest, 'closest depth rounded away from camera')
            if not r[10]:
                require(closest < min(c[92:96]), 'false occlusion rejection')
        else:
            require(r[10] == 1, 'invalid query must remain visible')
        if r[3]:
            pixel_lo, pixel_hi = (c[100:102], c[102:104]) if c[99] else (r[4:6], r[6:8])
            block = 2 ** r[12]
            require(r[12] < c[106], 'unavailable mip')
            for j in range(2):
                begin, end = int(pixel_lo[j]) // block, int(pixel_hi[j]) // block
                require(0 <= begin <= end < max(1, int(c[104 + j]) // block) and end - begin <= 1,
                        'footprint/tail not covered by four loads')
        observed = dict(frustum=r[0], cone=r[1], query=r[2], footprint=r[3], depth=int(r[9]), occlusion=int(r[10]))
        for name, expected in controls.get(str(index), {}).items():
            require(observed[name] == expected, f'{name} control expected {expected}')
        rejected_frustum += not r[0]
        rejected_cone += not r[1]
        occluded += not r[10]
    require(rejected_frustum > 100 and rejected_cone > 100 and queries > 1000 and occluded > 100,
            'insufficient nontrivial coverage')
    print(f'PASS {result_path.name}: {len(cases)} cases, zero false rejections; '
          f'frustum rejected={rejected_frustum}, cone rejected={rejected_cone}, '
          f'valid HZB queries={queries}, occluded={occluded}')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=('generate', 'check'))
    parser.add_argument('directory', type=Path)
    parser.add_argument('--results', type=Path)
    args = parser.parse_args()
    if args.action == 'generate':
        generate(args.directory)
    else:
        check(args.directory, args.results)
