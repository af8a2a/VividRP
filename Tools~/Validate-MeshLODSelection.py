"""GPU LOD-cut regression: recentered/quantized parent bounds must never leave holes.

Invoked by Validate-MeshletCulling.ps1. Includes the actual Camera_1 Sponza roof
failure and random enclosing spheres under perspective, orthographic and affine
transforms. Inputs model the existing upward bfloat16 parent radius/error packing.
"""
import argparse
import json
import math
from pathlib import Path
import random
import struct

CASE = struct.Struct('<52f')
RESULT = struct.Struct('<4f4I')
IDENTITY = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]
PERSPECTIVE = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, .1, 0, 0, 1, 0]
MAX_FLOAT = struct.unpack('<f', bytes.fromhex('ffff7f7f'))[0]


def f32(x):
    return struct.unpack('<f', struct.pack('<f', x))[0]


def ceil_bfloat(x):
    bits = struct.unpack('<I', struct.pack('<f', x))[0]
    if bits & 65535:
        bits = (bits + 65536) & 0xffff0000
    return struct.unpack('<f', struct.pack('<I', bits))[0]


def multiply(a, b):
    return [sum(a[row * 4 + k] * b[k * 4 + col] for k in range(4))
            for row in range(4) for col in range(4)]


def generate(root):
    cases, controls = [], {}

    def add(parent, child, matrix=IDENTITY, vp=PERSPECTIVE, error=.001,
            threshold=100, expected=None, packed_radius=None):
        parent, child = tuple(map(f32, parent)), tuple(map(f32, child))
        radius = ceil_bfloat(parent[3] + math.dist(parent[:3], child[:3]))
        if packed_radius is not None:
            radius = packed_radius
        packed = (*child[:3], radius)
        assert radius >= parent[3] + math.dist(parent[:3], packed[:3]) - 1e-6
        c = CASE.unpack(CASE.pack(*parent, *child, *packed, *matrix, *vp,
                                  error, ceil_bfloat(error), threshold, 0, 1920, 1080, 0, 0))
        if expected is not None:
            controls[str(len(cases))] = expected
        cases.append(c)

    add((0, 0, 100, 1), (0, 0, 100, .5), expected=[1, 0])
    add((0, 0, 2, 1), (0, 0, 2, .5), expected=[0, 1])
    add((0, 0, 0, 1), (0, 0, 0, .5), expected=[0, 1])
    add((0, 0, -10, 1), (0, 0, -10, .5), expected=[0, 1])
    add((0, 0, 0, 1), (0, 0, 0, .5), vp=IDENTITY, expected=[0, 1])
    add((0, 0, 100, 1), (0, 0, 100, .5), vp=IDENTITY, expected=[0, 1])
    add((0, 0, 1, 0), (0, 0, 1, 0), expected=[1, 0])
    # Zero-error leaf at/behind the eye plane must not evaluate 0 * infinity.
    add((0, 0, 0, 1), (0, 0, 0, .5), error=0, expected=[1, 0])

    # Live Camera_1 data: both parent and leaf were rejected by R / centerW.
    vp = [.5398436, .020026466, .8107941, 4.8062987,
          1.0028207, -1.2606114, -.6365622, 10.610814,
          .00017946937, .00020567405, -.00012457455, .2938387,
          -.59813195, -.6854664, .4151796, 20.834242]
    matrix = [1, 0, 0, 0, 0, 1.4210855e-14, 1.0000001, -5.792494,
              0, -1.0000001, 1.4210855e-14, 6.90519e-7, 0, 0, 0, 1]
    parent = (6.1093726, -3.2111769, 11.884949, 31.464842)
    for child, radius in [((2.9145663, -5.4295745, 12.218401, 13.273245), 35.5),
                          ((-.00018310547, -12.504761, 11.884949, 10.941756), 42.75)]:
        add(parent, child, matrix, vp, .0036991127, 14865.57391672431,
            expected=[0, 1], packed_radius=radius)

    rng = random.Random(197243)
    for i in range(16384):
        scale = 2 ** rng.randint(-16, 16)
        radius = rng.uniform(.01, 3) * scale
        center = [rng.uniform(-10, 10) * scale for _ in range(3)]
        child = [center[j] + rng.uniform(-1, 1) * radius for j in range(3)]
        parent = (*center, radius)
        child = (*child, rng.uniform(0, 1) * radius)
        # Affine reflection, shear, nonuniform scale and translation.
        m = [rng.uniform(-2, 2) / scale if j % 4 != 3 else rng.uniform(-20, 20)
             for j in range(12)] + [0, 0, 0, 1]
        vp = PERSPECTIVE if i % 2 else IDENTITY
        error = 10 ** rng.uniform(-6, 1)
        threshold = 10 ** rng.uniform(-3, 10)
        add(parent, child, m, vp, error, threshold)

    while len(cases) % 64:
        cases.append(cases[0])
    root.mkdir(parents=True, exist_ok=True)
    (root / 'lod-cases.bin').write_bytes(b''.join(CASE.pack(*c) for c in cases))
    (root / 'lod-controls.json').write_text(json.dumps(controls), encoding='utf-8')
    print(f'Generated {len(cases)} LOD cases, including Camera_1 roof regressions')


def check(root):
    cases = list(CASE.iter_unpack((root / 'lod-cases.bin').read_bytes()))
    results = list(RESULT.iter_unpack((root / 'lod-results.bin').read_bytes()))
    controls = json.loads((root / 'lod-controls.json').read_text(encoding='utf-8'))
    assert len(results) == len(cases)
    refined = coarse = 0
    for index, (c, r) in enumerate(zip(cases, results)):
        def require(ok, reason):
            if not ok:
                raise AssertionError(f'LOD case {index}: {reason}; result={r}; input={c}')
        require(all(math.isfinite(x) and x >= 0 for x in r[:2]), 'invalid radius/sentinel')
        require(r[1] >= r[0], 'enclosing parent sphere decreased the metric')
        require(not any(math.isnan(x) for x in r[2:4]), 'NaN error')
        require(r[3] >= r[2], 'packed parent error decreased')
        require(r[4] or r[5], 'parent/child cut has a hole')
        require(r[6] == 1 and r[7] == 0, 'forced leaf LOD changed')
        if str(index) in controls:
            require(list(r[4:6]) == controls[str(index)], 'selection control')
        # Independent FP64 support of the local sphere in clip W. A positive
        # error sphere crossing the eye plane must request refinement.
        m = multiply(c[28:44], c[12:28])
        w = sum(m[12 + j] * c[j] for j in range(3)) + m[15]
        extent = c[3] * math.hypot(*m[12:15])
        if w <= extent:
            require(r[0] == MAX_FLOAT, 'eye-plane crossing not protected')
        refined += not r[4] and r[5]
        coarse += r[4] and not r[5]
    assert refined > 100 and coarse > 100, 'must exercise refinement and coarse LOD'
    print(f'PASS LOD: {len(cases)} cases, zero cut holes/containment failures; '
          f'refined={refined}, coarse={coarse}')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=('generate', 'check'))
    parser.add_argument('directory', type=Path)
    args = parser.parse_args()
    (generate if args.action == 'generate' else check)(args.directory)
