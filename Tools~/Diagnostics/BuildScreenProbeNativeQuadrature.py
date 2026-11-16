"""Run positive-coverage quadrature through the native GI chain, not private history."""
import argparse
import hashlib
import json
from pathlib import Path

from BuildScreenProbeHistoryEpoch import build as epoch_build
from BuildScreenProbeQuadrature import shader_source as prototype_source


def shader_source():
    source=prototype_source()
    old='''            // Negative coverage is an Editor-only eligibility marker. Consumers
            // of this counterfactual use abs(a), never publish it as native history.
            lighting = float4(covered.rgb / max(covered.a, 1e-5), -covered.a);'''
    assert source.count(old)==1
    return source.replace(old,'''            // Native output always has positive coverage. Only an owned
            // stage0 diagnostic replay requests the private eligibility marker.
            float alpha = (_SdfGatherHistoryDiagnostics & 134217728u) != 0u ? -covered.a : covered.a;
            lighting = float4(covered.rgb / max(covered.a, 1e-5), alpha);''',1)


def build(output,variant,sequence):
    assert variant in ('original','control','candidate') and 0<=sequence<2**31
    source=epoch_build(output,True,sequence,128,True)
    helper=Path(__file__).with_name('ScreenProbeExperimentalGather.cs.txt').read_text(encoding='utf-8-sig')
    witness=Path(__file__).with_name('ScreenProbeNativeQuadrature.cs.txt').read_text(encoding='utf-8-sig')
    def replace(old,new):
        nonlocal source
        assert source.count(old)==1,old[:100];source=source.replace(old,new,1)
    replace('    void SetupQuality() {',helper+'\n'+witness+'\n    void SetupQuality() {')
    replace('SetupQuality();if(!EditorUtility.IsPersistent',
            'SetupQuality();'+('SetupQuadrature();' if variant!='original' else '')+'if(!EditorUtility.IsPersistent')
    replace('hooks["EditorGatherHistoryDiagnostics"].SetValue(null, 0);',
            'hooks["EditorGatherHistoryDiagnostics"].SetValue(null, '+str(67108864 if variant=='candidate' else 0)+');')
    if variant=='candidate':
        replace('    Action<ComputePassContext,int,Texture> qualityStage=(context,index,texture)=>{',
                '''    Action<ComputePassContext,int,Texture> qualityStage=(context,index,texture)=>{
        if(active && phase==1 && index==0 && context.Get<VividCameraData>().camera==camera
            && (frame==warmup || frame==warmup+count-1))RecordNativeQuadratureWitness(context,texture);''')
    replace('            CleanupExperimentalTracing();','            CleanupNativeQuadratureWitness();CleanupQuadrature();CleanupExperimentalTracing();')
    replace('experimentalTracePrograms,epochReset,','experimentalTracePrograms,quadraturePrograms,epochReset,')
    return source


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('output',type=Path)
    p.add_argument('--variant',choices=('original','control','candidate'),required=True);p.add_argument('--sequence',type=int,default=1733)
    args=p.parse_args();out=args.output.resolve();source=build(out,args.variant,args.sequence);out.mkdir(parents=True,exist_ok=False)
    (out/'capture-script.cs').write_text(source,encoding='utf-8')
    if args.variant!='original':(out/'quadrature-gather.compute.txt').write_text(shader_source(),encoding='utf-8')
    (out/'depth-copy.compute.txt').write_text('#pragma kernel CopyDepthCS\nTexture2D<float> _Source;RWTexture2D<float> _Destination;[numthreads(8,8,1)]void CopyDepthCS(uint3 id:SV_DispatchThreadID){if(any(id.xy>=uint2(1920,1080)))return;_Destination[id.xy]=_Source.Load(int3(id.xy,0));}\n',encoding='utf-8')
    protocol=dict(schema='screen-probe-native-quadrature-protocol.v1',variant=args.variant,corrected=False,guided=True,
        sequence=args.sequence,warmup=1024,measuredFrames=128,spacing=16,rays=8,payloadBudget=192*1024**2,
        historyEpoch=True,finalStatistics=True,pixelStages=True,nativeDiagnosticsFlags=67108864 if args.variant=='candidate' else 0,
        files={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in out.iterdir()},
        gates=dict(inputHashesExact=True,normalAndValidityExact=True,geometryPositionMaxMeters=.000002,
            meanChangeAbsPercent=1,ceilingVarianceReductionPercent=5,otherVarianceIncreasePercent=2,
            controlMeanChangeAbsPercent=.1,controlVarianceChangeAbsPercent=2),
        note='Normal7plane SH and native pixel/transport feedback; positive native coverage. Original uses persistent Gather; control/candidate install the same in-memory shader with the flag off/on. Private signed witness only at measured endpoints, restored before native temporal. Camera histories/source reset per case; defaults restored in finally.')
    (out/'protocol.json').write_text(json.dumps(protocol,indent=2)+'\n',encoding='utf-8');print(json.dumps(dict(root=str(out))))
