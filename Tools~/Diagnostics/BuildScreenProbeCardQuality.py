"""Compare native card-coverage traces without changing sampling or reconstruction."""
import argparse
import hashlib
import json
from pathlib import Path
from BuildScreenProbePixelOracleCapture import build, targets, snapshot_capture


def trace_source(workspace, before=None):
    folder=workspace/'Shaders/Core/Private/GlobalIllumination'
    trace=(before/'before-SdfScreenProbeTracing.compute' if before else folder/'SdfScreenProbeTracing.compute').read_text(encoding='utf-8-sig')
    surface=(before/'before-SdfSurfaceCache.hlsl' if before else workspace/'Shaders/Core/Public/GPUDriven/SdfSurfaceCache.hlsl').read_text(encoding='utf-8-sig')
    if before is None:
        candidate=Path(__file__).with_name('ScreenProbePlanarCardCandidate.hlsl.txt').read_text()
        assert 'bool VividTrySampleSdfSurfaceAlongRay' not in surface
        surface=surface.replace('void VividGetSdfCardProjection',candidate+'void VividGetSdfCardProjection',1)
        call='VividTrySampleSdfSurface(hit, hitPosition,'
        assert trace.count(call)==1
        trace=trace.replace(call,'VividTrySampleSdfSurfaceAlongRay(hit, direction, hitPosition,')
    for name in ('MeshSdfTrace.hlsl','SdfMaterialSidedness.hlsl'):
        surface=surface.replace(f'#include "{name}"',f'#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/{name}"')
    for line in trace.splitlines():
        if line.startswith('#include "'):
            name=line.split('"')[1]
            trace=trace.replace(line,f'#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/{name}"',1)
    pos=trace.index('#include ')
    return trace[:pos]+surface+'\n'+trace[pos:]


def build_capture(output,before=None,sequence=1733,production=False):
    source,shaders=build(output,sequence)
    # Both independent pixel histories reconstruct the ordinary production
    # Resolve. Quadrature remains disabled in the native and replay programs.
    source=source.replace('67108864','0').replace('134217728','0')
    marker='SetupQuadratureNormalization();SetupOraclePixels();'
    assert source.count(marker)==1
    source=source.replace(marker,marker+'SetupExperimentalTracing(privatePass);')
    guard='if(!EditorUtility.IsPersistent((ComputeShader)type.GetField("m_TraceCompute",inst).GetValue(qualityPass)))throw new InvalidOperationException("Original persistent trace required.");'
    assert source.count(guard)==1
    source=source.replace(guard,'if(!EditorUtility.IsPersistent(experimentalOriginalTrace) || !object.ReferenceEquals(type.GetField("m_TraceCompute",inst).GetValue(qualityPass),experimentalTrace))throw new InvalidOperationException("Card trace ownership changed.");')
    shaders['candidate-tracing.compute.txt']=trace_source(Path(__file__).resolve().parents[2],before)
    if production:
        replacements={
            'experimentalTrace=(ComputeShader)create.Invoke(null,new object[]{System.IO.File.ReadAllText(output+"/candidate-tracing.compute.txt")});':
                'experimentalTrace=experimentalOriginalTrace;',
            'if(EditorUtility.IsPersistent(experimentalTrace))throw new InvalidOperationException("Experimental tracing must remain in memory.");':
                'if(!EditorUtility.IsPersistent(experimentalTrace))throw new InvalidOperationException("Persistent production trace required.");',
            'field.SetValue(owner,experimentalTrace);experimentalTraceInstalled=true;':'// Production trace remains installed.',
            'if(experimentalTrace!=null)UnityEngine.Object.DestroyImmediate(experimentalTrace);':
                'if(experimentalTrace!=null && !object.ReferenceEquals(experimentalTrace,experimentalOriginalTrace))UnityEngine.Object.DestroyImmediate(experimentalTrace);',
            'field.SetValue(qualityPass,quadratureShader);quadratureInstalled=true;':
                'if(!EditorUtility.IsPersistent(quadratureOriginal))throw new InvalidOperationException("Persistent production Gather required.");',
            'if(!object.ReferenceEquals(shader,quadratureShader) || kernel!=shader.FindKernel("ScreenProbeIntegrateCS")':
                'if(!object.ReferenceEquals(shader,quadratureOriginal) || !EditorUtility.IsPersistent(shader) || kernel!=shader.FindKernel("ScreenProbeIntegrateCS")',
            'compile.Invoke(null,new object[]{quadratureShader,kernel,Array.Empty<string>(),GraphicsDeviceType.Direct3D12,BuildTarget.StandaloneWindows64})':
                'compile.Invoke(null,new object[]{quadratureOriginal,kernel,Array.Empty<string>(),GraphicsDeviceType.Direct3D12,BuildTarget.StandaloneWindows64})',
        }
        for old,new in replacements.items():
            assert source.count(old)==1,old
            source=source.replace(old,new,1)
        del shaders['candidate-tracing.compute.txt']
    return source,shaders


def generate(output,before=None,sequence=1733,production=False):
    source,shaders=build_capture(output,before,sequence,production)
    output.mkdir(parents=True,exist_ok=False)
    (output/'capture-script.cs').write_text(source,encoding='utf-8')
    for name,body in shaders.items():(output/name).write_text(body,encoding='utf-8')
    (output/'targets.json').write_text(json.dumps(targets(),indent=2)+'\n')
    protocol=dict(schema='screen-probe-card-quality.v1',sequence=sequence,spacing=16,rays=8,warmup=1024,measuredFrames=128,
        payloadBudget=384*1024**2,nativeSide='candidate',quadrature=False,motion=False,independentPixelHistories=True,
        irradiancePlanes=7,cardCoverageCandidate=before is None and not production,productionShaders=production,historyEpoch=True,finalStatistics=True,
        gates=dict(inputGeometryExact=True,nativePrivateHistoryByteExact=True,addedMeanMapMsePercent=1,
            addedAbsoluteMeanErrorPercentOfReference=.2,otherVarianceIncreasePercent=2,frameDifferenceRmsIncreasePercent=2),
        note='Original4cm/192step trace budget,16px/8rays,ordinary Resolve,anchor512/pixel32/four wavelets. Only card query changes. No temporal-variance-only acceptance.')
    protocol['builderFiles']={name:hashlib.sha256(Path(__file__).with_name(name).read_bytes()).hexdigest()
        for name in ('BuildScreenProbeCardQuality.py','ScreenProbePlanarCardCandidate.hlsl.txt')}
    (output/'protocol.json').write_text(json.dumps(protocol,indent=2)+'\n')
    snapshot_capture(output,shaders)
    return dict(root=str(output),candidate=before is None and not production,productionShaders=production)


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('output',type=Path);p.add_argument('--before',type=Path);p.add_argument('--sequence',type=int,default=1733)
    p.add_argument('--production',action='store_true')
    a=p.parse_args();print(json.dumps(generate(a.output.resolve(),a.before,a.sequence,a.production)))
