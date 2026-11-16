"""Let the candidate own native pixel histories and verify its independent replay."""
import argparse
import hashlib
import json
from pathlib import Path

from BuildScreenProbeQuadratureHistory import build as history_build
from BuildScreenProbeNativeQuadrature import shader_source


def build(output,sequence=1733,motion=False):
    source,shaders=history_build(output,sequence,motion)
    def replace(old,new):
        nonlocal source
        assert source.count(old)==1,old[:100]
        source=source.replace(old,new,1)
    replace('hooks["EditorGatherHistoryDiagnostics"].SetValue(null, 0);',
            'hooks["EditorGatherHistoryDiagnostics"].SetValue(null, 67108864);')
    replace('hooks["EditorRecordProbeAnchorDiagnostics"].SetValue(null,true);',
            'hooks["EditorRecordProbeAnchorDiagnostics"].SetValue(null,false);')
    replace('|| !(bool)type.GetField("m_RecordProbeAnchorDiagnostics",inst).GetValue(qualityPass)',
            '|| (bool)type.GetField("m_RecordProbeAnchorDiagnostics",inst).GetValue(qualityPass)')
    replace('atlas.width!=2640','atlas.width!=840')
    replace('cmd.SetComputeIntParam(shader,"_SdfGatherHistoryDiagnostics",32|(mode.aa?0:67108864));',
            'cmd.SetComputeIntParam(shader,"_SdfGatherHistoryDiagnostics",32|(mode.aa?0:67108864|134217728));')
    replace('                if(mode.aa)ReadComponentAa(context,mode.output,"copied");\n                else if(frame>=warmup)ReadQuality(context,mode.output,"quadrature","pixel");',
            '                if(!mode.aa && frame>=warmup)ReadQuality(context,mode.output,"quadrature","pixel");')
    replace('cmd.SetComputeIntParam(shader,"_SdfGatherHistoryDiagnostics",32);',
            'cmd.SetComputeIntParam(shader,"_SdfGatherHistoryDiagnostics",32|67108864);')
    replace('ReplayComponents(context,texture);NormalizeQuadrature(context);',
            'ReplayComponents(context,texture);NormalizeQuadrature(context);ReadComponentAa(context,quadratureNormalized,"copied");')
    replace('path==0?PrivateResolve("m_RawResolved"):quadratureNormalized',
            'path==0?componentAa:PrivateResolve("m_RawResolved")')
    # The independently replayed candidate must equal native color, moments,
    # geometry, history and final output; the baseline has its own recurrence.
    replace('path==0,frame==0 ||','path==1,frame==0 ||')
    replace('path==0,frame>=warmup || frame==0','path==1,frame>=warmup || frame==0')
    if motion:
        replace('ReadPrivate(context,PrivateResolve("m_RawResolved"),"resolved","source",false,true);',
                'ReadPrivate(context,componentAa,"resolved","source",false,true);')
    shaders['quadrature-gather.compute.txt']=shader_source()
    return source,shaders


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('output',type=Path)
    p.add_argument('--sequence',type=int,default=1733);p.add_argument('--motion',action='store_true')
    args=p.parse_args();out=args.output.resolve();source,shaders=build(out,args.sequence,args.motion)
    out.mkdir(parents=True,exist_ok=False);(out/'capture-script.cs').write_text(source,encoding='utf-8')
    for name,body in shaders.items():(out/name).write_text(body,encoding='utf-8')
    protocol=dict(schema='screen-probe-native-quadrature-history-protocol.v1',corrected=False,guided=True,
        sequence=args.sequence,warmup=1024,measuredFrames=128,spacing=16,rays=8,payloadBudget=(384 if args.motion else 320)*1024**2,
        historyEpoch=True,finalStatistics=True,quadrature=True,independentPixelHistories=True,nativeSide='candidate',irradiancePlanes=7,
        motion=args.motion,regions=['edge'] if args.motion else ['ceiling','floor','back','edge'],aaFrames=[0,1,2,1023,*range(1024,1152)],
        files={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in out.iterdir()},
        gates=dict(nativeCopiedResolveByteExact=True,nativePrivateHistoryByteExact=True,meanChangeAbsPercent=1,
            ceilingVarianceReductionPercent=5,otherVarianceIncreasePercent=2,frameDifferenceRmsIncreasePercent=2,
            independentSequenceRequired=True,motionRequired=True,energyReferenceRequired=True,gpuRequired=True),
        note='Positive-coverage candidate owns native pixel histories/final output; replayed candidate must match all five native roles, full-frame and ROI. Independent control recurrence uses the same native probe input with flag off. Normal7plane SH. No production source or default change; does not alter prior failed cross-run controls.')
    if args.motion:
        protocol.update(trajectory=dict(period=128,lateral=.28,forward=.06,yawDegrees=-7),motionRegion=[880,414,160,128])
        protocol['gates'].update(disocclusionP99IncreasePercent=5,disocclusionMaxIncreasePercent=5,newlyExposedOldAgeFractionIncrease=.01)
    (out/'protocol.json').write_text(json.dumps(protocol,indent=2)+'\n');print(json.dumps(dict(root=str(out))))
