"""Compare no-sky miss classification on identical native GPU ray inputs."""
import argparse
import hashlib
import json
from pathlib import Path

from BuildScreenProbeSameFrameEnergy import build as energy_build
from BuildSdfBlackEnvironmentCandidate import build_source, install_script


def build(output):
    source,shaders=energy_build(output,1733,7919,1,64,True)
    helper='''    void ReadBlackMicro(ComputePassContext context,Texture texture,string role,string side) {
        int f=frame,width=texture.width,height=texture.height;string format=texture.graphicsFormat.ToString();
        pending++;
        ((CommandBuffer)wrapped.GetValue(context.cmd)).RequestAsyncReadback(texture,0,request=>{
            try {
                if(request.hasError)throw new InvalidOperationException("Black micro readback failed.");
                var bytes=request.GetData<byte>().ToArray();
                if(role=="ray-rgb") {
                    var rgb=new byte[width*136*6];
                    for(int pixel=0;pixel<width*136;++pixel)Buffer.BlockCopy(bytes,pixel*8,rgb,pixel*6,6);
                    bytes=rgb;height=136;format="RGB16_words";
                }else if(role=="flags") {
                    var flags=new byte[width*height*4];
                    for(int pixel=0;pixel<width*height;++pixel)Buffer.BlockCopy(bytes,pixel*16+12,flags,pixel*4,4);
                    bytes=flags;format="R32_SFloat";
                }
                bool save=role!="ray-rgb";
                string file=save?output+"/"+arm+"/"+f.ToString("0000")+"-micro-"+side+"-"+role+".bin":null;
                if(save)Write(file,bytes);
                records.Add(new{arm,frame=f,tag="micro-"+role,side,width,height,format,sha256=Hash(bytes),saved=save,path=file});
            }catch(Exception e){errors.Add(e.Message);}finally{pending--;}
        });
    }
'''

    def replace(old,new):
        nonlocal source
        assert source.count(old)==1,old[:100]
        source=source.replace(old,new,1)

    replace('    string privateMethod="stable-unclamped";','    string privateMethod="none";')
    replace('SetupPrivateReplay();SetupEnergy();','SetupPrivateReplay();SetupEnergy();SetupExperimentalTracing(privatePass);')
    replace('CleanupPrivateReplay();','CleanupExperimentalTracing();CleanupPrivateReplay();')
    replace('if(!EditorUtility.IsPersistent(shader))throw new InvalidOperationException("Original persistent trace shader required.");',
            'if(!object.ReferenceEquals(shader,experimentalTrace))throw new InvalidOperationException("Owned experimental trace shader required.");')
    replace('privatePrograms, energyAaPairs,','privatePrograms, experimentalTracePrograms,energyAaPairs,')
    replace('    object PrivateField(string name)',helper+'\n    object PrivateField(string name)')
    marker='            cmd.SetComputeIntParam(shader,"_SdfDirectionalGuideEnabled",0);'
    replace(marker,'''            int nativeDiagnostics=(int)hooks["EditorGatherHistoryDiagnostics"].GetValue(null)|32;
            if(context.Get<VividSkyData>().activeSkyType!=SkyType.None || !(bool)PrivateField("m_RealtimeOnly"))
                throw new InvalidOperationException("Black micro requires real-time no-sky lighting.");
            cmd.SetComputeIntParam(shader,"_SdfDiffuseRecordRayDiagnostics",1);
            foreach(var mode in new[]{("baseline",nativeDiagnostics),("corrected",nativeDiagnostics|33554432)}) {
                cmd.SetComputeIntParam(shader,"_SdfGatherHistoryDiagnostics",mode.Item2);
                cmd.DispatchCompute(shader,kernel,15,17,1);
                ReadBlackMicro(context,lighting,"raw",mode.Item1);
                ReadBlackMicro(context,geometry,"geometry",mode.Item1);
                ReadBlackMicro(context,energyOutputs.Single(p=>p.field=="m_DirectionalRayTexture").texture,"ray-rgb",mode.Item1);
                ReadBlackMicro(context,energyOutputs.Single(p=>p.field=="m_RayDiagnostics").texture,"flags",mode.Item1);
            }
            cmd.SetComputeIntParam(shader,"_SdfGatherHistoryDiagnostics",nativeDiagnostics);
'''+marker)
    replace('            cmd.SetComputeIntParam(shader,"_SdfDiffuseFrame",nativeFrame);',
            '            cmd.SetComputeIntParam(shader,"_SdfGatherHistoryDiagnostics",(int)hooks["EditorGatherHistoryDiagnostics"].GetValue(null)|32);\n            cmd.SetComputeIntParam(shader,"_SdfDiffuseFrame",nativeFrame);')
    shaders.pop('temporal-candidate.compute.txt')
    shaders['candidate-tracing.compute.txt']=build_source()
    return install_script(source),shaders


if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument("output",type=Path);args=parser.parse_args()
    output=args.output.resolve();source,shaders=build(output)
    output.mkdir(parents=True,exist_ok=False);(output/'capture-script.cs').write_text(source,encoding='utf-8')
    for name,body in shaders.items():(output/name).write_text(body,encoding='utf-8')
    protocol=dict(schema='screen-probe-black-environment-micro.v1',method='none',sequence=1733,referenceSeed=7919,
        setsPerFrame=1,warmup=1024,measuredFrames=64,spacing=16,rays=8,payloadBudget=256*1024**2,
        metadataWitnesses=True,feedbackPublicationWitnesses=True,recordReasons=True,recordGpuBounds=True,
        targetRegions=dict(ceiling=[46,59,28],floor=[54,8,12],back=[68,38,8],edge=[55,26,10]),
        files={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in output.iterdir()},
        gates=dict(nativeBaselineByteExact=True,ceilingVarianceReductionPercent=5,meanChangeAbsPercent=1,
            consumedAbsoluteTolerance=.0002,consumedRelativeTolerance=.002),
        note='Both classification modes share the screen rays, direction guide, frame and owned in-memory candidate shader. Corrected mode only adds bit33554432. No production asset edits, renderer history writes or default change.')
    (output/'protocol.json').write_text(json.dumps(protocol,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(dict(root=str(output))))
