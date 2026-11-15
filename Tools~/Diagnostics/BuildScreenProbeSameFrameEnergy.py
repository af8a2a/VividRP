"""Pair private history candidates with original-shader same-frame ray references."""
import argparse
import hashlib
import json
from pathlib import Path

from BuildScreenProbeColorHistoryReplay import build as replay_build


ACCUMULATOR='''#pragma kernel AccumulateEnergyReferenceCS
Texture2D<float4> _EnergyInput;
RWTexture2D<float4> _EnergySum;
uint _EnergySample;
#if ENERGY_RECORD_REASONS
Texture2D<float4> _EnergyRays;
RWTexture2D<float4> _EnergyReasonA;
RWTexture2D<float4> _EnergyReasonB;
#endif
[numthreads(8,8,1)]
void AccumulateEnergyReferenceCS(uint3 id:SV_DispatchThreadID) {
    if(any(id.xy>=uint2(120,136)))return;
    float4 v=_EnergyInput.Load(int3(id.xy,0));
    v=float4(v.rgb*v.a,v.a);
    _EnergySum[id.xy]=(_EnergySample==0?0.0:_EnergySum[id.xy])+v;
#if ENERGY_RECORD_REASONS
    float4 a=0.0,b=0.0;
    for(uint ray=0;ray<8;++ray) {
        uint flags=uint(floor(_EnergyRays.Load(int3(id.x*8+ray,id.y,0)).w));
        uint source=flags&7u;
        if(source==1u || source==2u || source==7u || (flags&64u)!=0u)b.y+=1.0;
        else if((flags&256u)!=0u)a.z+=1.0;
        else if(source==4u){if((flags&8u)!=0u)a.y+=1.0;else a.x+=1.0;}
        else if(source==3u)a.w+=1.0;
        else if(source==5u)b.x+=1.0;
        else b.z+=1.0;
        b.w+=1.0;
    }
    _EnergyReasonA[id.xy]=(_EnergySample==0?0.0:_EnergyReasonA[id.xy])+a;
    _EnergyReasonB[id.xy]=(_EnergySample==0?0.0:_EnergyReasonB[id.xy])+b;
#endif
}
'''


def build(output,sequence,reference_seed,sets=16,measured=128,record_reasons=False):
    assert sets in (1,16,64) and measured in (64,128)
    source,shaders=replay_build(output,"stable-unclamped",sequence,measured)
    helper=Path(__file__).with_name("ScreenProbeSameFrameEnergy.cs.txt").read_text(encoding="utf-8-sig")
    helper=helper.replace("__SETS__",str(sets)).replace("__REFERENCE_SEED__",str(reference_seed))
    helper=helper.replace("__REASONS__",str(record_reasons).lower())

    def replace(old,new):
        nonlocal source
        assert source.count(old)==1,old[:100]
        source=source.replace(old,new,1)

    split=helper.index('    RenderTexture EnergyTexture(')
    replace('    string privateMethod=',helper[:split]+'\n    string privateMethod=')
    replace('    object PrivateField(string name)',helper[split:]+'\n    object PrivateField(string name)')
    replace('gi.traceMode.Override(SdfDiffuseTraceMode.Hybrid);SetupPrivateReplay();',
            'gi.traceMode.Override(SdfDiffuseTraceMode.Hybrid);SetupPrivateReplay();SetupEnergy();')
    replace('        Read(Resolve("m_HitLighting"), "raw");',
            '        ReplayEnergy(context);\n        Read(Resolve("m_HitLighting"), "raw");')
    replace('        if(privatePendingPairs.Count!=0)',
            '        if(energyPending.Count!=0)throw new InvalidOperationException("Unpaired energy AA readbacks.");\n        if(privatePendingPairs.Count!=0)')
    replace('privatePrograms, method=privateMethod,',
            'privatePrograms, energyAaPairs,energySets,energySeed,method=privateMethod,')
    marker='        var cmd=context.cmd;var raw=(CommandBuffer)wrapped.GetValue(cmd);int slot=frame&1,previous=1-slot;'
    replace(marker,marker+'\n        if(frame>=warmup)ReadEnergy(context,PrivateResolve("m_RawResolved"),"resolved","source",pixel:true);')
    replace('        ReadPrivate(context,input,"final",',
            '        if(frame>=warmup)ReadEnergy(context,input,"final",path==0?"baseline":"candidate",pixel:true);\n        ReadPrivate(context,input,"final",')
    shaders['energy-accumulator.compute.txt']=f'#define ENERGY_RECORD_REASONS {int(record_reasons)}\n'+ACCUMULATOR
    return source,shaders


if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument("output",type=Path)
    parser.add_argument("--sequence",type=int,default=1733);parser.add_argument("--reference-seed",type=int,default=7919)
    parser.add_argument("--sets",type=int,choices=(1,16,64),default=16)
    parser.add_argument("--measured",type=int,choices=(64,128),default=128)
    parser.add_argument("--record-reference-reasons",action="store_true")
    args=parser.parse_args();output=args.output.resolve()
    source,shaders=build(output,args.sequence,args.reference_seed,args.sets,args.measured,args.record_reference_reasons)
    output.mkdir(parents=True,exist_ok=False)
    (output/"capture-script.cs").write_text(source,encoding="utf-8")
    for name,body in shaders.items():(output/name).write_text(body,encoding="utf-8")
    protocol=dict(schema="screen-probe-same-frame-energy-protocol.v1",method="stable-unclamped",sequence=args.sequence,
        referenceSeed=args.reference_seed,setsPerFrame=args.sets,warmup=1024,measuredFrames=args.measured,
        recordReasons=args.record_reference_reasons,
        recordGpuBounds=args.record_reference_reasons,
        spacing=16,rays=8,payloadBudget=256*1024**2,metadataWitnesses=True,feedbackPublicationWitnesses=True,
        targetRegions=dict(ceiling=[46,59,28],floor=[54,8,12],back=[68,38,8],edge=[55,26,10]),
        files={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in output.iterdir()},
        gates=dict(nativeBaselineByteExact=True,ceilingVarianceReductionPercent=5,meanChangeAbsPercent=1,
            addedMeanErrorPercentOfReference=1,addedReferenceMsePercent=1,minimumPointMeanReferenceCoverage=.99),
        note="Reference recomputes screen rays and SDF gather using the unchanged native shader and same-frame resources. Guiding is disabled only in private reference dispatches, then restored. Reference is incident E/pi before receiver albedo; it shares Surface Cache and SDF approximation and is not absolute Unreal truth.")
    (output/"protocol.json").write_text(json.dumps(protocol,indent=2)+"\n",encoding="utf-8")
    print(json.dumps(dict(root=str(output))))
