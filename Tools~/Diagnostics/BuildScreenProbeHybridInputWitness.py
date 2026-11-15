"""Witness the conflicting Hybrid block with two exact receiver geometries."""
import argparse
import hashlib
import json
from pathlib import Path

from BuildScreenProbeGeometryReference import build


def witness(output, geometry0, geometry1):
    source,shader,sha0=build(output,geometry0,"Hybrid")
    data1=geometry1.read_bytes()
    assert len(data1)==120*136*16
    sha1=hashlib.sha256(data1[:120*68*16]).hexdigest()

    def replace(old,new):
        nonlocal source
        assert source.count(old)==1,old[:100]
        source=source.replace(old,new,1)

    replace("warmup = 512, count = 1024","warmup = 832, count = 64")
    replace("blockFrames=64,blocks=32","blockFrames=64,blocks=2")
    replace('currentArm=="uniform-b"?16:0','currentArm=="geometry-1"?1:0')
    replace('if(tag=="geometry" && baseGeometrySha256!="'+sha0+'")',
            'if(tag=="geometry" && baseGeometrySha256!=(currentArm=="geometry-0"?"'+sha0+'":"'+sha1+'"))')
    replace('        await Run("uniform-a", 7919); await Run("uniform-b", 25387);', '''        await Run("geometry-0",25387);
        var secondInput=System.IO.File.ReadAllBytes(output+"/geometry-input-1.bin");
        if(Hash(secondInput)!="'''+hashlib.sha256(data1).hexdigest()+'''")throw new InvalidOperationException("Second witness geometry changed.");
        referenceGeometryInput.LoadRawTextureData(secondInput);referenceGeometryInput.Apply(false,false);
        await Run("geometry-1",25387);''')
    replace('        gi.gatherMethod.Override(SdfDiffuseGatherMethod.ScreenProbeGather); gi.screenProbeSamplingGuide.Override(false);',
            '        gi.gatherMethod.Override(SdfDiffuseGatherMethod.ScreenProbeGather); gi.screenProbeSamplingGuide.Override(false);\n        gi.recordRayDiagnostics.Override(true);gi.recordFirstHitDiagnostics.Override(true);')
    replace('        Read(Resolve("m_HitLighting"), "raw");', '''        Read(Resolve("m_HitLighting"), "raw");
        ReadCrop(context,Resolve("m_ScreenRays"),"screen-rays",46*8,59,28*8,1);
        ReadCrop(context,Resolve("m_RayDiagnostics"),"ray-diagnostics",46*8,59,28*8,1);
        ReadCrop(context,Resolve("m_FirstHitDiagnostics"),"first-hit",46*8,59*3,28*8,3);
        foreach(var item in new[]{("m_ScreenHzb","input-hzb"),("m_ScreenMotion","input-motion"),
            ("m_PreviousScreenRadiance","input-screen-radiance"),("m_PreviousScreenGeometry","input-screen-geometry"),
            ("m_GBuffer0","input-gbuffer0"),("m_GBuffer1","input-gbuffer1")})Read(Resolve(item.Item1),item.Item2);
        int[] MatrixBits(Matrix4x4 value){var bits=new int[16];for(int row=0;row<4;++row)for(int column=0;column<4;++column)bits[4*row+column]=BitConverter.SingleToInt32Bits(value[row,column]);return bits;}
        object cameraState=Field("m_State");
        var previousVp=(Matrix4x4)cameraState.GetType().GetField("ViewProjection",inst|System.Reflection.BindingFlags.Public).GetValue(cameraState);
        var jitter=(Vector2)Field("m_ScreenJitterDelta");
        records.Add(new{arm,frame,inputMatrices=new{vp=MatrixBits((Matrix4x4)Field("m_ViewProjection")),inverseVp=MatrixBits((Matrix4x4)Field("m_InverseViewProjection")),
            previousVp=MatrixBits(previousVp),previousInverseVpRecomputed=MatrixBits(previousVp.inverse),jitter=new[]{BitConverter.SingleToInt32Bits(jitter.x),BitConverter.SingleToInt32Bits(jitter.y)}}});''')
    replace('            Read(Resolve("m_GBuffer0"),"input-gbuffer0");Read(Resolve("m_GBuffer1"),"input-gbuffer1");Read(Resolve("m_RadiosityInput"), "radiosity");',
            '            Read(Resolve("m_RadiosityInput"), "radiosity");')
    replace('schema = "screen-probe-exact-geometry-reference.v1",','schema = "screen-probe-hybrid-input-witness.v1",')
    helper=Path(__file__).with_name("ScreenProbeHybridInputWitness.cs.txt").read_text(encoding="utf-8-sig")
    replace('    void SetupReference() {',helper+'\n    void SetupReference() {')
    return source,shader,sha0,sha1


if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output",type=Path)
    parser.add_argument("--geometry-0",type=Path,required=True)
    parser.add_argument("--geometry-1",type=Path,required=True)
    args=parser.parse_args();output=args.output.resolve()
    source,shader,sha0,sha1=witness(output,args.geometry_0,args.geometry_1)
    output.mkdir(parents=True,exist_ok=False)
    for name,data in (("capture-script.cs",source.encode()),("reference-tracing.compute.txt",shader.encode()),
            ("geometry-input.bin",args.geometry_0.read_bytes()),("geometry-input-1.bin",args.geometry_1.read_bytes())):
        (output/name).write_bytes(data)
    protocol=dict(schema="screen-probe-hybrid-input-witness-protocol.v1",traceMode="Hybrid",spacing=16,rays=8,
        warmup=832,measuredFrames=64,frameBase=25387,arms=["geometry-0","geometry-1"],baseGeometrySha256=[sha0,sha1],
        payloadBudget=48*1024**2,files={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in output.iterdir()},
        limitations=["Fixed Gather receiver only; native screen history and camera inputs are observed, not overridden",
            "Readback/diagnostic workload; no GPU timing or physical reference claim"])
    (output/"protocol.json").write_text(json.dumps(protocol,indent=2)+"\n",encoding="utf-8")
    print(json.dumps(dict(root=str(output))))
