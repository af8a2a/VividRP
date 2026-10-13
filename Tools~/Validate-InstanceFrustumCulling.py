"""Generate independent double-precision corner oracles and check GPU readback.

Use Validate-InstanceFrustumCulling.ps1 for compilation and D3D12 execution.
No Unity Editor, Unity tests, or third-party Python packages are needed.
"""
import argparse
import itertools
import json
import math
from pathlib import Path
import random
import struct

CASE = struct.Struct('<48f')
RESULT = struct.Struct('<4I4f')
IDENTITY = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]
ZERO_PLANES = [0.0] * 24
BOX_PLANES = [1, 0, 0, 10, -1, 0, 0, 10, 0, 1, 0, 10,
              0, -1, 0, 10, 0, 0, 1, 10, 0, 0, -1, 10]


def dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def case(lo=(-1, -1, -1), hi=(1, 1, 1), matrix=None, planes=None):
    # The oracle starts from exactly the FP32 inputs seen by the shader.
    values = [*lo, 0, *hi, 0, *(matrix if matrix is not None else IDENTITY),
              *(planes if planes is not None else BOX_PLANES)]
    return CASE.unpack(CASE.pack(*values))


def corners(values):
    lo, hi, m = values[:3], values[4:7], values[8:24]
    return [tuple(dot(m[row * 4:row * 4 + 3], p) + m[row * 4 + 3] for row in range(3))
            for p in itertools.product(*zip(lo, hi))]


def valid(values):
    return (all(math.isfinite(v) for v in values[:24])
            and all(a <= b for a, b in zip(values[:3], values[4:7]))
            and values[20:24] == (0, 0, 0, 1))


def visible(values, points):
    if not valid(values):
        return True
    for start in range(24, 48, 4):
        plane = values[start:start + 4]
        if not all(math.isfinite(v) for v in plane) or max(map(abs, plane[:3])) == 0:
            continue
        if max(dot(plane[:3], p) + plane[3] for p in points) < 0:
            return False
    return True


def generate(root):
    records, strict, groups = [], {}, {}

    def add(values, group, expected=None):
        groups[group] = groups.get(group, 0) + 1
        if expected is not None:
            assert visible(values, corners(values)) == expected, f'Invalid control case: {group}'
            strict[str(len(records))] = expected
        records.append(values)

    add(case(), 'inside', True)
    for axis in range(3):
        for sign in (-1, 1):
            m = IDENTITY.copy()
            m[axis * 4 + 3] = sign * 20
            add(case(matrix=m), 'outside_each_plane', False)
            m[axis * 4 + 3] = sign * 11
            add(case(matrix=m), 'touch_each_plane', True)
    m = IDENTITY.copy()
    m[11] = -3
    near = BOX_PLANES.copy()
    near[16:20] = [0, 0, 1, 0]
    add(case(matrix=m, planes=near), 'near_enabled', False)
    near[16:20] = [0, 0, 0, 0]
    add(case(matrix=m, planes=near), 'near_disabled', True)
    add(case(matrix=[1, 1, 1, 12.8, 0, 0.01, 0, 0, 0, 0, 0.01, 0, 0, 0, 0, 1]), 'shear_contact', True)
    add(case(lo=(1, 1, 1), hi=(-1, -1, -1)), 'invalid_bounds', True)
    add(case(lo=(math.nan, 0, 0)), 'nan_bounds', True)
    add(case(planes=[math.nan] * 24), 'invalid_planes', True)
    m = IDENTITY.copy()
    m[15] = 0
    add(case(matrix=m), 'projective_transform', True)
    add(case(lo=(0, 0, 0), hi=(0, 0, 0)), 'point_box', True)
    tiny_shear = [5e-31, 5e-31, 5e-31, 0] * 3 + [0, 0, 0, 1]
    add(case(matrix=tiny_shear, planes=[1 / math.sqrt(3)] * 3 + [-2.55e-30] + [0] * 20),
        'clamped_axis_scale', True)

    rng = random.Random(829441)
    for i in range(32768):
        # Affine transforms include mirrors, arbitrary shear, extreme scales,
        # flattened boxes, and offsets well outside the FP16 representable range.
        scale = 2.0 ** rng.randint(-110, 70)
        translation_scale = scale * (2.0 ** rng.randint(0, 22))
        m = [rng.uniform(-2, 2) * scale if c < 3 else rng.uniform(-2, 2) * translation_scale
             for r in range(3) for c in range(4)] + [0, 0, 0, 1]
        center = [rng.uniform(-4, 4) for _ in range(3)]
        extent = [0 if i % 17 == j else 2.0 ** rng.randint(-12, 8) for j in range(3)]
        base = case(tuple(c - e for c, e in zip(center, extent)),
                    tuple(c + e for c, e in zip(center, extent)), m, ZERO_PLANES)
        pts = corners(base)
        n = [rng.uniform(-1, 1) for _ in range(3)]
        length = math.sqrt(dot(n, n))
        n = [x / length * (4 if i % 11 == 0 else 1) for x in n]
        support = max(dot(n, p) for p in pts)
        # Dense boundary cases on both sides; oracle uses the rounded plane offset.
        offset = -support + rng.choice([-1, 0, 1]) * scale * (2.0 ** rng.randint(-20, -1))
        planes = ZERO_PLANES.copy()
        slot = (i % 6) * 4
        planes[slot:slot + 4] = [*n, offset]
        add(case(base[:3], base[4:7], base[8:24], planes), 'affine_boundary')

    # Perspective side planes, off-center/jittered frusta, and boxes crossing W=0.
    # Inward world planes are independent of reversed-Z depth-buffer encoding.
    for i in range(8192):
        sx, sy = rng.uniform(0.4, 2.5), rng.uniform(0.4, 2.5)
        ox, oy = rng.uniform(-0.2, 0.2), rng.uniform(-0.2, 0.2)
        planes = [sx, 0, 1 + ox, 0, -sx, 0, 1 - ox, 0, 0, sy, 1 + oy, 0,
                  0, -sy, 1 - oy, 0, 0, 0, 1, -0.1, 0, 0, -1, 500]
        for start in range(0, 24, 4):
            length = math.sqrt(dot(planes[start:start + 3], planes[start:start + 3]))
            planes[start:start + 4] = [v / length for v in planes[start:start + 4]]
        m = [rng.uniform(-3, 3) if c < 3 else rng.uniform(-100, 100)
             for r in range(3) for c in range(4)] + [0, 0, 0, 1]
        add(case(matrix=m, planes=planes), 'perspective_off_center')

    while len(records) % 64:
        add(case(), 'dispatch_padding', True)
    root.mkdir(parents=True, exist_ok=True)
    with (root / 'cases.bin').open('wb') as f:
        for record in records:
            f.write(CASE.pack(*record))
    (root / 'manifest.json').write_text(json.dumps({'strict': strict, 'groups': groups}, indent=2))
    print(f'Generated {len(records)} cases: {groups}')


def check(root, results):
    records = list(CASE.iter_unpack((root / 'cases.bin').read_bytes()))
    outputs = list(RESULT.iter_unpack(results.read_bytes()))
    assert len(outputs) == len(records), 'GPU output size mismatch'
    strict = json.loads((root / 'manifest.json').read_text())['strict']
    false_negatives = false_positives = rejected = sphere_failures = 0
    for i, (record, output) in enumerate(zip(records, outputs)):
        pts = corners(record)
        reference = visible(record, pts)
        actual = bool(output[0])
        if reference and not actual:
            false_negatives += 1
            if false_negatives < 5:
                print('False rejection:', i, record)
        false_positives += actual and not reference
        rejected += not actual
        if str(i) in strict:
            assert actual == strict[str(i)], f'Deterministic case {i} failed'
        if valid(record) and output[1]:
            sphere_center, radius = output[4:7], output[7]
            if any(math.dist(p, sphere_center) > radius for p in pts):
                sphere_failures += 1
    report = dict(cases=len(records), false_rejections=false_negatives,
                  conservative_retained=false_positives, rejected=rejected,
                  sphere_containment_failures=sphere_failures)
    print(json.dumps(report, indent=2))
    results.with_suffix('.json').write_text(json.dumps(report, indent=2))
    assert false_negatives == 0, 'Visible boxes were falsely rejected'
    assert sphere_failures == 0, 'Light/page sphere does not enclose transformed box'
    assert rejected > 1000, 'Culling must still reject a meaningful set of outside boxes'


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['generate', 'check'])
    parser.add_argument('directory', type=Path)
    parser.add_argument('--results', type=Path)
    args = parser.parse_args()
    if args.action == 'generate':
        generate(args.directory)
    else:
        check(args.directory, args.results or args.directory / 'results.bin')
