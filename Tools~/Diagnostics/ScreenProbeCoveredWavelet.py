"""Owned experiment: average consumed diffuse energy with conditional-RGB guides."""
from pathlib import Path

from BuildScreenProbeSameFrameAtrous import shader_source


def covered_source(body,symmetric=False):
    replacements={
        'float3 sum = center.rgb * totalWeight;':
            'float3 sum = (center.rgb * abs(center.a)) * totalWeight;',
        'sum += weight * lighting.rgb;':
            'sum += weight * (lighting.rgb * abs(lighting.a));',
        'center.rgb = sum / totalWeight;':
            '// Preserve the interface coverage and HDR limit; filter the quantity consumed by lighting.\n'
            '            center.rgb = min((sum / totalWeight) / abs(center.a), 65000.0);',
    }
    for before,after in replacements.items():
        assert body.count(before)==1,before
        body=body.replace(before,after,1)
    if symmetric:
        before='float coverageWeight = saturate(abs(lighting.a) * inverseCoverage);'
        assert body.count(before)==1
        body=body.replace(before,'// Reciprocal confidence: swapping center/tap preserves the coverage weight.\n'
            '                float coverageWeight = min(abs(lighting.a), abs(center.a))\n'
            '                    / max(max(abs(lighting.a), abs(center.a)), 1e-4);',1)
    return body


def install(source,shaders,symmetric=False,witness=False):
    body=covered_source((Path(__file__).parents[2]/'Shaders/Core/Private/GlobalIllumination/SdfDiffuseAtrous.hlsl').read_text(encoding='utf-8-sig'),symmetric)
    include='#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/SdfDiffuseAtrous.hlsl"'
    assert shaders['quadrature-gather.compute.txt'].count(include)==1
    shaders['quadrature-gather.compute.txt']=shaders['quadrature-gather.compute.txt'].replace(include,body,1)
    shaders['covered-wavelet.compute.txt']=shader_source(body)
    old='    ComputeShader privateTemporal=null,privateTemporalCandidate=null,privateWavelet=null,privateFeedback=null;'
    assert source.count(old)==1
    source=source.replace(old,old+'\n    ComputeShader privateCoveredWavelet=null;',1)
    old='        privateWavelet=PrivateShader("wavelet.compute.txt",new[]{"FilterSdfDiffuseAtrous","FilterSdfDiffuseAtrousFinal"});'
    assert source.count(old)==1
    source=source.replace(old,old+'\n        privateCoveredWavelet=PrivateShader("covered-wavelet.compute.txt",new[]{"FilterSdfDiffuseAtrous","FilterSdfDiffuseAtrousFinal"});',1)
    start=source.index('    void ReplayPrivateWavelets(');end=source.index('    Action<ComputePassContext,int,Texture> privateTemporalHook=',start)
    block=source[start:end].replace('privateWavelet','waveletShader')
    old='        var cmd=context.cmd;'
    assert block.count(old)==1
    block=block.replace(old,'        var waveletShader=path==1?privateCoveredWavelet:privateWavelet;\n'+old,1)
    source=source[:start]+block+source[end:]
    if witness:
        source=add_witness(source)
    return source,shaders


def add_witness(source):
    def replace(before,after):
        nonlocal source
        assert source.count(before)==1,before[:100]
        source=source.replace(before,after,1)
    replace('void ReadPrivate(ComputePassContext context,Texture texture,string role,string side,bool pair,bool save)',
        'void ReadPrivate(ComputePassContext context,Texture texture,string role,string side,bool pair,bool save,bool hashOnly=false)')
    replace('bool full=pair && (f<3 || f==warmup-1 || f>=warmup);',
        'bool full=hashOnly || (pair && (f<3 || f==warmup-1 || f>=warmup));')
    replace('PairPrivate(f+"/"+role+"/full",side,hash,bytes,format,fullWidth,fullHeight,f);',
        'if(pair)PairPrivate(f+"/"+role+"/full",side,hash,bytes,format,fullWidth,fullHeight,f);')
    replace('void ReplayPrivateWavelets(ComputePassContext context,int path)',
        'void ReplayPrivateWavelets(ComputePassContext context,int path,bool conditionalWitness=false)')
    replace('var waveletShader=path==1?privateCoveredWavelet:privateWavelet;',
        'var waveletShader=path==1 && !conditionalWitness?privateCoveredWavelet:privateWavelet;\n'
        '        string waveletSide=conditionalWitness?"quadrature-reference":path==0?"baseline":"candidate";\n'
        '        if(path==1 && frame>=warmup) {\n'
        '            ReadPrivate(context,privateLighting[1],"witness-input",waveletSide,false,false,true);\n'
        '            ReadPrivate(context,privateMoments[1,frame&1],"witness-moments",waveletSide,false,false,true);\n'
        '        }')
    replace('ReadOraclePixels(context,input,"final",path==0?"baseline":"candidate",true);',
        'ReadOraclePixels(context,input,"final",waveletSide,true);')
    replace('ReadPrivate(context,input,"final",path==0?"baseline":"candidate",path==1,frame>=warmup || frame==0);',
        'ReadPrivate(context,input,"final",waveletSide,path==1 && !conditionalWitness,frame>=warmup || frame==0);')
    replace('ReplayPrivateWavelets(context,0);ReplayPrivateWavelets(context,1);',
        'ReplayPrivateWavelets(context,0);ReplayPrivateWavelets(context,1);ReplayPrivateWavelets(context,1,true);')
    return source
