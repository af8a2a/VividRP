"""Hold the shared-input Resolve pair for the maintained PIX capture workflow."""
import argparse
import hashlib
import json
from pathlib import Path

from BuildScreenProbeQuadratureTiming import build as timing_build
from BuildScreenProbeQuadrature import shader_source


def build(output, dispatch_repeats=1):
    assert dispatch_repeats in (1,8)
    source=timing_build(output)
    marker='cmd.BeginSample(marker);cmd.DispatchCompute(shader,kernel,240,135,1);cmd.EndSample(marker);'
    assert source.count(marker)==1
    source=source.replace(marker,'cmd.BeginSample(marker);for(int repetition=0;repetition<'+str(dispatch_repeats)+';++repetition)cmd.DispatchCompute(shader,kernel,240,135,1);cmd.EndSample(marker);',1)
    a=source.index('    void StartTiming() {');b=source.index('    void FinishTiming()',a)
    source=source[:a]+'''    void StartTiming() {
        if(timingStarted || pending!=0)throw new InvalidOperationException("PIX hold entered before readbacks drained.");
        timingStarted=true;
        System.IO.File.WriteAllText(output+"/ready-for-pix.txt","Both private Resolve dispatches active; native7plane SH, stable source.");
    }
'''+source[b:]
    a=source.index('    void FinishTiming() {');b=source.index('    void CleanupTiming()',a)
    source=source[:a]+'''    void FinishTiming() {
        if(!timingStarted)throw new InvalidOperationException("PIX hold never became ready.");
    }
'''+source[b:]
    marker='&& ++frame >= warmup + count) active = false;'
    assert source.count(marker)==1
    source=source.replace(marker,'&& ++frame >= warmup + count && System.IO.File.Exists(output+"/done.txt")) active = false;',1)
    # A long awaited script owns the Unity CLI command queue. Schedule the
    # already-owned run on Editor's main loop, then release that queue so the
    # maintained PIX workflow can issue preflight/capture/status commands.
    boundary=source.index('\n{\n');end=source.rindex('\n}')
    imports,body=source[:boundary],source[boundary+3:end]
    literal=output.as_posix().replace('"','""')
    return imports+'''\n{
    async System.Threading.Tasks.Task<object> RunOwned() {
'''+body+'''
    }
    EditorApplication.delayCall+=async ()=>{
        try {
            var result=await RunOwned();
            System.IO.File.WriteAllText(@"'''+literal+'''/hold-terminal.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{success=true,result}));
        }catch(Exception e){
            System.IO.File.WriteAllText(@"'''+literal+'''/hold-terminal.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{success=false,error=e.ToString()}));
        }
    };
    return new{scheduled=true,output=@"'''+literal+'''"};
}
'''


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('output',type=Path);p.add_argument('--dispatch-repeats',type=int,choices=(1,8),default=1);args=p.parse_args();out=args.output.resolve()
    source=build(out,args.dispatch_repeats);out.mkdir(parents=True,exist_ok=False)
    (out/'capture-script.cs').write_text(source,encoding='utf-8')
    (out/'quadrature-gather.compute.txt').write_text(shader_source(),encoding='utf-8')
    (out/'copy-irradiance.compute.txt').write_text(Path(__file__).with_name('SdfCopyIrradiancePlanes.compute.txt').read_text(encoding='utf-8-sig'),encoding='utf-8')
    (out/'depth-copy.compute.txt').write_text('#pragma kernel CopyDepthCS\nTexture2D<float> _Source;RWTexture2D<float> _Destination;[numthreads(8,8,1)]void CopyDepthCS(uint3 id:SV_DispatchThreadID){if(any(id.xy>=uint2(1920,1080)))return;_Destination[id.xy]=_Source.Load(int3(id.xy,0));}\n',encoding='utf-8')
    protocol=dict(schema='screen-probe-quadrature-pix-hold-protocol.v1',warmup=1024,minimumMeasuredFrames=640,
        payloadBudget=8*1024**2,spacing=16,rays=8,aaFrames=[0,1,2,1023,1663],
        dispatchRepeats=args.dispatch_repeats,
        files={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in out.iterdir()},
        note='After ready-for-pix.txt, run maintained PIX diagnose against the current GI pass. Write done.txt only after capture workflow is terminal; owner cancellation via cancel.txt. GPU recorders intentionally disabled. Same-frame paired private Resolve; no native history writes, no total-GI timing inference.')
    (out/'protocol.json').write_text(json.dumps(protocol,indent=2)+'\n',encoding='utf-8');print(json.dumps(dict(root=str(out))))
