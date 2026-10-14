"""D3D12 LOD cut/pixel-budget regression, invoked by Validate-MeshletCulling.ps1.

Tests exact shared-group transitions, three-level coverage, equality +/- 1 ULP,
Camera_1 roof bounds, affine transforms, orthographic/perspective, FOV, viewport,
instance bias, and zero error/budget. Independent FP64 projected-error oracle.
"""
import argparse
import json
import math
from pathlib import Path
import random
import struct

CASE = struct.Struct('<52f')
RESULT = struct.Struct('<4f12I')
IDENTITY = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]
PERSPECTIVE = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, .1, 0, 0, 1, 0]
MAX_FLOAT = struct.unpack('<f', bytes.fromhex('ffff7f7f'))[0]


def f32(x):
    return struct.unpack('<f', struct.pack('<f', x))[0]


def multiply(a, b):
    return [sum(a[row * 4 + k] * b[k * 4 + col] for k in range(4))
            for row in range(4) for col in range(4)]


def projected_error(sphere, error, m, screen):
    if error == 0:
        return 0
    center = [sum(m[r * 4 + j] * sphere[j] for j in range(3)) + m[r * 4 + 3] for r in range(4)]
    ws = math.hypot(*m[12:15])
    extent = sphere[3] * ws
    margin = 8e-6 * (sum(abs(m[12 + j] * sphere[j]) for j in range(3)) + abs(m[15]) + extent) + 1e-30
    nearest = center[3] - extent - margin
    if nearest <= 0:
        return MAX_FLOAT
    xy = [math.hypot(*m[r * 4:r * 4 + 3]) for r in range(2)]
    return min(MAX_FLOAT, error * max((xy[r] + (abs(center[r]) + sphere[3] * xy[r]) / nearest * ws)
                                    * .5 * screen[r] / nearest for r in range(2)))


def generate(root):
    cases, controls = [], {}

    def add(parent, child, matrix=IDENTITY, vp=PERSPECTIVE, error=.001,
            budget=1, expected=None, grand=None, screen=(1920, 1080), bias=1, boundary=0):
        parent, child = tuple(map(f32, parent)), tuple(map(f32, child))
        grand = grand or (*parent[:3], parent[3] * 2 + 1e-4)
        c = CASE.unpack(CASE.pack(*parent, *child, *grand, *matrix, *vp,
                                  error, error * 3, budget, error * .25, *screen, bias, boundary))
        if expected is not None:
            controls[str(len(cases))] = expected
        cases.append(c)

    add((0, 0, 100, 1), (0, 0, 100, .5), expected=[1, 0])
    add((0, 0, 2, 1), (0, 0, 2, .5), expected=[0, 1])
    add((0, 0, 0, 1), (0, 0, 0, .5), expected=[0, 1])
    add((0, 0, -10, 1), (0, 0, -10, .5), expected=[0, 1])
    add((0, 0, 0, 1), (0, 0, 0, .5), vp=IDENTITY, expected=[1, 0])
    add((0, 0, 100, 1), (0, 0, 100, .5), vp=IDENTITY, expected=[1, 0])
    add((0, 0, 1, 0), (0, 0, 1, 0), expected=[1, 0])
    add((0, 0, 0, 1), (0, 0, 0, .5), error=0, budget=0, expected=[1, 0])
    add((0, 0, 100, 1), (0, 0, 100, .5), budget=0, expected=[0, 1])
    add((0, 0, 100, 1), (0, 0, 100, .5), vp=IDENTITY, screen=(3840, 2160), expected=[0, 1])
    add((0, 0, 100, 1), (0, 0, 100, .5), vp=IDENTITY, screen=(3840, 2160), bias=2, expected=[1, 0])
    for boundary, expected in [(1, [1, 0]), (2, [0, 1]), (3, [1, 0])]:
        add((2, -1, 100, 1), (2, -1, 100, .5), boundary=boundary, expected=expected)

    # Live Camera_1 Sponza roof. Full-precision group bounds are shared by both sides.
    vp = [.5398436, .020026466, .8107941, 4.8062987,
          1.0028207, -1.2606114, -.6365622, 10.610814,
          .00017946937, .00020567405, -.00012457455, .2938387,
          -.59813195, -.6854664, .4151796, 20.834242]
    matrix = [1, 0, 0, 0, 0, 1.4210855e-14, 1.0000001, -5.792494,
              0, -1.0000001, 1.4210855e-14, 6.90519e-7, 0, 0, 0, 1]
    parent = (6.1093726, -3.2111769, 11.884949, 31.464842)
    for child in [(2.9145663, -5.4295745, 12.218401, 13.273245),
                  (-.00018310547, -12.504761, 11.884949, 10.941756)]:
        add(parent, child, matrix, vp, .0036991127, expected=[0, 1])

    rng = random.Random(197243)
    for i in range(16384):
        scale = 2 ** rng.randint(-16, 16)
        radius = rng.uniform(.01, 3) * scale
        center = [rng.uniform(-10, 10) * scale for _ in range(3)]
        child = [center[j] + rng.uniform(-.25, .25) * radius for j in range(3)]
        parent = (*center, radius)
        child = (*child, rng.uniform(0, .4) * radius)
        # Affine reflection, shear, nonuniform scale and translation.
        m = [rng.uniform(-2, 2) / scale if j % 4 != 3 else rng.uniform(-20, 20)
             for j in range(12)] + [0, 0, 0, 1]
        vp = list(PERSPECTIVE if i % 2 else IDENTITY)
        vp[0], vp[5] = 2 ** rng.uniform(-2, 2), 2 ** rng.uniform(-2, 2)
        error = scale * 10 ** rng.uniform(-6, 0)
        budget = 10 ** rng.uniform(-3, 4)
        add(parent, child, m, vp, error, budget, screen=(rng.randint(128, 4096), rng.randint(128, 2160)),
            bias=2 ** rng.uniform(-2, 2))

    while len(cases) % 64:
        cases.append(cases[0])
    root.mkdir(parents=True, exist_ok=True)
    (root / 'lod-cases.bin').write_bytes(b''.join(CASE.pack(*c) for c in cases))
    (root / 'lod-controls.json').write_text(json.dumps(controls), encoding='utf-8')
    print(f'Generated {len(cases)} shared-group LOD cases, including Camera_1 roof')


def check(root):
    cases = list(CASE.iter_unpack((root / 'lod-cases.bin').read_bytes()))
    results = list(RESULT.iter_unpack((root / 'lod-results.bin').read_bytes()))
    controls = json.loads((root / 'lod-controls.json').read_text(encoding='utf-8'))
    assert len(results) == len(cases)
    refined = coarse = range_rejected = 0
    for index, (c, r) in enumerate(zip(cases, results)):
        def require(ok, reason):
            if not ok:
                raise AssertionError(f'LOD case {index}: {reason}; result={r}; input={c}')
        require(all(math.isfinite(x) and x >= 0 for x in r[:4]), 'non-finite pixel error')
        require(r[1] <= r[0] <= r[2], 'non-monotone nested group error')
        require(sum(r[4:6]) == 1, 'main-view cut hole or overlap')
        require(sum(r[8:10]) == 1, 'shadow cut hole or overlap')
        require(sum(r[12:15]) == 1, 'three-level cut hole or overlap')
        require(not (r[15] & 2) or (r[15] & 1), 'VSM hierarchy rejected selected leaf')
        require(r[15] & 4, 'VSM hierarchy rejected truncated finest level')
        range_rejected += not (r[15] & 1)
        require(r[6:8] == (1, 0) and r[10:12] == (1, 0), 'forced leaf changed')
        if str(index) in controls:
            require(list(r[4:6]) == controls[str(index)], 'selection control')
        m = multiply(c[28:44], c[12:28])
        expected = projected_error(c[:4], c[44], m, c[48:50])
        require(math.isclose(r[0], expected, rel_tol=0.003, abs_tol=1e-10), 'FP64 pixel error oracle')
        if c[51] == 0:
            budget = c[46] * c[50]
            if not math.isclose(expected, budget, rel_tol=.003):
                require(bool(r[4]) == (expected <= budget), 'FP64 selection oracle')
        shadow_expected = c[44] * r[3] <= c[46] * c[50]
        require(bool(r[8]) == shadow_expected, 'linear shadow texel oracle')
        refined += r[5]
        coarse += r[4]
    assert refined > 100 and coarse > 100 and range_rejected > 100
    print(f'PASS LOD: {len(cases)} cases, zero holes/overlap in main, shadow and 3-level cuts; '
          f'pixel oracle/equality/forced-depth/VSM hierarchy passed; refined={refined}, coarse={coarse}')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=('generate', 'check'))
    parser.add_argument('directory', type=Path)
    args = parser.parse_args()
    (generate if args.action == 'generate' else check)(args.directory)
