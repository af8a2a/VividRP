"""Report declared same-input wavelet contrasts against the fixed physical oracle."""
import argparse
import hashlib
import json
from pathlib import Path

from AnalyzeCornellPixelOracle import analyze as oracle
from AnalyzeScreenProbeQuadratureHistory import analyze as noise,qualify_replay
from AnalyzeScreenProbeFlux import qualify_mass
from ScreenProbeWaveletFactorial import qualify_inputs


CONTRASTS={
    'energy-row-vs-original':('baseline','energy-row-native'),
    'energy-row-rounding':('energy-row-native','energy-row-rne'),
    'symmetric-row-rounding':('symmetric-row-rtz','symmetric-row-rne'),
    'flux-rounding':('flux-rtz','candidate'),
    'normalization-rne':('symmetric-row-rne','candidate'),
    'normalization-rtz':('symmetric-row-rtz','flux-rtz'),
    'bilateral-formula-rne':('energy-row-rne','symmetric-row-rne'),
}


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('capture',type=Path);p.add_argument('reference',type=Path)
    p.add_argument('--output',type=Path,required=True);a=p.parse_args()
    a.output.mkdir(parents=True,exist_ok=False)
    names=('AnalyzeScreenProbeWaveletFactorial.py','AnalyzeCornellPixelOracle.py','AnalyzeScreenProbeQuadratureHistory.py',
           'ScreenProbeWaveletFactorial.py','AnalyzeScreenProbeFlux.py','TemporalSamplingExperiment.py')
    scripts={str(Path(__file__).with_name(name).resolve()):hashlib.sha256(Path(__file__).with_name(name).read_bytes()).hexdigest() for name in names}
    capture,protocol,_,rows,*_=qualify_replay(a.capture.resolve())
    inputs=qualify_inputs(capture,protocol,rows);mass=qualify_mass(capture,protocol)
    assert mass['allPassed']
    reports={}
    for name,(baseline,candidate) in CONTRASTS.items():
        physical=oracle(a.capture.resolve(),a.reference.resolve(),baseline,candidate)
        temporal=noise(a.capture.resolve(),baseline,candidate)
        report=dict(control=baseline,candidate=candidate,sameWaveletInputs=name!='energy-row-vs-original',physical=physical,temporal=temporal)
        (a.output/(name+'.json')).write_text(json.dumps(report,indent=2)+'\n')
        reports[name]={region:dict(meanChangePercent=q['comparison']['mean_change_percent'],
            temporalVarianceReductionPercent=q['comparison']['variance_reduction_percent'],
            physicalMeanMapMseDeltaPercent=physical['regions'][region]['meanMapMse']['deltaPercentOfBaseline'],
            physicalMeanMapMseThreeSigmaUpperPercent=physical['regions'][region]['meanMapMse']['deltaPlusThreeSigmaPercentOfBaseline'])
            for region,q in temporal['regions'].items()}
        print(json.dumps(dict(contrast=name,regions=reports[name])),flush=True)
    summary=dict(schema='screen-probe-wavelet-factorial.v1',qualified=True,inputs=inputs,discreteMass=mass,
        contrasts=reports,analysisScripts=scripts,productionQualified=False,limits=[
            'Only energy-row-vs-original compares different temporal inputs; other contrasts share full input and moment hashes.',
            'Matched symmetric row/fixed variants share the bilateral formula and first-scale guides. Later-scale guides respond to each branch output; they are not frozen.',
            'Native output remains flux-RNE, all other wavelets are read-only counterfactuals. No motion, dynamic lighting or GPU timing acceptance.',
            'One renderer sequence; factorial diagnosis precedes an independently qualified production candidate.'])
    for path,sha in scripts.items():assert hashlib.sha256(Path(path).read_bytes()).hexdigest()==sha,'Analysis implementation changed'
    (a.output/'summary.json').write_text(json.dumps(summary,indent=2)+'\n')
