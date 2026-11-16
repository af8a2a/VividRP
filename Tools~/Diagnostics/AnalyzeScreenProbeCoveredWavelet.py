"""Locate wavelet-only changes between qualified native candidate experiments."""
import argparse
import json
from pathlib import Path

from AnalyzeScreenProbeQuadratureHistory import qualify_replay


def analyze(before,after):
    old,new=(qualify_replay(root) for root in (before,after))
    assert not old[1].get('coverageWeightedWavelet',False) and new[1]['coverageWeightedWavelet']
    for key in ('sequence','warmup','measuredFrames','spacing','rays','regions'):
        assert old[1][key]==new[1][key]
    stages={}
    def check(stage,pairs):
        pairs=list(pairs);assert pairs
        different=[list(key) for key,a,b in pairs if a['sha256']!=b['sha256']]
        stages[stage]=dict(pairs=len(pairs),byteExact=not different,differences=different)
    # All non-final source readbacks include geometry, transport, anchors, SH,
    # quadrature witness, Resolve and the actual native temporal result.
    assert old[2].keys()==new[2].keys()
    check('sourceBeforeWavelets',((key,old[2][key],new[2][key]) for key in old[2] if not key[1].startswith('final')))
    for side in ('baseline','candidate'):
        for role in ('temporal','history','moments','geometry','final'):
            check(side+'-'+role,((key,old[3][key],new[3][key]) for key in old[3] if key[1].startswith(role+'-') and key[2]==side))
    resolve=[]
    for capture in (old[0],new[0]):
        resolve.append({(r['frame'],r['side']):r for r in capture['records'] if r.get('componentAa')})
    assert resolve[0].keys()==resolve[1].keys()
    check('fullResolve',((key,resolve[0][key],resolve[1][key]) for key in resolve[0]))
    attributable=all(stages[key]['byteExact'] for key in ('sourceBeforeWavelets','fullResolve',
        'candidate-temporal','candidate-history','candidate-moments','candidate-geometry'))
    return dict(schema='screen-probe-covered-wavelet-attribution.v1',qualified=True,
        preWaveletInputsByteExact=attributable,stages=stages,
        limits=['Manifest comparisons are preceded by full source/payload and native replay qualification.',
            'Identical pre-wavelet values isolate the color-average change; this does not prove physical accuracy, moving quality or GPU performance.'])


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('before',type=Path);p.add_argument('after',type=Path);p.add_argument('--output',type=Path,required=True)
    a=p.parse_args();assert not a.output.exists()
    report=analyze(a.before.resolve(),a.after.resolve());a.output.write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps(dict(preWaveletInputsByteExact=report['preWaveletInputsByteExact'],
        stages={k:dict(pairs=v['pairs'],byteExact=v['byteExact'],differences=len(v['differences'])) for k,v in report['stages'].items()}),indent=2))
