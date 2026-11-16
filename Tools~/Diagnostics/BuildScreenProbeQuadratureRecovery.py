"""Exercise original128-frame motion followed by128 stationary recovery frames."""
import argparse
import hashlib
import json
from pathlib import Path

from BuildScreenProbeQuadratureHistory import build as history_build


def build(output):
    source,shaders=history_build(output,1733,True)
    def replace(old,new):
        nonlocal source
        assert source.count(old)==1,old[:100]
        source=source.replace(old,new,1)
    replace('warmup = 1024, count = 128','warmup = 1024, count = 256')
    replace('blockFrames=64,blocks=2','blockFrames=64,blocks=4')
    replace('384L*1024*1024','160L*1024*1024')
    replace('var privateRois=new[]{("edge",880,414,160,128)};',
            'var privateRois=new[]{("ceiling",912,970,96,48),("edge",880,414,160,128)};')
    replace('privateAaPairs!=(count+4)*10','privateAaPairs!=(count+4)*15')
    replace('phase==1 && frame>=warmup?Mathf.Sin',
            'phase==1 && frame>=warmup && frame<warmup+128?Mathf.Sin')
    replace('else if(frame>=warmup)ReadQuality(context,mode.output,"quadrature","pixel");',
            'else if(frame>=warmup)ReadRecoveryEligibility(context,mode.output);')
    marker='        int fullWidth=texture.width,fullHeight=texture.height,f=frame;string currentArm=arm,format=texture.graphicsFormat.ToString();'
    replace(marker,marker+'''
        save=(role=="final" && side!="native" && f>=warmup) || IsRecoveryKeyFrame(f);''')
    helper=Path(__file__).with_name('ScreenProbeQuadratureRecovery.cs.txt').read_text(encoding='utf-8-sig')
    replace('    void SetupQuality() {',helper+'\n    void SetupQuality() {')
    a=source.index('    Action<ComputePassContext,int,Texture> qualityStage=')
    b=source.index('    void Log(',a)
    source=source[:a]+'''    Action<ComputePassContext,int,Texture> qualityStage=(context,index,texture)=>{
        if(!active || phase!=1 || context.Get<VividCameraData>().camera!=camera)return;
        if(index==0){ReplayComponents(context,texture);NormalizeQuadrature(context);}
        if(index==1)ReplayPrivateTemporal(context);
    };
'''+source[b:]
    a=source.index('    Action<ComputePassContext, Texture, Texture, Texture> trace =')
    b=source.index('    Action<ComputePassContext, Texture, Texture, Texture, Texture> linear =',a)
    prefix=source[a:b];prefix=prefix[:prefix.index('        if (frame < warmup) return;')]
    source=source[:a]+prefix+'''        if(frame>=warmup)RecordRecoveryState(context);
        if(frame==warmup || frame==warmup+count-1)ReadRecoverySource(context);
    };
'''+source[b:]
    replace('if(blockCounts.Any(n=>n!=64))throw new InvalidOperationException("Incomplete aggregate blocks.");','')
    replace('        WriteDoubles("block-sums.f64",sums);WriteDoubles("block-squares.f64",squares);','')
    replace('        PublishQualityStatistics();','')
    return source,shaders


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('output',type=Path);args=p.parse_args();out=args.output.resolve()
    source,shaders=build(out);out.mkdir(parents=True,exist_ok=False)
    (out/'capture-script.cs').write_text(source,encoding='utf-8')
    for name,body in shaders.items():(out/name).write_text(body,encoding='utf-8')
    protocol=dict(schema='screen-probe-quadrature-recovery-protocol.v1',warmup=1024,measuredFrames=256,motionFrames=128,
        spacing=16,rays=8,payloadBudget=160*1024**2,regions=['ceiling','edge'],
        aaFrames=[0,1,2,1023,*range(1024,1280)],
        keyFrames=[0,1024,1151,1152,1215,1216,1247,1279],
        trajectory=dict(period=128,lateral=.28,forward=.06,yawDegrees=-7,stationaryTail=128),
        files={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in out.iterdir()},
        gates=dict(nativePrivateByteExact=True,minimumStationaryFrames=120,minimumFinalCeilingEligibleFraction=.5,
            recoveredMeanChangeAbsPercent=1,reentryRoiMeanStepAbsPercent=.25,reentryP99GainStepAbsPercent=2),
        note='Same native probe input; independent private pixel recurrence. Normalized positive alpha. Original128-frame sine motion then stop. Save all final ROI sequences; other roles only keyframes with all-frame AA hashes, plus all-frame eligibility counts. No transport feedback/default change.')
    (out/'protocol.json').write_text(json.dumps(protocol,indent=2)+'\n',encoding='utf-8');print(json.dumps(dict(root=str(out))))
