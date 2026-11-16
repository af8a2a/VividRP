"""Capture final-pixel GI and receiver geometry for the independent Cornell oracle."""
import argparse
import hashlib
import json
from pathlib import Path

from BuildScreenProbeNativeQuadratureHistory import build as native_build


REGIONS=dict(ceiling=(912,970,96,48),floor=(912,130,96,64),back=(1100,600,96,64),edge=(880,414,160,128))


def targets():
    return [dict(region=name,x=x,y=y) for name,(left,bottom,width,height) in REGIONS.items()
        for y in range(bottom+2,bottom+height,4) for x in range(left+2,left+width,4)]


GEOMETRY_SHADER='''#pragma kernel CaptureOraclePixelsCS
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/SdfScreenProbeCommon.hlsl"
StructuredBuffer<uint> _OraclePixels;
RWTexture2D<float4> _OracleGeometry;
RWTexture2D<float4> _OracleNormals;
uint _OracleCount;
[numthreads(64,1,1)]void CaptureOraclePixelsCS(uint3 id:SV_DispatchThreadID) {
    if(id.x>=_OracleCount)return;
    uint code=_OraclePixels[id.x];uint2 pixel=uint2(code%uint(_SdfDiffuseScreenSize.x),code/uint(_SdfDiffuseScreenSize.x));
    float3 position,normal;bool valid=LoadReceiver(pixel,position,normal);
    _OracleGeometry[id.xy]=valid?float4(position,PackProbeNormal(normal)):float4(0,0,0,-1);
    _OracleNormals[id.xy]=valid?float4(normal,1):0;
}
'''


def build(output,sequence=1733,covered_wavelet=False,symmetric_coverage=False,wavelet_witness=False,energy_moments=False,motion=False,conservative_flux=False,wavelet_factorial=False):
    assert covered_wavelet or not (symmetric_coverage or wavelet_witness)
    assert not energy_moments or (covered_wavelet and not wavelet_witness),'Conditional-RGB witness requires matching moment units'
    assert not conservative_flux or (energy_moments and covered_wavelet and not wavelet_witness),'Flux requires consumed-energy moments and wavelets'
    assert not wavelet_factorial or (conservative_flux and not motion),'Factorial wavelets require static conservative-flux inputs'
    source,shaders=native_build(output,sequence,motion)
    helper=Path(__file__).with_name('ScreenProbePixelOracleCapture.cs.txt').read_text(encoding='utf-8-sig')
    def replace(old,new):
        nonlocal source
        assert source.count(old)==1,old[:100];source=source.replace(old,new,1)
    split=helper.index('    void SetupOraclePixels(')
    replace('    string privateMethod=',helper[:split]+'\n    string privateMethod=')
    replace('    object PrivateField(string name)',helper[split:]+'\n    object PrivateField(string name)')
    replace('SetupPrivateReplay();SetupQuadratureNormalization();','SetupPrivateReplay();SetupQuadratureNormalization();SetupOraclePixels();')
    replace('        Read(Resolve("m_HitLighting"), "raw");','        CaptureOraclePixels(context);\n        Read(Resolve("m_HitLighting"), "raw");')
    replace('        ReadPrivate(context,input,"final",','        if(frame>=warmup)ReadOraclePixels(context,input,"final",path==0?"baseline":"candidate",true);\n        ReadPrivate(context,input,"final",')
    replace('            CleanupPrivateReplay();CleanupQuadrature();CleanupComponents();','            oraclePixelMap?.Dispose();CleanupPrivateReplay();CleanupQuadrature();CleanupComponents();')
    replace('privatePrograms,privateAaPairs,','privatePrograms,oraclePrograms,privateAaPairs,')
    if not motion:replace('320L*1024*1024','384L*1024*1024')
    # Guard before constructing a temporary profile, including failed startup.
    replace('    var roots = scene.GetRootGameObjects();','    if(!EditorApplication.isPlaying || !EditorApplication.isPaused)throw new InvalidOperationException("Paused PlayMode required before allocating the oracle capture.");\n    var roots = scene.GetRootGameObjects();')
    shaders['oracle-geometry.compute.txt']=GEOMETRY_SHADER
    if covered_wavelet:
        from ScreenProbeCoveredWavelet import install
        source,shaders=install(source,shaders,symmetric_coverage,wavelet_witness)
    if energy_moments:
        from ScreenProbeEnergyMoments import install
        source,shaders=install(source,shaders)
    if conservative_flux:
        from ScreenProbeConservativeFlux import install
        source,shaders=install(source,shaders)
    if wavelet_factorial:
        from ScreenProbeWaveletFactorial import install
        source,shaders=install(source,shaders)
    return source,shaders


def snapshot_capture(output,shaders):
    """Pin executable sources before the caller can launch the generated capture."""
    from VerifyGiSourceSnapshot import create
    folder=Path(__file__).resolve().parent
    names=['BuildScreenProbePixelOracleCapture.py','ScreenProbePixelOracleCapture.cs.txt']
    for shader,module in (('covered-wavelet.compute.txt','ScreenProbeCoveredWavelet.py'),
                          ('energy-temporal.compute.txt','ScreenProbeEnergyMoments.py'),
                          ('flux-wavelet.compute.txt','ScreenProbeConservativeFlux.py')):
        if shader in shaders:names.append(module)
    if 'flux-wavelet.compute.txt' in shaders:names.append('ScreenProbeConservativeFlux.cs.txt')
    if 'flux-rtz.compute.txt' in shaders:names+=['ScreenProbeWaveletFactorial.py','ScreenProbeWaveletFactorial.cs.txt']
    workspace=folder.parents[1]
    create(output/'source-before-capture.json',workspace,
        [(folder/name).relative_to(workspace).as_posix() for name in names],
        [output/name for name in ('protocol.json','targets.json',*shaders)],
        [output/'capture-script.cs'])


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('output',type=Path);p.add_argument('--sequence',type=int,default=1733)
    p.add_argument('--covered-wavelet',action='store_true')
    p.add_argument('--symmetric-coverage',action='store_true');p.add_argument('--wavelet-witness',action='store_true')
    p.add_argument('--energy-moments',action='store_true')
    p.add_argument('--conservative-flux',action='store_true')
    p.add_argument('--wavelet-factorial',action='store_true')
    p.add_argument('--motion',action='store_true')
    a=p.parse_args();out=a.output.resolve();source,shaders=build(out,a.sequence,a.covered_wavelet,a.symmetric_coverage,a.wavelet_witness,a.energy_moments,a.motion,a.conservative_flux,a.wavelet_factorial)
    out.mkdir(parents=True,exist_ok=False);(out/'capture-script.cs').write_text(source,encoding='utf-8')
    for name,body in shaders.items():(out/name).write_text(body,encoding='utf-8')
    points=targets();(out/'targets.json').write_text(json.dumps(points,indent=2)+'\n')
    protocol=dict(schema='screen-probe-native-quadrature-history-protocol.v1',corrected=False,guided=True,sequence=a.sequence,
        warmup=1024,measuredFrames=128,spacing=16,rays=8,payloadBudget=384*1024**2,historyEpoch=True,finalStatistics=True,
        quadrature=True,independentPixelHistories=True,nativeSide='candidate',irradiancePlanes=7,motion=a.motion,regions=['edge'] if a.motion else list(REGIONS),
        aaFrames=[0,1,2,1023,*range(1024,1152)],pixelOracle=True,oracleTargetCount=len(points),coverageWeightedWavelet=a.covered_wavelet,
        symmetricCoverage=a.symmetric_coverage,coverageWaveletWitness=a.wavelet_witness,
        consumedEnergyMoments=a.energy_moments,
        conservativeFluxWavelet=a.conservative_flux,
        files={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in out.iterdir()},
        gates=dict(nativeCopiedResolveByteExact=True,nativePrivateHistoryByteExact=True,meanChangeAbsPercent=1,
            ceilingVarianceReductionPercent=5,otherVarianceIncreasePercent=2,frameDifferenceRmsIncreasePercent=2,
            analyticPrimaryPositionMaxMeters=.00002,analyticPrimaryNormalMinCosine=.9999,
            referencePathsPerPixel=16384,addedAbsoluteMeanErrorPercentOfReference=.2,addedMeanMapMsePercent=1),
        note='Actual off-probe final pixels. GPU geometry/decoded normals and live effective Lambert fixture exported independently of transport. Original native trace unchanged. Oracle is a separate exact box/quad Lambert model, not an SDF replay or OpenPBR/Unreal equivalence claim.')
    if a.motion:
        protocol.update(trajectory=dict(period=128,lateral=.28,forward=.06,yawDegrees=-7),motionRegion=[880,414,160,128])
        protocol['gates'].update(disocclusionP99IncreasePercent=5,disocclusionMaxIncreasePercent=5,newlyExposedOldAgeFractionIncrease=.01)
    if a.conservative_flux:
        protocol['gates'].update(fluxMassRoundingBound=True,fluxFp32RelativeTolerance=2e-6,fluxCoverageByteExact=True)
        protocol['fluxMassFrames']=[1024,1151]
        protocol['performanceQualified']=False
        protocol['note']+=' Symmetric fixed-normalization flux overwrites each native wavelet result before its next consumer. Original native wavelet dispatch remains; quality experiment only, unsuitable for GPU timing.'
    if a.wavelet_factorial:
        from ScreenProbeWaveletFactorial import VARIANTS
        protocol.update(waveletFactorial=True,waveletFactorialVariants=list(VARIANTS),payloadBudget=512*1024**2)
        protocol['note']+=' Same-input factorial replay at measured frames only: matched symmetric bilateral weights, fixed/row normalization and explicit RNE/RTZ; old energy-row implicit/RNE bridge. No counterfactual history writes or GPU-cost claim.'
    (out/'protocol.json').write_text(json.dumps(protocol,indent=2)+'\n')
    snapshot_capture(out,shaders)
    print(json.dumps(dict(root=str(out),targets=len(points))))
