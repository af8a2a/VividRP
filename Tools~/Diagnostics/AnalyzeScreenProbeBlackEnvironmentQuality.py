"""Compare native black-boundary controls without relaxing exact input checks."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

from ProbeAnchorCalibration import LUMA,evaluate_sh,normal_from_packed
from TemporalSamplingExperiment import read,metrics,paired


ROIS=('ceiling','floor','back','edge')


def inspect(root):
    assert not (root/'raw-retention.json').exists(),'Full sequence archived'
    load=lambda n:json.loads((root/n).read_text(encoding='utf-8-sig'))
    capture,protocol=load('capture.json'),load('protocol.json')
    assert capture['schema']=='screen-probe-black-environment-quality.v1'
    assert capture['complete'] and not capture['errors'] and load('source-verification.json')['unchanged']
    for name,digest in protocol['files'].items():assert hashlib.sha256((root/name).read_bytes()).hexdigest()==digest
    count=protocol['measuredFrames'];assert count in (128,1024)
    warmup=4096 if count==1024 else 1024
    assert (capture['warmup'],capture['measuredFrames'],capture['spacing'],capture['rays'])==(warmup,count,16,8)
    payload=0
    for row in capture['records']:
        if not row.get('path'):continue
        path=Path(row['path']).resolve();assert path.is_relative_to(root.resolve())
        data=path.read_bytes();assert hashlib.sha256(data).hexdigest()==row['sha256'];payload+=len(data)
    assert payload==capture['payloadBytes']<=capture['payloadBudget']==protocol['payloadBudget']
    # Independent replay paths have their own explicit side; keep source rows
    # separate while still hashing and budgeting every payload above.
    source_rows=[r for r in capture['records'] if 'tag' in r and 'side' not in r]
    rows={(r['frame'],r['tag']):r for r in source_rows}
    assert len(rows)==len(source_rows)
    states={r['frame']:r for r in capture['records'] if 'state' in r}
    assert set(states)==set(range(warmup,warmup+count))
    for state in states.values():
        s=state['state']
        assert (s['order'],s['batch'],s['samples'],s['seed'])==(8,32,64,0)
        assert (s['anchorMaxAge'],s['pixelMaxAge'],s['atrous'])==(512,32,4)
        expected_flags=33554432 if protocol['corrected'] else 0
        if protocol.get('schema')=='screen-probe-native-quadrature-protocol.v1':
            assert protocol['variant'] in ('original','control','candidate') and not protocol['corrected']
            expected_flags=67108864 if protocol['variant']=='candidate' else 0
            assert protocol['nativeDiagnosticsFlags']==expected_flags
        if protocol.get('schema')=='screen-probe-native-quadrature-history-protocol.v1':
            assert protocol['nativeSide']=='candidate' and protocol['irradiancePlanes']==7 and not protocol['corrected']
            expected_flags=67108864
        assert s['flags']==expected_flags
        assert s['guide']==protocol['guided'] and not s['restir'] and s['traceMode']=='Hybrid'
        assert s['activeSkyType']=='None' and s['sun']==[0,0,0] and s['sky']==0
    assert {rows[f,'radiosity']['sha256'] for f in (warmup,warmup+count-1)}=={'40252e7ae82c1da6ae025dbf012a3ce232aa54ea10bfbf1ca9a7998df93270a8'}
    return capture,protocol,rows,states


def compare(baseline,candidate):
    captures=[inspect(r) for r in (baseline,candidate)]
    assert all(c[1]['measuredFrames']==128 for c in captures),'Use long-window statistics analysis'
    assert not captures[0][1]['corrected'] and captures[1][1]['corrected']
    for key in ('guided','sequence','warmup','measuredFrames','spacing','rays'):assert captures[0][1][key]==captures[1][1][key]
    differences=[]
    for f in range(1024,1152):
        for tag in ('geometry','pixel-geometry','input-depth','input-gbuffer0','input-gbuffer1'):
            a,b=(c[2][f,tag] for c in captures)
            if any(a[k]!=b[k] for k in ('sha256','width','height','format')):differences.append([f,tag])
        a,b=(c[3][f] for c in captures)
        if a['vp']!=b['vp'] or a['state']['inverseVp']!=b['state']['inverseVp']:differences.append([f,'matrices'])
    report=dict(schema='screen-probe-black-environment-quality-comparison.v1',qualified=not differences,
        inputDifferences=differences,productionQualified=False,guided=captures[0][1]['guided'])
    if differences:return report
    signals=[];raw_energy=[]
    for _,protocol,rows,_ in captures:
        stack=lambda tag:np.stack([read(rows[f,tag]).astype(float) for f in range(1024,1152)])
        raw=stack('raw');geometry=stack('geometry')
        raw_energy.append(raw[...,:3]*raw[...,3:4])
        regions={}
        for roi in ROIS:
            x,y,points=({'ceiling':[46,59,28],'floor':[54,8,12],'back':[68,38,8],'edge':[55,26,10]})[roi]
            selected=geometry[:,y,x:x+points]
            normal=normal_from_packed(selected[...,3])
            anchor=stack('anchor-'+roi)
            sh=stack('sh-'+roi)
            stages={}
            for tag in ('anchor','prepared','reconstructed','resolved-points','temporal-points','final-points'):
                v=stack(tag+'-'+roi)[:,0]
                stages[tag]=(v[...,:3]*abs(v[...,3:4]))@LUMA
            stages['raw']=(raw[:,y,x:x+points,:3]*raw[:,y,x:x+points,3:4])@LUMA
            stages['sh-at-normal']=np.maximum(evaluate_sh(sh,normal),0)@LUMA*anchor[:,0,:,3]
            final=stack('final-'+roi)
            stages['final']=(final[...,:3]*abs(final[...,3:4]))@LUMA
            directional=stack('directional-'+roi);meta=stack('directional-meta-'+roi)
            bits=meta[...,1].astype('<f2').view('<u2').astype(np.int32)
            valid=(bits>=0x400)&(bits<0x2400);payload=bits-0x400
            age=np.where(valid,(payload&511)/32,0);stale=np.where(valid,(payload>>9)&15,0)
            regions[roi]=dict(signals=stages,
                directionCoverage=float(directional[...,3].mean()),directionAge=float(age.mean()),
                validDirectionFraction=float((directional[...,3]>0).mean()),directionStale=float(stale.mean()),
                rawCoverage=float(raw[:,y,x:x+points,3].mean()))
        signals.append(regions)
    raw_delta=abs(raw_energy[1]-raw_energy[0]);scale=np.maximum(abs(raw_energy[1]),abs(raw_energy[0]))
    raw_equal=bool(np.all(raw_delta<=.0002+.002*scale))
    channel_change=100*(raw_energy[1].sum((0,1,2))/raw_energy[0].sum((0,1,2))-1)
    guided=captures[0][1]['guided']
    raw_gate=bool(np.all(abs(channel_change)<=1)) if guided else raw_equal
    report.update(rawEnergyWithinFp16Bound=raw_equal,maximumRawConsumedRgbError=float(raw_delta.max()),
        totalRawConsumedRgbChangePercent=100*(raw_energy[1].sum()/raw_energy[0].sum()-1),
        rawConsumedChannelChangePercent=channel_change.tolist(),pairedRawSamplesExpectedIdentical=not guided,
        rawEnergyGatePassed=raw_gate,
        rawEnergyGateScope='Guided proposal feedback changes sample directions: require each global consumed RGB mean within1percent' if guided else 'Same uniform rays: per-sample FP16 consumed-energy bound')
    regions={}
    for roi in ROIS:
        a,b=(s[roi] for s in signals)
        regions[roi]=dict(stages={stage:dict(baseline=metrics(a['signals'][stage]),candidate=metrics(b['signals'][stage]),
            comparison=paired(a['signals'][stage],b['signals'][stage])) for stage in a['signals']},
            baseline={k:v for k,v in a.items() if k!='signals'},candidate={k:v for k,v in b.items() if k!='signals'})
    gates=captures[0][1]['gates']
    final={roi:r['stages']['final']['comparison'] for roi,r in regions.items()}
    noise=any(final[roi]['variance_reduction_percent']>=gates['affectedVarianceReductionPercent'] for roi in ('floor','back'))
    no_regression=all(v['variance_reduction_percent']>=-gates['otherVarianceIncreasePercent'] for v in final.values())
    energy=all(abs(v['mean_change_percent'])<=gates['meanChangeAbsPercent'] for v in final.values())
    report.update(regions=regions,affectedNoiseGatePassed=noise,otherNoiseGatePassed=no_regression,meanGatePassed=energy,
        pilotPassed=raw_gate and noise and no_regression and energy,
        limits=['One native-chain sequence; guide-enabled and motion validation are separate gates.',
            'Stage and ROI names do not assert a pure material plane; geometry must be consulted.',
            'No absolute physical or Unreal reference or GPU-cost claim.'])
    return report


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('baseline',type=Path);parser.add_argument('candidate',type=Path);parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args();result=compare(args.baseline.resolve(),args.candidate.resolve())
    args.output.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    compact={k:v for k,v in result.items() if k!='regions'}
    if 'regions' in result:compact['final']={r:v['stages']['final']['comparison'] for r,v in result['regions'].items()}
    print(json.dumps(compact,indent=2))
