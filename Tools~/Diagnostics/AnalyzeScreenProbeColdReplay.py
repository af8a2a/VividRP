"""Compare complete cold/prefix captures without relaxing bitwise A/A gates."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np


def load(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def qualify(root):
    c, p = load(root/'capture.json'), load(root/'protocol.json')
    if p['schema']=='screen-probe-card-quality.v1':
        from AnalyzeScreenProbeCardQuality import qualify as qualify_quality
        qualify_quality(root)
        # Normalize only the protocol fields used below; the full original
        # protocol remains in the comparison and still must match exactly.
        p=dict(p,frames=p['warmup']+p['measuredFrames'],prefixOnly=False,
            sourceOrders=8,sourceSamples=64,sourceBatches=32,flags=0)
    assert c['complete'] and not c['errors'] and c['epochReset']
    assert c['privateInitialized']
    assert c.get('coldOtherCameras', 0) == 0
    assert load(root/'source-verification.json')['unchanged']
    assert (c['spacing'], c['rays']) == (16, 8)
    assert (p['spacing'], p['rays'], p['sourceOrders'], p['sourceSamples'], p['sourceBatches'], p['flags']) == (16, 8, 8, 64, 32, 0)
    if p['prefixOnly']:
        assert c['renderedFrames'] == p['frames'] and c['measuredFrames'] == 0
        checkpoints = 3 + int(p['frames'] == 1024)
    else:
        assert c['warmup'] + c['measuredFrames'] == p['frames']
        checkpoints = c['measuredFrames'] + (4 if c['warmup'] else 0)
    assert c['componentAaPairs'] == checkpoints and c['privateAaPairs'] == checkpoints*25
    for name in ('experimentalTracePrograms', 'componentPrograms', 'quadraturePrograms', 'privatePrograms', 'oraclePrograms'):
        assert c[name], name
    for kind in ('inputs', 'scripts'):
        for row in load(root/'source-before-capture.json')[kind]:
            assert digest(Path(row['path'])) == row['sha256']
    rows = {}
    total = 0
    for row in c['records']:
        if 'sha256' not in row or not ('tag' in row or row.get('componentAa')):
            continue
        key = row.get('frame'), row.get('tag', 'component'), row.get('side')
        assert key not in rows, key
        rows[key] = row
        if row.get('path'):
            path = Path(row['path']).resolve()
            assert path.is_relative_to(root.resolve())
            assert digest(path) == row['sha256']
            total += path.stat().st_size
    # Aggregate rows are accounted separately from per-frame signal records.
    for row in c['records']:
        if 'aggregate' in row or row.get('gpuFingerprints'):
            path = Path(row['path']).resolve()
            assert path.is_relative_to(root.resolve()) and digest(path) == row['sha256']
            total += path.stat().st_size
            if row.get('gpuFingerprints'):
                assert path.stat().st_size == row['frames']*len(row['names'])*row['stripes']*16
                assert row['frames'] == p['frames'] and row['stripes'] == 16
    assert total == c['payloadBytes'] <= c['payloadBudget']
    assert len([r for r in rows.values() if r.get('componentAa')]) == checkpoints*2
    for key, row in rows.items():
        if key[1] == 'component' and key[2] == 'native':
            assert row['sha256'] == rows[key[:2]+('copied',)]['sha256']
        if key[2] == 'native' and key[1] != 'component':
            other = rows.get(key[:2]+('candidate',))
            if other:
                assert row['sha256'] == other['sha256']
    return c, p, rows


def fingerprint_difference(first, second):
    ledgers = [[r for r in c['records'] if r.get('gpuFingerprints')] for c in (first, second)]
    assert len(ledgers[0]) == len(ledgers[1]) <= 1
    if not ledgers[0]:
        return None
    a, b = ledgers[0][0], ledgers[1][0]
    assert all(a[k] == b[k] for k in ('frames', 'stripes', 'names'))
    shape = (a['frames'], len(a['names']), a['stripes'], 4)
    left, right = (np.fromfile(row['path'], '<u4').reshape(shape) for row in (a, b))
    changed = np.any(left != right, axis=(2, 3))
    differences = [[int(f), a['names'][slot]] for f, slot in zip(*np.nonzero(changed))]
    return dict(exact=not differences, checkedFrameSignals=a['frames']*len(a['names']),
        differences=differences, firstObservedFrame=min((f for f, _ in differences), default=None),
        limitation='Integer fingerprints localize differences; equality does not prove full-byte equality.')


def mean_map_difference(first, second, roots):
    """Same-code disagreement, deliberately distinct from physical energy error."""
    if not first['measuredFrames']:
        return None
    targets = [load(root/'targets.json') for root in roots]
    assert targets[0] == targets[1]
    images = []
    for capture in (first, second):
        rows = sorted((r for r in capture['records'] if r.get('tag') == 'oracle-final'
                       and r.get('side') == 'candidate'), key=lambda r: r['frame'])
        assert len(rows) == capture['measuredFrames']
        frames = []
        for row in rows:
            image = np.fromfile(row['path'], '<f2').astype(np.float64).reshape(-1, 4)
            assert len(image) == len(targets[0]) and np.isfinite(image).all()
            frames.append(image[:, :3]*image[:, 3:4])
        images.append(np.mean(frames, axis=0))
    left, right = (rgb @ np.array([.2126, .7152, .0722]) for rgb in images)
    regions = []
    for name in dict.fromkeys(t['region'] for t in targets[0]):
        mask = np.array([t['region'] == name for t in targets[0]])
        a, b = left[mask], right[mask]
        scale = float(np.mean((a+b)*.5))
        assert scale > 0
        regions.append(dict(region=name, points=int(mask.sum()), firstMean=float(a.mean()), secondMean=float(b.mean()),
            energyDeltaPercent=float((b-a).mean()/scale*100),
            meanMapRmsPercent=float(np.sqrt(np.mean((b-a)**2))/scale*100),
            meanMapMaxPercent=float(np.max(np.abs(b-a))/scale*100)))
    return dict(frames=first['measuredFrames'], regions=regions, acceptanceEligible=False,
        limitation='A/A disagreement in the finite final mean map; not error against a physical reference or converged bias.')


def compare(roots):
    cases = [qualify(root) for root in roots]
    base, protocol, rows = cases[0]
    comparisons = []
    for root, (capture, other_protocol, other) in zip(roots[1:], cases[1:]):
        assert protocol == other_protocol, 'Only same-protocol A/A is eligible'
        assert rows.keys() == other.keys(), 'Unequal record coverage'
        program_equal = all(base.get(k) == capture.get(k) for k in
            ('experimentalTracePrograms', 'componentPrograms', 'quadraturePrograms', 'privatePrograms', 'oraclePrograms'))
        differences = [list(k) for k in rows if rows[k]['sha256'] != other[k]['sha256']]
        constants = [[r for r in c['records'] if r.get('coldConstants')] for c in (base, capture)]
        ledger_constants = [[r for r in c['records'] if r.get('fingerprintConstants')] for c in (base, capture)]
        assert len(ledger_constants[0]) == len(ledger_constants[1])
        if ledger_constants[0]:
            assert [r['frame'] for r in ledger_constants[0]] == list(range(protocol['frames']))
            assert [r['frame'] for r in ledger_constants[1]] == list(range(protocol['frames']))
        fingerprint = fingerprint_difference(base, capture)
        mean_map = mean_map_difference(base, capture, (roots[0], root))
        witness = None
        if protocol.get('witness'):
            frame = protocol['probeFrame']
            inputs = []
            for table in (rows, other):
                geometry = np.fromfile(table[frame, 'cold-m_Geometry', 'source']['path'], '<f4').reshape(136, 120, 4)
                assert np.all(geometry[68, :12, 3] < 0), 'Witness storage must be invalid geometry'
                anchor = np.fromfile(table[frame, 'cold-m_PreparedAnchor', 'source']['path'], '<u4').reshape(136, 120, 4)
                inputs.append(anchor[68, :12].copy())
            witness = dict(words=48, exact=bool(np.array_equal(*inputs)), first=inputs[0].tolist(), second=inputs[1].tolist())
        comparisons.append(dict(other=str(root), checkedPairs=len(rows), differences=differences,
            firstObservedFrame=min((k[0] for k in differences), default=None),
            compiledProgramsExact=program_equal, cpuConstantsExact=constants[0] == constants[1], gpuConstants=witness,
            fingerprintConstantsExact=ledger_constants[0] == ledger_constants[1],
            gpuFingerprints=fingerprint, finalMeanMap=mean_map,
            exact=not differences and program_equal and constants[0] == constants[1] and ledger_constants[0] == ledger_constants[1]
                and (fingerprint is None or fingerprint['exact'])))
    return dict(schema='screen-probe-cold-aa.v1', baseline=str(roots[0]), comparisons=comparisons,
        exact=all(c['exact'] for c in comparisons), qualityAccepted=False,
        limitation='Same-code reproducibility only; no final mean-map or energy acceptance is inferred.')


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('captures', nargs='+', type=Path)
    p.add_argument('--output', type=Path, required=True)
    a = p.parse_args()
    assert len(a.captures) >= 2 and not a.output.exists()
    result = compare([path.resolve() for path in a.captures])
    a.output.write_text(json.dumps(result, indent=2)+'\n')
    print(json.dumps(dict(exact=result['exact'], pairs=[c['checkedPairs'] for c in result['comparisons']],
        differences=[len(c['differences']) for c in result['comparisons']],
        firstFrames=[c['firstObservedFrame'] for c in result['comparisons']]), indent=2))
