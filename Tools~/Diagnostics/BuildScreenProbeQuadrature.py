"""Build an owned Gather prototype and same-frame quadrature counterfactual."""
import argparse
import hashlib
import json
from pathlib import Path
import re

from BuildScreenProbeResolveComponents import build as component_build


def shader_source():
    folder=Path(__file__).resolve().parent;ws=folder.parents[1]
    source=(ws/'Shaders/Core/Private/GlobalIllumination/SdfScreenProbeGather.compute').read_text(encoding='utf-8-sig')
    helper=(folder/'ScreenProbeQuadrature.hlsl.txt').read_text(encoding='utf-8-sig')
    split=helper.index('bool CanUseSdfProbeQuadrature(')
    marker='float2 GetScreenProbeInterpolationPixel('
    assert source.count(marker)==1;source=source.replace(marker,helper[:split]+'\n'+marker,1)
    marker='    float2 noise = (float2(seed & 65535u, seed >> 16u) + 0.5) / 32768.0 - 1.0;'
    # One occurrence belongs to the injected helper, the other to the original lookup.
    index=source.index(marker,source.index('float2 GetScreenProbeInterpolationPixel('))
    source=source[:index]+source[index:].replace(marker,'    float2 noise = SdfQuadratureNoise(seed);',1)
    marker='[numthreads(8, 8, 1)]\nvoid ScreenProbeIntegrateCS('
    assert source.count(marker)==1;source=source.replace(marker,helper[split:]+'\n'+marker,1)
    marker='    if (LoadReceiver(id.xy, position, normal))\n        lighting = IntegrateScreenProbeIrradiance(id.xy, size, position, normal);'
    assert source.count(marker)==1
    source=source.replace(marker,'''    if (LoadReceiver(id.xy, position, normal))
    {
        if ((_SdfGatherHistoryDiagnostics & 67108864u) != 0u && CanUseSdfProbeQuadrature(id.xy, size, position, normal))
        {
            float4 covered = 0.0;
            [unroll]
            for (int sample = 0; sample < 4; ++sample)
            {
                SdfQuadratureSlot = sample;
                float4 value = IntegrateScreenProbeIrradiance(id.xy, size, position, normal);
                covered += float4(value.rgb * value.a, value.a) * 0.25;
            }
            // Negative coverage is an Editor-only eligibility marker. Consumers
            // of this counterfactual use abs(a), never publish it as native history.
            lighting = float4(covered.rgb / max(covered.a, 1e-5), -covered.a);
        }
        else
            lighting = IntegrateScreenProbeIrradiance(id.xy, size, position, normal);
    }''',1)
    return re.sub(r'#include "([^"/]+)"',lambda m:'#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/'+m[1]+'"',source)


def build(output):
    source=component_build(output)
    helper=Path(__file__).with_name('ScreenProbeExperimentalGather.cs.txt').read_text(encoding='utf-8-sig')
    def replace(old,new):
        nonlocal source
        assert source.count(old)==1,old[:100];source=source.replace(old,new,1)
    replace('    ComputeShader componentCopy=null;',helper+'\n    ComputeShader componentCopy=null;')
    replace('SetupQuality();SetupComponents();','SetupQuality();SetupComponents();SetupQuadrature();')
    replace('            CleanupComponents();CleanupExperimentalTracing();','            CleanupQuadrature();CleanupComponents();CleanupExperimentalTracing();')
    replace('if(!EditorUtility.IsPersistent(shader) || kernel!=shader.FindKernel("ScreenProbeIntegrateCS")',
            'if(!object.ReferenceEquals(shader,quadratureShader) || kernel!=shader.FindKernel("ScreenProbeIntegrateCS")')
    replace('offset:componentWithoutJitter?0:840','offset:0')
    replace('                cmd.DispatchCompute(shader,kernel,240,135,1);','                cmd.SetComputeIntParam(shader,"_SdfGatherHistoryDiagnostics",32|(mode.aa?0:67108864));\n                cmd.DispatchCompute(shader,kernel,240,135,1);')
    replace('componentWithoutJitter?"unjittered":"uncalibrated"','"quadrature"')
    replace('            cmd.SetComputeTextureParam(shader,kernel,"_SdfDiffuseOutput",native);','            cmd.SetComputeTextureParam(shader,kernel,"_SdfDiffuseOutput",native);\n            cmd.SetComputeIntParam(shader,"_SdfGatherHistoryDiagnostics",32);')
    replace('componentPrograms,componentAaPairs,','componentPrograms,componentAaPairs,quadraturePrograms,')
    return source


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('output',type=Path);args=p.parse_args();out=args.output.resolve()
    source=build(out);out.mkdir(parents=True,exist_ok=False);(out/'capture-script.cs').write_text(source,encoding='utf-8')
    folder=Path(__file__).resolve().parent
    (out/'quadrature-gather.compute.txt').write_text(shader_source(),encoding='utf-8')
    (out/'copy-irradiance.compute.txt').write_text((folder/'SdfCopyIrradiancePlanes.compute.txt').read_text(encoding='utf-8-sig'),encoding='utf-8')
    (out/'depth-copy.compute.txt').write_text('#pragma kernel CopyDepthCS\nTexture2D<float> _Source;RWTexture2D<float> _Destination;\n[numthreads(8,8,1)]void CopyDepthCS(uint3 id:SV_DispatchThreadID){if(any(id.xy>=uint2(1920,1080)))return;_Destination[id.xy]=_Source.Load(int3(id.xy,0));}\n',encoding='utf-8')
    protocol=dict(schema='screen-probe-quadrature-protocol.v1',corrected=False,guided=True,sequence=1733,warmup=1024,measuredFrames=128,spacing=16,rays=8,payloadBudget=192*1024**2,historyEpoch=True,finalStatistics=True,resolveComponents=True,quadrature=True,
        files={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in out.iterdir()},
        gates=dict(nativeCopiedResolveByteExact=True,coverageExact=False,varianceClosureRelativeTolerance=1e-10,meanChangeAbsPercent=1,varianceReductionPercent=5,otherVarianceIncreasePercent=2),
        note='In-memory Gather prototype: native flags0 retains original lookup; only private Resolve flag67108864 enables four stratified lookups on mature strong-support planar regions. No added probe or repair rays. Negative private alpha marks eligibility; consume abs(a). No renderer history writes or default change.')
    (out/'protocol.json').write_text(json.dumps(protocol,indent=2)+'\n',encoding='utf-8');print(json.dumps(dict(root=str(out))))
