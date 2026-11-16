"""Start native Gather from a fresh history epoch after the source cache converges."""
import argparse
import hashlib
import json
from pathlib import Path

from BuildScreenProbeBlackEnvironmentQuality import build as quality_build


def build(output,guided,sequence=1733,measured=128,pixel_stages=False):
    source=quality_build(output,False,guided,sequence,measured)
    helper=Path(__file__).with_name('ScreenProbeHistoryEpoch.cs.txt').read_text(encoding='utf-8-sig')
    def replace(old,new):
        nonlocal source
        assert source.count(old)==1,old[:100];source=source.replace(old,new,1)
    replace('    void SetupQuality() {',helper+'\n    void SetupQuality() {')
    replace('SetupQuality();SetupExperimentalTracing(qualityPass);',
            'SetupQuality();if(!EditorUtility.IsPersistent((ComputeShader)type.GetField("m_TraceCompute",inst).GetValue(qualityPass)))throw new InvalidOperationException("Original persistent trace required.");')
    replace('if (phase == 0) { ready = frozen ? ready + 1 : 0; if (ready < 8) return; phase = 1; frame = -1; }',
            'if (phase == 0) { ready = frozen ? ready + 1 : 0; if (ready < 8) return; phase = 2;active=false;return; }')
    replace('        if (frame < warmup) return;','        CaptureEpochFirstFrame(context);\n        if (frame < warmup) return;')
    replace('if (frame < warmup) { phase = 0; frame = 0; ready = 0; records.Add(new { arm, warmupRestart = true }); return; }',
            'throw new InvalidOperationException("Frozen source invalidated after epoch reset.");')
    replace('            throw new InvalidOperationException("Source invalidated during reference measurement.");','')
    marker='        System.IO.File.WriteAllText(output + "/progress.txt", arm + " complete");'
    replace(marker,'''        if(!epochReset) {
            if(phase!=2)throw new InvalidOperationException("Source epoch was not ready.");
            ResetGatherHistoryEpoch();phase=1;frame=0;active=true;
            while(active && errors.Count==0 && EditorApplication.timeSinceStartup<deadline) {
                if(System.IO.File.Exists(output+"/cancel.txt"))throw new OperationCanceledException("Owner cancelled.");
                game.Repaint();EditorApplication.QueuePlayerLoopUpdate();await System.Threading.Tasks.Task.Delay(16);
            }
            AsyncGPUReadback.WaitAllRequests();
            for(int i=0;pending>0 && i<50;++i)await System.Threading.Tasks.Task.Delay(20);
            if(active || pending!=0 || errors.Count!=0)throw new InvalidOperationException("Epoch capture failed: "+string.Join("; ",errors));
        }
'''+marker)
    replace('experimentalTracePrograms, complete,','experimentalTracePrograms,epochReset,epochInvalidated, complete,')
    if pixel_stages:
        marker='        ReadQuality(context,texture,index==0?"resolved-points":"temporal-points","pixel-points");'
        replace(marker,marker+'\n        ReadQuality(context,texture,index==0?"resolved":"temporal","pixel");')
    return source


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('output',type=Path);p.add_argument('--guided',action='store_true');p.add_argument('--sequence',type=int,default=1733);p.add_argument('--measured',type=int,choices=(128,1024),default=128);p.add_argument('--pixel-stages',action='store_true')
    args=p.parse_args();out=args.output.resolve();source=build(out,args.guided,args.sequence,args.measured,args.pixel_stages)
    out.mkdir(parents=True,exist_ok=False);(out/'capture-script.cs').write_text(source,encoding='utf-8')
    (out/'depth-copy.compute.txt').write_text('#pragma kernel CopyDepthCS\nTexture2D<float> _Source;RWTexture2D<float> _Destination;\n[numthreads(8,8,1)]void CopyDepthCS(uint3 id:SV_DispatchThreadID){if(any(id.xy>=uint2(1920,1080)))return;_Destination[id.xy]=_Source.Load(int3(id.xy,0));}\n',encoding='utf-8')
    protocol=dict(schema='screen-probe-history-epoch-protocol.v1',corrected=False,guided=args.guided,sequence=args.sequence,
        warmup=4096 if args.measured==1024 else 1024,measuredFrames=args.measured,spacing=16,rays=8,payloadBudget=192*1024**2,historyEpoch=True,finalStatistics=True,pixelStages=args.pixel_stages,
        files={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in out.iterdir()},
        gates=dict(inputGeometryExact=True,affectedVarianceReductionPercent=5,otherVarianceIncreasePercent=2,meanChangeAbsPercent=1,frameDifferenceRmsIncreasePercent=2),
        note='Original persistent production trace, flags0; source frozen before metadata-only Gather history invalidation. No production edits. First epoch frame must reject history; source invalidation aborts.')
    (out/'protocol.json').write_text(json.dumps(protocol,indent=2)+'\n',encoding='utf-8');print(json.dumps(dict(root=str(out))))
