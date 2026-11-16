"""Measure same-frame baseline/stratified Resolve with GPU-only recorders."""
import argparse
import hashlib
import json
from pathlib import Path

from BuildScreenProbeQuadrature import build as quadrature_build, shader_source


def build(output):
    source=quadrature_build(output)
    def replace(old,new):
        nonlocal source
        assert source.count(old)==1,old[:100]
        source=source.replace(old,new,1)
    replace('using UnityEngine;', 'using Unity.Profiling;\nusing UnityEngine;')
    replace('warmup = 1024, count = 128','warmup = 1024, count = 640')
    replace('blockFrames=64,blocks=2','blockFrames=64,blocks=10')
    replace('192L*1024*1024','8L*1024*1024')
    helper=Path(__file__).with_name('ScreenProbeQuadratureTiming.cs.txt').read_text(encoding='utf-8-sig')
    replace('    ComputeShader componentCopy=null;', helper+'\n    ComputeShader componentCopy=null;')
    replace('hooks["EditorRecordProbeAnchorDiagnostics"].SetValue(null,true);',
            'hooks["EditorRecordProbeAnchorDiagnostics"].SetValue(null,false);')
    replace('|| !(bool)type.GetField("m_RecordProbeAnchorDiagnostics",inst).GetValue(qualityPass)',
            '|| (bool)type.GetField("m_RecordProbeAnchorDiagnostics",inst).GetValue(qualityPass)')
    replace('atlas.width!=2640','atlas.width!=840')
    replace('        int f=frame;pending++;',
            '        int f=frame;if(f>2 && f!=warmup-1 && f!=warmup+count-1)return;pending++;')
    old='            foreach(var mode in new[]{(offset:0,output:componentAa,aa:true),(offset:0,output:componentPre,aa:false)}) {'
    replace(old,'''            bool reverse=((frame-warmup)&3)==1 || ((frame-warmup)&3)==2;
            var aaMode=(offset:0,output:componentAa,aa:true);var candidateMode=(offset:0,output:componentPre,aa:false);
            foreach(var mode in reverse?new[]{candidateMode,aaMode}:new[]{aaMode,candidateMode}) {''')
    replace('                cmd.DispatchCompute(shader,kernel,240,135,1);',
            '''                string marker=mode.aa?"GI Quadrature Baseline Resolve":"GI Quadrature Stratified Resolve";
                cmd.BeginSample(marker);cmd.DispatchCompute(shader,kernel,240,135,1);cmd.EndSample(marker);''')
    replace('else ReadQuality(context,mode.output,"quadrature","pixel");',
            'else if(frame==warmup-1 || frame==warmup+count-1)ReadQuality(context,mode.output,"quadrature","pixel");')
    a=source.index('    Action<ComputePassContext,int,Texture> qualityStage=')
    b=source.index('    void Log(',a)
    source=source[:a]+'''    Action<ComputePassContext,int,Texture> qualityStage=(context,index,texture)=>{
        if(!active || phase!=1 || index!=0 || context.Get<VividCameraData>().camera!=camera)return;
        if(frame==warmup+64)StartTiming();
        ReplayComponents(context,texture);
    };
'''+source[b:]
    a=source.index('    Action<ComputePassContext, Texture, Texture, Texture> trace =')
    b=source.index('    Action<ComputePassContext, Texture, Texture, Texture, Texture> linear =',a)
    prefix=source[a:b]
    prefix=prefix[:prefix.index('        if (frame < warmup) return;')]
    source=source[:a]+prefix+'''        CheckTimingWorkload(context,lighting,geometry);
        if(frame==warmup-1 || frame==warmup+count-1)ReadTimingSource(context);
    };
'''+source[b:]
    replace('if(blockCounts.Any(n=>n!=64))throw new InvalidOperationException("Incomplete aggregate blocks.");',
            'FinishTiming();')
    replace('        WriteDoubles("block-sums.f64",sums);WriteDoubles("block-squares.f64",squares);','')
    replace('componentAaPairs!=count','componentAaPairs!=5')
    replace('        PublishQualityStatistics();','')
    replace('            CleanupQuadrature();CleanupComponents();',
            '            CleanupTiming();CleanupQuadrature();CleanupComponents();')
    replace('componentAaPairs,quadraturePrograms,','componentAaPairs,quadraturePrograms,timingSamples,timingStarted,')
    return source


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('output',type=Path)
    args=parser.parse_args();output=args.output.resolve();source=build(output)
    output.mkdir(parents=True,exist_ok=False)
    (output/'capture-script.cs').write_text(source,encoding='utf-8')
    (output/'quadrature-gather.compute.txt').write_text(shader_source(),encoding='utf-8')
    (output/'copy-irradiance.compute.txt').write_text(Path(__file__).with_name('SdfCopyIrradiancePlanes.compute.txt').read_text(encoding='utf-8-sig'),encoding='utf-8')
    (output/'depth-copy.compute.txt').write_text('#pragma kernel CopyDepthCS\nTexture2D<float> _Source;RWTexture2D<float> _Destination;[numthreads(8,8,1)]void CopyDepthCS(uint3 id:SV_DispatchThreadID){if(any(id.xy>=uint2(1920,1080)))return;_Destination[id.xy]=_Source.Load(int3(id.xy,0));}\n',encoding='utf-8')
    protocol=dict(schema='screen-probe-quadrature-timing-protocol.v1',warmup=1024,frames=640,timingStart=1088,
        payloadBudget=8*1024**2,spacing=16,rays=8,aaFrames=[0,1,2,1023,1663],
        censorFirstGpuSamples=64,censorLastGpuSamples=64,minimumRetainedGpuSamples=384,
        markers=['GI Quadrature Baseline Resolve','GI Quadrature Stratified Resolve'],
        files={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in output.iterdir()},
        note='Same-frame shared native probe inputs, normal7plane SH, identical copied atlas and alternating AB/BA/BA/AB dispatch order. GPU markers surround only Resolve dispatch, excluding atlas copy/readback. No readback requests during retained timing interval. This is marginal Resolve cost, not production total GI cost.')
    (output/'protocol.json').write_text(json.dumps(protocol,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(dict(root=str(output))))
