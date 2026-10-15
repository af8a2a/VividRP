"""Native meshoptimizer quality experiment; not the Unity C# builder or a GPU render.

Compare interpolated normal/UV error on a planar nonlinear attribute field at
the same triangle target. All sampling and inputs are deterministic.
"""
import ctypes as c
import importlib.util
import json
import math
import sys
import struct
from pathlib import Path

spec = importlib.util.spec_from_file_location("clusterlod_probe", Path(__file__).with_name("Validate-ClusterLOD.py"))
sys.dont_write_bytecode = True
abi = importlib.util.module_from_spec(spec)
spec.loader.exec_module(abi)


def quality(normal_weight=0.1, uv_multiplier=0.05):
    size = 20
    positions, indices = abi.grid(size)
    attributes = []
    for x, y, _ in positions:
        normal = (0.8 * math.sin(x * .7), .6 * math.cos(y * .6), 1.)
        length = math.sqrt(sum(v * v for v in normal))
        attributes.append(tuple(v / length for v in normal) +
                          (x / size + .15 * math.sin(y * .4), y / size + .15 * math.cos(x * .5)))
    area = 0.
    for i in range(0, len(indices), 3):
        a, b, d = [attributes[j][3:] for j in indices[i:i + 3]]
        area += abs((b[0] - a[0]) * (d[1] - a[1]) - (b[1] - a[1]) * (d[0] - a[0])) * .5
    uv_weight = min(1024., math.sqrt((len(indices) / 3) / area))
    vertices = (abi.F * (len(positions) * 3))(*(v for p in positions for v in p))
    src = (abi.U * len(indices))(*indices)

    def run(weights, uv_scale=1.):
        attrs = (abi.F * (len(attributes) * 5))(*(v * (uv_scale if k >= 3 else 1.) for a in attributes for k, v in enumerate(a)))
        dst = (abi.U * len(indices))()
        error = abi.F()
        count = abi.simplify(dst, src, len(src), vertices, len(positions), 12,
                             attrs, 20, (abi.F * 5)(*weights), 5, None,
                             len(indices) // 4 // 3 * 3, 1e30, 1, c.byref(error))
        return list(dst[:count]), error.value * abi.simplify_scale(vertices, len(positions), 12)

    def measure(result):
        total = [0., 0.]
        for point, reference in zip(positions, attributes):
            interpolated = None
            for i in range(0, len(result), 3):
                ids = result[i:i + 3]
                a, b, d = [positions[j] for j in ids]
                v0, v1, v2 = [(b[k] - a[k]) for k in range(2)], [(d[k] - a[k]) for k in range(2)], [(point[k] - a[k]) for k in range(2)]
                det = v0[0] * v1[1] - v0[1] * v1[0]
                if abs(det) < 1e-12:
                    continue
                u = (v2[0] * v1[1] - v2[1] * v1[0]) / det
                v = (v0[0] * v2[1] - v0[1] * v2[0]) / det
                if min(1. - u - v, u, v) >= -1e-9:
                    interpolated = [sum(w * attributes[j][k] for w, j in zip((1. - u - v, u, v), ids)) for k in range(5)]
                    break
            assert interpolated is not None, "Lost source surface coverage"
            total[0] += sum((interpolated[k] - reference[k]) ** 2 for k in range(3))
            total[1] += sum((interpolated[k] - reference[k]) ** 2 for k in range(3, 5))
        return dict(normal_rmse=math.sqrt(total[0] / len(positions)), uv_rmse=math.sqrt(total[1] / len(positions)))

    report = {}
    results = {}
    uv_weight *= uv_multiplier
    for name, weights in [("position_only", [0.] * 5), ("normal", [normal_weight] * 3 + [0., 0.]),
                          ("uv", [0., 0., 0., uv_weight, uv_weight]), ("both", [normal_weight] * 3 + [uv_weight, uv_weight])]:
        result, error = run(weights)
        results[name] = result
        report[name] = dict(triangles=len(result) // 3, combined_error_object_space=error, **measure(result))
    assert len({v['triangles'] for v in report.values()}) == 1, "Unequal triangle targets"
    assert report['normal']['normal_rmse'] < report['position_only']['normal_rmse']
    assert report['uv']['uv_rmse'] < report['position_only']['uv_rmse']
    assert report['both']['normal_rmse'] < report['position_only']['normal_rmse']
    assert report['both']['uv_rmse'] < report['position_only']['uv_rmse']
    assert run([normal_weight] * 3 + [uv_weight / 8, uv_weight / 8], 8)[0] == results['both']
    return dict(source_triangles=len(indices) // 3, samples=len(positions), uv_weight=uv_weight,
                uv_scale_invariant=True, variants=report)


def check_hierarchy_exports(root):
    """Check production builder/quantizer JSON exports on CPU, not rendered cuts."""
    def f32(value):
        return struct.unpack('<f', struct.pack('<f', value))[0]

    def bits_float(value):
        return struct.unpack('<f', struct.pack('<I', value))[0]

    result = []
    for path in sorted(root.glob('*-weighted.json')) + sorted(root.glob('*-tight.json')):
        data = json.loads(path.read_text())
        groups = [[f32(v) for v in group] for group in data['groups']]
        source = [[f32(v) for v in group] for group in data['source_groups']]
        for packed, expected in zip(data['packed'], groups):
            word = packed[3]
            scalars = data['overflow'][word & 0x7fffffff] if word & 0x80000000 else struct.unpack('<ee', struct.pack('<I', word))
            decoded = [bits_float(v) for v in packed[:3]] + [f32(v) for v in scalars]
            assert decoded == expected, 'GPU payload differs from CPU decoded groups'
        edges = {(n[0], n[1]) for n in data['nodes'] if n[1] != 0xffffffff}
        source_edges = {(n[0], n[1]) for n in data['source_nodes'] if n[1] != 0xffffffff}
        for table, links in [(groups, edges), (source, source_edges)]:
            for child, parent in links:
                a, b = table[child], table[parent]
                assert b[3] >= math.dist(a[:3], b[:3]) + a[3], 'Nonconservative parent radius'
                assert b[4] >= a[4], 'Nonmonotone parent error'
        for old, new in zip(data['source_nodes'], data['nodes']):
            a, b = source[old[0]], groups[new[0]]
            assert a[:3] == b[:3] and b[3] >= a[3] and b[4] >= a[4]
            assert (old[1] == 0xffffffff) == (new[1] == 0xffffffff)
            if old[1] != 0xffffffff:
                a, b = source[old[1]], groups[new[1]]
                assert a[:3] == b[:3] and b[3] >= a[3] and b[4] >= a[4]

        # Reconstruct the alternative max recurrence from the actual additive
        # graph. Subtracting stored FP32 errors includes outward-rounding bias;
        # this comparison is an estimate, not an independent geometric bound.
        children = {}
        for child, parent in source_edges:
            if child != parent:
                children.setdefault(parent, set()).add(child)
        memo = {}
        def alternative(index, visiting):
            if index in memo:
                return memo[index]
            assert index not in visiting, 'Cyclic source hierarchy'
            child_ids = children.get(index, set())
            local = max(0., source[index][4] - max((source[c][4] for c in child_ids), default=0.))
            value = max(local, max((alternative(c, visiting | {index}) for c in child_ids), default=0.))
            memo[index] = value
            return value
        roots = {n[0] for n in data['source_nodes'] if n[1] == 0xffffffff}
        result.append(dict(asset=path.stem, nodes=len(data['nodes']), groups=len(groups),
                           checked_edges=len(edges), overflow=len(data['overflow']),
                           max_root_additive=max(source[i][4] for i in roots),
                           estimated_max_root_max_recurrence=max(alternative(i, set()) for i in roots)))
    assert result, 'No production hierarchy exports found'
    return result


if __name__ == '__main__':
    import argparse
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--normal-weight', type=float, default=0.1)
    parser.add_argument('--uv-weight', type=float, default=0.05)
    parser.add_argument('--hierarchy-exports', type=Path, help='Optional production builder/quantizer JSON directory')
    args = parser.parse_args()
    report = quality(args.normal_weight, args.uv_weight)
    if args.hierarchy_exports:
        report['hierarchies'] = check_hierarchy_exports(args.hierarchy_exports)
    print(json.dumps(report, indent=2))
