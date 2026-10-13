"""Exercise the shipped meshoptimizer ABI without launching Unity.

This is a native integration probe, not execution of the C# builder or Unity tests.
Run from any directory with Python 3; only the standard library is required.
"""
import ctypes as c
import json
import math
from pathlib import Path


U, F, B, S = c.c_uint, c.c_float, c.c_ubyte, c.c_size_t
PU, PF, PB = c.POINTER(U), c.POINTER(F), c.POINTER(B)
ROOT = Path(__file__).resolve().parents[1]
DLL = ROOT / "Runtime/SubSystem/Plugin/MeshOptimizer/Plugins/x86_64/meshoptimizer.dll"
lib = c.CDLL(str(DLL))


class Meshlet(c.Structure):
    _fields_ = [(name, U) for name in ("vo", "to", "vc", "tc")]


def bind(name, result, args):
    fn = getattr(lib, "meshopt_" + name)
    fn.restype, fn.argtypes = result, args
    return fn


bound = bind("buildMeshletsBound", S, [S, S, S])
build = bind("buildMeshlets", S, [c.POINTER(Meshlet), PU, PB, PU, S, PF, S, S, S, S, F])
remap_positions = bind("generatePositionRemap", None, [PU, PF, S, S])
partition = bind("partitionClusters", S, [PU, PU, S, PU, S, PF, S, S, S])
simplify = bind("simplifyWithAttributes", S, [PU, PU, S, PF, S, S, PF, S, PF, S, PB, S, F, U, PF])
sloppy = bind("simplifySloppy", S, [PU, PU, S, PF, S, S, PB, S, F, PF])
simplify_scale = bind("simplifyScale", F, [PF, S, S])


def grid(size, curved=False, seams=False):
    positions = [(float(x), float(y), math.sin(x * 0.7) * math.cos(y * 0.6) if curved else 0.0)
                 for y in range(size + 1) for x in range(size + 1)]
    indices = []
    for y in range(size):
        for x in range(size):
            a = y * (size + 1) + x
            indices.extend([a, a + size + 1, a + 1, a + 1, a + size + 1, a + size + 2])
    if seams:
        # Split each quad's vertex IDs: connectivity must use the position-only remap.
        positions = [positions[i] for i in indices]
        indices = list(range(len(indices)))
    return positions, indices


def clusterize(positions, indices):
    verts = (F * (len(positions) * 3))(*(v for p in positions for v in p))
    src = (U * len(indices))(*indices)
    meshlets = (Meshlet * bound(len(src), 128, 128))()
    mv, mt = (U * len(src))(), (B * len(src))()
    count = build(meshlets, mv, mt, src, len(src), verts, len(positions), 12, 128, 128, 0.25)
    clusters = [[mv[m.vo + mt[m.to + i]] for i in range(m.tc * 3)] for m in meshlets[:count]]
    assert sorted(v for cluster in clusters for v in cluster) == sorted(indices)
    return verts, clusters


def groups_and_locks(positions, indices, clusters=None):
    if clusters is None:
        verts, clusters = clusterize(positions, indices)
    else:
        verts = (F * (len(positions) * 3))(*(v for p in positions for v in p))
    remap = (U * len(positions))()
    remap_positions(remap, verts, len(positions), 12)
    flat = [remap[v] for cluster in clusters for v in cluster]
    counts = (U * len(clusters))(*(len(cluster) for cluster in clusters))
    parts = (U * len(clusters))()
    count = partition(parts, (U * len(flat))(*flat), len(flat), counts, len(clusters), verts, len(positions), 12, 4)
    assert set(parts) == set(range(count))
    owners = [set() for _ in positions]
    for i, cluster in enumerate(clusters):
        for v in cluster:
            owners[remap[v]].add(parts[i])
    locks = [int(len(owners[remap[v]]) > 1) for v in range(len(positions))]
    assert all(locks[i] == locks[remap[i]] for i in range(len(positions)))
    groups = [[v for i, cluster in enumerate(clusters) if parts[i] == g for v in cluster] for g in range(count)]
    return clusters, groups, locks


def simplify_group(positions, indices, locks, mode="normal", scale=1.0, target_error=0.1):
    # Compact by original ID, as the production wrapper does, retaining seam vertices.
    unique = list(dict.fromkeys(indices))
    remap = {v: i for i, v in enumerate(unique)}
    src = (U * len(indices))(*(remap[i] for i in indices))
    verts = (F * (len(unique) * 3))(*(x * scale for i in unique for x in positions[i]))
    locked = (B * len(unique))(*(locks[i] for i in unique))
    dst, error = (U * len(src))(), F()
    target = len(src) // 3 // 2 * 3
    if mode == "normal":
        count = simplify(dst, src, len(src), verts, len(unique), 12, None, 0, None, 0,
                         locked, target, target_error, 1, c.byref(error))
    else:
        count = sloppy(dst, src, len(src), verts, len(unique), 12, locked, target, target_error, c.byref(error))
    absolute_error = error.value * simplify_scale(verts, len(unique), 12)
    result = [unique[i] for i in dst[:count]] if count else indices[:]
    if not count:
        absolute_error = 0.0
    assert result and len(result) % 3 == 0
    assert all(i in remap for i in result)
    return result, absolute_error


def main():
    report = {}
    for seams in (False, True):
        positions, indices = grid(32, seams=seams)
        clusters, groups, locks = groups_and_locks(positions, indices)
        assert len(groups) > 1 and sum(locks) > 0
        assert groups_and_locks(positions, indices) == (clusters, groups, locks)
        for mode in ("normal", "sloppy"):
            for group in groups:
                result, error = simplify_group(positions, group, locks, mode)
                # Boundary positions survive; sloppy simplification may merge coincident seam IDs.
                assert {positions[i] for i in group if locks[i]} <= {positions[i] for i in result}
                assert math.isfinite(error) and error >= 0
        report["seams" if seams else "grid"] = dict(clusters=len(clusters), groups=len(groups), locked=sum(locks))

    positions, indices = grid(20, curved=True)
    _, clusters = clusterize(positions, indices)
    merged = [v for cluster in clusters for v in cluster]
    normal, err1 = simplify_group(positions, merged, [0] * len(positions))
    scaled, err8 = simplify_group(positions, merged, [0] * len(positions), scale=8)
    assert len(normal) < len(indices) * 0.8 and normal == scaled and err1 > 0
    assert math.isclose(err8, err1 * 8, rel_tol=1e-5)
    for mode in ("normal", "sloppy"):
        result, error = simplify_group(positions, merged, [1] * len(positions), mode)
        assert len(result) == len(indices) and error >= 0
    report["curved"] = dict(source_triangles=len(indices) // 3, simplified_triangles=len(normal) // 3,
                            error=err1, error_at_scale_8=err8)

    # Full native hierarchy progression with the same group target and acceptance threshold.
    positions, pending = grid(64)
    levels = [len(pending) // 3]
    _, clusters = clusterize(positions, pending)
    while True:
        clusters, groups, locks = groups_and_locks(positions, pending, clusters)
        if len(clusters) <= 1:
            break
        simplified_groups = [simplify_group(positions, group, locks, target_error=0.01)[0] for group in groups]
        coarse = [v for group in simplified_groups for v in group]
        if len(coarse) >= len(pending) * 0.8:
            break
        levels.append(len(coarse) // 3)
        pending = coarse
        clusters = [cluster for group in simplified_groups for cluster in clusterize(positions, group)[1]]
    assert len(levels) > 1
    report["hierarchy_triangle_counts"] = levels
    triangle = [(0., 0., 0.), (1., 0., 0.), (0., 1., 0.)]
    restored, error = simplify_group(triangle, [0, 1, 2], [0, 0, 0], "sloppy", target_error=1.)
    assert restored == [0, 1, 2] and error == 0
    report["empty_result_preserves_source"] = True
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
