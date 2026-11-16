"""Same-input wavelets: matched bilateral weights, normalization and half rounding."""
from pathlib import Path


VARIANTS=('energy-row-native','energy-row-rne','symmetric-row-rne','symmetric-row-rtz','flux-rtz')
FLUX_KERNELS=('PrepareScreenProbeFluxCS','FilterScreenProbeFluxCS','FilterScreenProbeFluxFinalCS')
ROW_KERNELS=('FilterSdfDiffuseAtrous','FilterSdfDiffuseAtrousFinal')
PROGRAMS={'energy-row-rne.compute.txt':ROW_KERNELS,
          **{name+'.compute.txt':FLUX_KERNELS for name in ('symmetric-row-rne','symmetric-row-rtz','flux-rtz')}}


def qualify_inputs(capture,protocol,rows):
    assert protocol['waveletFactorial'] and protocol['waveletFactorialVariants']==list(VARIANTS)
    frames=range(protocol['warmup'],protocol['warmup']+protocol['measuredFrames'])
    pairs=0
    for frame in frames:
        for role in ('witness-input-full','witness-moments-full'):
            base=rows[frame,role,'candidate']
            for side in VARIANTS:
                other=rows[frame,role,side]
                assert all(base[k]==other[k] for k in ('sha256','width','height','format')),'Factorial inputs differ'
                pairs+=1
    guides=capture['factorialGuideHashes']
    indexed={(q['frame'],q['side']):q for q in guides}
    endpoints=(protocol['warmup'],protocol['warmup']+protocol['measuredFrames']-1)
    sides=('candidate','symmetric-row-rne','symmetric-row-rtz','flux-rtz')
    assert len(guides)==len(indexed)==len(endpoints)*len(sides),'Missing or duplicate factorial guides'
    for frame in endpoints:
        values=[indexed[frame,side] for side in sides]
        assert all((q['format'],q['width'],q['height'])==('R32G32B32A32_SFloat',1920,1080) for q in values)
        assert len({q['sha256'] for q in values})==1,'First-scale factorial guides differ'
    return dict(fullInputPairs=pairs,firstScaleGuideHashes=len(guides),firstScaleGuidesByteExact=True)

ROUND_ZERO='''float4 SdfFactorialRoundZeroHalf(float4 value)
{
    float4 magnitude=min(abs(value),65504.0);
    uint4 lower=f32tof16(magnitude);
    lower-=uint4(f16tof32(lower)>magnitude);
    return asfloat(asuint(f16tof32(lower))|(asuint(value)&0x80000000u));
}
'''


def row_normalized_flux(source):
    replacements={
        'center.rgb=min(max(centerEnergy+flux,0.0)/abs(center.a),65000.0);':
            'float selfWeight=36.0/256.0;\n'
            '        float denominator=selfWeight+coefficientSum;\n'
            '        center.rgb=min(max(centerEnergy+flux/denominator,0.0)/abs(center.a),65000.0);',
        'filteredVariance=max(0.25*summary.x,(1.0-coefficientSum)*(1.0-coefficientSum)*summary.x+varianceNumerator);':
            'filteredVariance=max(0.25*summary.x,(selfWeight*selfWeight*summary.x+varianceNumerator)/(denominator*denominator));',
    }
    for before,after in replacements.items():
        assert source.count(before)==1,before
        source=source.replace(before,after,1)
    return source


def round_zero(source):
    before='_SdfDiffuseOutput[pixel]=SdfRoundToNearestHalf(center);'
    assert source.count(before)==1
    source=source.replace(before,'_SdfDiffuseOutput[pixel]=SdfFactorialRoundZeroHalf(center);',1)
    before='[numthreads(8,8,1)]\nvoid PrepareScreenProbeFluxCS'
    assert source.count(before)==1
    return source.replace(before,ROUND_ZERO+'\n'+before,1)


def install(source,shaders):
    shaders['symmetric-row-rne.compute.txt']=row_normalized_flux(shaders['flux-wavelet.compute.txt'])
    shaders['symmetric-row-rtz.compute.txt']=round_zero(shaders['symmetric-row-rne.compute.txt'])
    shaders['flux-rtz.compute.txt']=round_zero(shaders['flux-wavelet.compute.txt'])
    before='_SdfDiffuseOutput[pixel] = center;'
    assert shaders['covered-wavelet.compute.txt'].count(before)==1
    shaders['energy-row-rne.compute.txt']=shaders['covered-wavelet.compute.txt'].replace(before,'_SdfDiffuseOutput[pixel] = SdfRoundToNearestHalf(center);',1)
    helper=Path(__file__).with_name('ScreenProbeWaveletFactorial.cs.txt').read_text(encoding='utf-8-sig')
    split=helper.index('    void SetupFactorial(')
    def replace(before,after):
        nonlocal source
        assert source.count(before)==1,before[:110]
        source=source.replace(before,after,1)
    replace('    string privateMethod=',helper[:split]+'\n    string privateMethod=')
    replace('    object PrivateField(string name)',helper[split:]+'\n    object PrivateField(string name)')
    replace('SetupEnergyMoments();SetupFlux();','SetupEnergyMoments();SetupFlux();SetupFactorial();')
    replace('fluxPrograms,fluxMass,privateAaPairs,','fluxPrograms,fluxMass,factorialPrograms,factorialGuideHashes,privateAaPairs,')
    replace('384L*1024*1024','512L*1024*1024')
    replace('void ReadPrivate(ComputePassContext context,Texture texture,string role,string side,bool pair,bool save)',
            'void ReadPrivate(ComputePassContext context,Texture texture,string role,string side,bool pair,bool save,bool hashOnly=false)')
    replace('bool full=pair && (f<3 || f==warmup-1 || f>=warmup);','bool full=hashOnly || (pair && (f<3 || f==warmup-1 || f>=warmup));')
    replace('PairPrivate(f+"/"+role+"/full",side,hash,bytes,format,fullWidth,fullHeight,f);',
            'if(pair)PairPrivate(f+"/"+role+"/full",side,hash,bytes,format,fullWidth,fullHeight,f);')
    replace('void ReplayPrivateWavelets(ComputePassContext context,int path)',
            'void ReplayPrivateWavelets(ComputePassContext context,int path,string waveletVariant=null)')
    replace('var waveletShader=path==1?privateCoveredWavelet:privateWavelet;',
            'var waveletShader=waveletVariant=="energy-row-rne"?factorialEnergyRne:path==1?privateCoveredWavelet:privateWavelet;\n'
            '        string waveletSide=waveletVariant??(path==0?"baseline":"candidate");\n'
            '        bool useFlux=path==1 && (waveletVariant==null || waveletVariant=="flux-rtz" || waveletVariant.StartsWith("symmetric-row-",StringComparison.Ordinal));\n'
            '        ComputeShader fluxOverride=waveletVariant=="flux-rtz"?factorialFluxRtz:waveletVariant=="symmetric-row-rne"?factorialRowRne:waveletVariant=="symmetric-row-rtz"?factorialRowRtz:null;\n'
            '        if(path==1 && frame>=warmup) {\n'
            '            ReadPrivate(context,privateLighting[1],"witness-input",waveletSide,false,false,true);\n'
            '            ReadPrivate(context,privateMoments[1,frame&1],"witness-moments",waveletSide,false,false,true);\n'
            '        }')
    replace('if(path==1)RecordFlux(context,iteration,input,privateWaveColors[slot],variance,last?null:privateWaveVariance[slot],privateMoments[1,frame&1],false);',
            'if(useFlux)RecordFlux(context,iteration,input,privateWaveColors[slot],variance,last?null:privateWaveVariance[slot],privateMoments[1,frame&1],false,fluxOverride,waveletSide);')
    replace('ReadOraclePixels(context,input,"final",path==0?"baseline":"candidate",true);',
            'ReadOraclePixels(context,input,"final",waveletSide,true);')
    replace('ReadPrivate(context,input,"final",path==0?"baseline":"candidate",path==1,frame>=warmup || frame==0);',
            'ReadPrivate(context,input,"final",waveletSide,path==1 && waveletVariant==null,frame>=warmup || frame==0);')
    replace('ReplayPrivateWavelets(context,0);ReplayPrivateWavelets(context,1);',
            'ReplayPrivateWavelets(context,0);ReplayPrivateWavelets(context,1);\n'
            '        if(frame>=warmup)foreach(string variant in factorialVariants)ReplayPrivateWavelets(context,1,variant);')
    replace('Texture moments,bool native) {','Texture moments,bool native,ComputeShader overrideShader=null,string guideSide=null) {')
    start=source.index('    void RecordFlux(');end=source.index('    object PrivateField(string name)',start)
    block=source[start:end];assert block.count('fluxShader')==16,block.count('fluxShader')
    block=block.replace('fluxShader','shader')
    before='        var cmd=context.cmd;'
    assert block.count(before)==1
    block=block.replace(before,'        var shader=overrideShader??fluxShader;\n'+before,1)
    before='            cmd.DispatchCompute(shader,kernel,240,135,1);'
    assert block.count(before)==1
    block=block.replace(before,before+'\n'
        '            if(name=="PrepareScreenProbeFluxCS" && iteration==0 && guideSide!=null && (frame==warmup || frame==warmup+count-1))ReadFactorialGuides(raw,guideSide);',1)
    source=source[:start]+block+source[end:]
    return source,shaders
