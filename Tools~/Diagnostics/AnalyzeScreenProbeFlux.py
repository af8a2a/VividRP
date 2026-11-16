"""Check GPU wavelet consumed-energy conservation within explicit rounding bounds."""
import argparse
import json
from pathlib import Path

import numpy as np

from AnalyzeScreenProbeQuadratureHistory import qualify_replay


def qualify_mass(capture,protocol):
    assert protocol['conservativeFluxWavelet'] and not protocol['performanceQualified']
    assert protocol['gates']['fluxMassRoundingBound'] and protocol['gates']['fluxCoverageByteExact']
    tolerance=protocol['gates']['fluxFp32RelativeTolerance']
    assert tolerance==2e-6,'Flux arithmetic bound changed'
    rows=capture['fluxMass'];frames=protocol['fluxMassFrames']
    assert frames==[protocol['warmup'],protocol['warmup']+protocol['measuredFrames']-1]
    indexed={(q['frame'],q['iteration'],q['side']):q for q in rows}
    assert len(rows)==len(indexed)==len(frames)*4*2,'Incomplete or duplicate GPU flux mass readbacks'
    pairs=[]
    for frame in frames:
        previous=None
        for iteration in range(4):
            a,b=(indexed[frame,iteration,side] for side in ('input','output'))
            for q in (a,b):
                assert (q['format'],q['width'],q['height'])==('R16G16B16A16_SFloat',1920,1080)
                assert q['nonFinite']==q['negative']==q['saturated']==0,'Flux conservation excludes HDR saturation or invalid inputs'
                assert np.isfinite(q['sum']).all() and np.isfinite(q['halfUlp']).all()
            assert a['alphaSha256']==b['alphaSha256'],'Flux changed coverage'
            if previous is not None:
                assert previous['sum']==a['sum'] and previous['alphaSha256']==a['alphaSha256'],'Wavelet stages do not form one consumed chain'
            before,after=(np.array(q['sum']) for q in (a,b))
            bound=np.array(b['halfUlp'])+tolerance*np.abs(before)
            error=np.abs(after-before)
            pairs.append(dict(frame=frame,step=1<<iteration,before=before.tolist(),after=after.tolist(),
                deltaPercent=(100*(after-before)/np.maximum(abs(before),1e-12)).tolist(),
                absoluteError=error.tolist(),roundingAndArithmeticBound=bound.tolist(),
                errorOverBound=(error/np.maximum(bound,1e-20)).tolist(),passed=bool((error<=bound).all())))
            previous=b
    return dict(schema='screen-probe-flux-mass.v1',readbacks=len(rows),pairs=pairs,
        coverageByteExact=True,allPassed=all(p['passed'] for p in pairs),
        worstErrorOverBound=max(max(p['errorOverBound']) for p in pairs),
        worstAbsoluteDeltaPercent=max(max(abs(v) for v in p['deltaPercent']) for p in pairs),
        limits=['Discrete full-screen RGB*coverage mass, not a surface-area radiometric integral.',
            'Rounding bound is the sum of output half ULPs plus2e-6 input mass for FP32 arithmetic; not an assertion of zero rounding bias.',
            'Native hook overwrites the original dispatch. Quality and consumed-chain checks only; no attributable GPU cost.'])


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('capture',type=Path);p.add_argument('--output',type=Path,required=True)
    a=p.parse_args();assert not a.output.exists()
    capture,protocol,*_=qualify_replay(a.capture.resolve())
    report=qualify_mass(capture,protocol);a.output.write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps({k:v for k,v in report.items() if k not in ('pairs','limits')},indent=2))
