"""Replay native Resolve over post/pre-calibration SH with same-frame AA."""
import argparse
import hashlib
import json
from pathlib import Path

from BuildScreenProbeHistoryEpoch import build as epoch_build


def build(output,without_jitter=False):
    folder=Path(__file__).resolve().parent;source=epoch_build(output,True)
    helper=(folder/'ScreenProbeResolveComponents.cs.txt').read_text(encoding='utf-8-sig')
    helper=helper.replace('__WITHOUT_JITTER__',str(without_jitter).lower())
    def replace(old,new):
        nonlocal source
        assert source.count(old)==1,old[:100];source=source.replace(old,new,1)
    replace('"EditorGatherFrameOverride", "EditorGatherHistoryDiagnostics",','"EditorGatherFrameOverride", "EditorRecordProbeAnchorDiagnostics", "EditorGatherHistoryDiagnostics",')
    marker='    var previous = hooks.ToDictionary(p => p.Key, p => p.Value.GetValue(null));'
    replace(marker,marker+'\n    if((bool)previous["EditorRecordProbeAnchorDiagnostics"])throw new InvalidOperationException("Anchor diagnostics already owned.");')
    replace('    void SetupQuality() {',helper+'\n    void SetupQuality() {')
    replace('SetupQuality();if(!EditorUtility.IsPersistent','SetupQuality();SetupComponents();if(!EditorUtility.IsPersistent')
    replace('hooks["EditorGatherHistoryDiagnostics"].SetValue(null, 0);','hooks["EditorGatherHistoryDiagnostics"].SetValue(null, 0);hooks["EditorRecordProbeAnchorDiagnostics"].SetValue(null,true);')
    marker='        ReadQuality(context,texture,index==0?"resolved-points":"temporal-points","pixel-points");'
    replace(marker,marker+'\n        if(index==0){ReadQuality(context,texture,"resolved","pixel");ReplayComponents(context,texture);}')
    marker='        ReadQuality(context,Resolve("m_Probes"),"anchor","probe");'
    replace(marker,marker+'\n        ReadQuality(context,Resolve("m_GBuffer1"),"packed-normal-points","pixel-points");')
    replace('        PublishQualityStatistics();','        if(componentPending.Count!=0 || componentAaPairs!=count)throw new InvalidOperationException("Incomplete component AA.");\n        PublishQualityStatistics();')
    replace('            CleanupExperimentalTracing();','            CleanupComponents();CleanupExperimentalTracing();')
    replace('experimentalTracePrograms,epochReset,','experimentalTracePrograms,componentPrograms,componentAaPairs,epochReset,')
    return source


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('output',type=Path);p.add_argument('--without-jitter',action='store_true');args=p.parse_args();out=args.output.resolve();source=build(out,args.without_jitter)
    out.mkdir(parents=True,exist_ok=False);(out/'capture-script.cs').write_text(source,encoding='utf-8')
    (out/'copy-irradiance.compute.txt').write_text(Path(__file__).with_name('SdfCopyIrradiancePlanes.compute.txt').read_text(encoding='utf-8-sig'),encoding='utf-8')
    (out/'depth-copy.compute.txt').write_text('#pragma kernel CopyDepthCS\nTexture2D<float> _Source;RWTexture2D<float> _Destination;\n[numthreads(8,8,1)]void CopyDepthCS(uint3 id:SV_DispatchThreadID){if(any(id.xy>=uint2(1920,1080)))return;_Destination[id.xy]=_Source.Load(int3(id.xy,0));}\n',encoding='utf-8')
    protocol=dict(schema='screen-probe-resolve-components-protocol.v2',corrected=False,guided=True,sequence=1733,warmup=1024,measuredFrames=128,spacing=16,rays=8,payloadBudget=192*1024**2,historyEpoch=True,finalStatistics=True,resolveComponents=True,withoutJitter=args.without_jitter,
        files={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in out.iterdir()},
        gates=dict(nativeCopiedResolveByteExact=True,coverageExact=not args.without_jitter,varianceClosureRelativeTolerance=1e-10),
        note='Existing diagnostic SH planes7..13 are before scalar calibration; withoutJitter mode instead retains current calibrated SH and only disables private interpolation jitter. Same native Resolve kernel/inputs, owned outputs/atlas, restored bindings/constants; no renderer history writes. Deltas include output clamp and FP16 rounding.')
    (out/'protocol.json').write_text(json.dumps(protocol,indent=2)+'\n',encoding='utf-8');print(json.dumps(dict(root=str(out))))
