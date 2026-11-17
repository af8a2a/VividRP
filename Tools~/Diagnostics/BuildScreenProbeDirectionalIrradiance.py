"""Same-frame scalar-anchor versus directional-radiance irradiance comparison."""
import argparse
import json
import re
from pathlib import Path

from BuildScreenProbePixelOracleCapture import build, targets, snapshot_capture


def include_paths(source):
    return re.sub(r'#include "([^"/]+)"', lambda m:
        '#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/' + m[1] + '"', source)


def generate(output, sequence=1733, radiance_domain=False, spatial_passes=1, history_weight=.5, lumen_spatial=False, conservative_spatial=False, dither_history=False):
    frames = 128
    assert spatial_passes in (1, 3) and 0 <= history_weight <= 511/512
    workspace = Path(__file__).resolve().parents[2]
    folder = workspace / 'Shaders/Core/Private/GlobalIllumination'
    source, shaders = build(output, sequence)

    def replace(old, new):
        nonlocal source
        assert source.count(old) == 1, (old, source.count(old))
        source = source.replace(old, new, 1)

    # Both estimates see exactly the same trace, guide, radiance history and geometry.
    # Native pixels use the candidate; baseline and candidate pixel histories are separate.
    replace('hooks["EditorRecordProbeAnchorDiagnostics"].SetValue(null,false);',
            'hooks["EditorRecordProbeAnchorDiagnostics"].SetValue(null,true);')
    replace('|| (bool)type.GetField("m_RecordProbeAnchorDiagnostics",inst).GetValue(qualityPass)',
            '|| !(bool)type.GetField("m_RecordProbeAnchorDiagnostics",inst).GetValue(qualityPass)')
    replace('atlas.width!=840', 'atlas.width!=2640')
    replace('(offset:0,output:componentPre,aa:false)', '(offset:840,output:componentPre,aa:false)')

    filtering = (folder / 'SdfScreenProbeFiltering.compute').read_text(encoding='utf-8-sig')
    old = 'sh, rcp(max(covered, 1e-5)), covered > 0.0 ? anchorLighting.a : 0.0);'
    assert filtering.count(old) == 1
    filtering = filtering.replace(old, 'sh, rcp(max(covered, 1e-5)), saturate(covered / RaysPerProbe));')
    if radiance_domain:
        start = filtering.index('[numthreads(16, 1, 1)]\nvoid ScreenProbeTemporallyAccumulateTraceRadianceCS(')
        end = filtering.index('[numthreads(16, 1, 1)]\nvoid ScreenProbeFilterGatherTracesCS(', start)
        filtering = filtering[:start] + Path(__file__).with_name('ScreenProbeRadianceTemporal.hlsl.txt').read_text() + '\n' + filtering[end:]
        filtering = filtering.replace('float blend = min(0.5, (age - 1.0) / age);', f'float blend = min({history_weight}, (age - 1.0) / age);')
        if dither_history:
            marker = 'float PackRadianceHistoryAge('
            assert filtering.count(marker) == 1
            filtering = filtering.replace(marker, '''float3 QuantizeRadianceHistory(float3 radiance, uint2 address)
{
    float3 value = clamp(radiance, 0.0, 65504.0);
    uint3 lower = f32tof16(value);
    lower -= uint3(f16tof32(lower) > value);
    float3 low = f16tof32(lower);
    float3 high = f16tof32(min(lower + 1u, 0x7bffu));
    uint seed = HashProbe(address.x + 65537u * address.y + 0x9e3779b9u * _SdfDiffuseFrame);
    float random = float(seed >> 8u) * (1.0 / 16777216.0);
    float3 probability = (value - low) / max(high - low, 1e-30);
    return f16tof32(lower + uint3(random < probability));
}

''' + marker, 1)
            marker = 'RoundSdfHistory(geometry.w >= 0.0 ? float4(radiance, 1.0) : 0.0)'
            assert filtering.count(marker) == 1
            filtering = filtering.replace(marker, 'geometry.w >= 0.0 ? float4(QuantizeRadianceHistory(radiance, address), 1.0) : 0.0')
        old = '''        if (_SdfProbeAnchorDiagnostics != 0u)
            StoreProbeAnchorDiagnostics(id.xy + uint2(7u * grid.x, 0u), uint2(grid.x, 0u),
                sh, rcp(max(covered, 1e-5)), saturate(covered / RaysPerProbe));'''
        assert filtering.count(old) == 1
        filtering = filtering.replace(old, '''        if (_SdfProbeAnchorDiagnostics != 0u)
        {
            SdfProbeIrradiance directionalSh = sh;
            // This is the cosine integral of the SAME filtered radiance field,
            // not an independent raw/temporal/spatial scalar anchor.
            float3 projectionCorrection = anchor - EvaluateSdfProbeIrradiance(directionalSh, normal);
            directionalSh.r0.w += projectionCorrection.r;
            directionalSh.g0.w += projectionCorrection.g;
            directionalSh.b0.w += projectionCorrection.b;
            StoreProbeAnchorDiagnostics(id.xy + uint2(7u * grid.x, 0u), uint2(grid.x, 0u),
                directionalSh, rcp(max(covered, 1e-5)), saturate(covered / RaysPerProbe));
        }''')
    if lumen_spatial:
        assert radiance_domain
        start = filtering.index('[numthreads(16, 1, 1)]\nvoid ScreenProbeFilterGatherTracesCS(')
        end = filtering.index('float4 AccumulateLegacyDirectionalProbeAnchor(', start)
        filtering = filtering[:start] + Path(__file__).with_name('ScreenProbeRadianceSpatial.hlsl.txt').read_text() + '\n' + filtering[end:]
    if conservative_spatial:
        assert lumen_spatial
        start = filtering.index('[numthreads(16, 1, 1)]\nvoid ScreenProbeFilterGatherTracesCS(')
        end = filtering.index('float4 AccumulateLegacyDirectionalProbeAnchor(', start)
        spatial = filtering[start:end]
        spatial = spatial.replace('weight *= saturate(1.0 - angle / (PI / 18.0));',
            'float angleWeight = saturate(1.0 - angle / (PI / 18.0));')
        spatial = spatial.replace('float angle = acos(clamp(dot(toHit, direction) / max(length(toHit), 1e-5), -1.0, 1.0));',
            '''float3 reverseHit = -delta + direction * distance;
            float cosine = min(dot(toHit, direction) / max(length(toHit), 1e-5),
                dot(reverseHit, direction) / max(length(reverseHit), 1e-5));
            float angle = acos(clamp(cosine, -1.0, 1.0));''')
        spatial = spatial.replace('float relativeDepth = dot(delta, _SdfProbeCameraForward.xyz) / eyeDepth;',
            '''float otherEyeDepth = max(0.1, dot(float4(other.xyz, 1.0), _SdfProbeCameraForward));
            weight = min(weight, ProbeTraceGeometryWeight(geometry, other.xyz,
                UnpackProbeNormal(other.w), otherEyeDepth)) * angleWeight;
            float relativeDepth = dot(delta, _SdfProbeCameraForward.xyz) / min(eyeDepth, otherEyeDepth);''')
        spatial = spatial.replace('sum += otherLighting.rgb * weight;\n            total += weight;',
            'sum += 0.2 * weight * (otherLighting.rgb - lighting.rgb);')
        filtering = filtering[:start] + spatial + filtering[end:]
    if spatial_passes == 3:
        assert radiance_domain
        start = filtering.index('[numthreads(16, 1, 1)]\nvoid ScreenProbeFilterGatherTracesCS(')
        end = filtering.index('float4 AccumulateLegacyDirectionalProbeAnchor(', start)
        spatial = filtering[start:end].replace('ScreenProbeFilterGatherTracesCS', 'ScreenProbeFilterRadianceSpatialCS')
        for expression in ('address', 'otherAddress'):
            spatial = spatial.replace(f'_SdfDirectionalTemporal.Load(int3({expression}, 0))', f'_SdfDirectionalSpatialInput.Load(int3({expression}, 0))')
        filtering += '\n#pragma kernel ScreenProbeFilterRadianceSpatialCS\nTexture2D<float4> _SdfDirectionalSpatialInput;\n' + spatial
    shaders['directional-filter.compute.txt'] = include_paths(filtering)

    helper = Path(__file__).with_name('ScreenProbeExperimentalGather.cs.txt').read_text()
    helper = helper.replace('quadrature', 'directionalFilter').replace('Quadrature', 'DirectionalFilter')
    helper = helper.replace('m_GatherCompute', 'm_FilterCompute').replace('directionalFilter-gather.compute.txt', 'directional-filter.compute.txt')
    kernels = re.findall(r'^#pragma kernel (\w+)', filtering, re.M)
    start = helper.index('foreach(string name in new[]{')
    end = helper.index('}) {', start)
    helper = helper[:start] + 'foreach(string name in new[]{' + ','.join('"'+k+'"' for k in kernels) + helper[end:]
    if dither_history:
        helper = '    CommandBuffer directionalFrameCommands=null;\n' + helper
        helper = helper.replace('field.SetValue(qualityPass,directionalFilterShader);directionalFilterInstalled=true;',
            'field.SetValue(qualityPass,directionalFilterShader);directionalFilterInstalled=true;\n        directionalFrameCommands=new CommandBuffer{name="Directional radiance frame binding"};')
        helper = helper.replace('if(directionalFilterShader!=null)UnityEngine.Object.DestroyImmediate(directionalFilterShader);',
            'directionalFrameCommands?.Release();\n        if(directionalFilterShader!=null)UnityEngine.Object.DestroyImmediate(directionalFilterShader);')
    if spatial_passes == 3:
        helper = helper.replace('if(kernel!=directionalFilterOriginal.FindKernel(name))', 'if(name!="ScreenProbeFilterRadianceSpatialCS" && kernel!=directionalFilterOriginal.FindKernel(name))')
        helper += '''
    RenderTexture directionalSpatialScratch=null;
    void FilterDirectionalRadiance(ComputePassContext context,Texture native) {
        var filtered=QualityResolve("m_CurrentDirectionalFiltered");
        if(directionalSpatialScratch==null)directionalSpatialScratch=ComponentTexture(filtered.width,filtered.height);
        var shader=directionalFilterShader;var cmd=(CommandBuffer)wrapped.GetValue(context.cmd);
        int kernel=shader.FindKernel("ScreenProbeFilterRadianceSpatialCS");
        cmd.SetComputeTextureParam(shader,kernel,"_SdfDiffuseGeometry",QualityResolve("m_Geometry"));
        cmd.SetComputeTextureParam(shader,kernel,"_SdfDirectionalComposite",QualityResolve("m_CurrentDirectional"));
        cmd.SetComputeTextureParam(shader,kernel,"_SdfDirectionalTemporal",QualityResolve("m_DirectionalTemporal"));
        for(int pass=0;pass<2;pass++) {
            cmd.SetComputeTextureParam(shader,kernel,"_SdfDirectionalSpatialInput",pass==0?filtered:directionalSpatialScratch);
            cmd.SetComputeTextureParam(shader,kernel,"_SdfDirectionalFilteredOutput",pass==0?directionalSpatialScratch:filtered);
            cmd.DispatchCompute(shader,kernel,120,136,1);
        }
        cmd.DispatchCompute(shader,shader.FindKernel("ScreenProbeConvertToIrradianceCS"),15,17,1);
        var gather=(ComputeShader)type.GetField("m_GatherCompute",inst).GetValue(qualityPass);
        cmd.DispatchCompute(gather,gather.FindKernel("ScreenProbeIntegrateCS"),240,135,1);
    }
'''
        replace('    void ReplayComponents(ComputePassContext context,Texture native) {',
                '    void ReplayComponents(ComputePassContext context,Texture native) {\n        FilterDirectionalRadiance(context,native);')
        replace('CleanupPrivateReplay();', 'if(directionalSpatialScratch!=null){directionalSpatialScratch.Release();UnityEngine.Object.DestroyImmediate(directionalSpatialScratch);}\n            CleanupPrivateReplay();')
    replace('    ComputeShader quadratureShader=null,quadratureOriginal=null;', helper + '\n    ComputeShader quadratureShader=null,quadratureOriginal=null;')
    replace('SetupQuality();SetupComponents();SetupQuadrature();', 'SetupQuality();SetupComponents();SetupQuadrature();SetupDirectionalFilter();')
    replace('CleanupPrivateReplay();CleanupQuadrature();', 'CleanupPrivateReplay();CleanupDirectionalFilter();CleanupQuadrature();')
    replace('componentAaPairs,quadraturePrograms,', 'componentAaPairs,quadraturePrograms,directionalFilterPrograms,')
    if dither_history:
        marker = 'if (active && current == camera) hooks["EditorGatherFrameOverride"].SetValue(null, frameBase + (phase == 1 ? frame : 0));'
        replace(marker, '''if (active && current == camera) {
            int sampleFrame = frameBase + (phase == 1 ? frame : 0);
            hooks["EditorGatherFrameOverride"].SetValue(null, sampleFrame);
            // Production currently binds this constant only on Trace/Gather/Guide.
            // The new radiance quantizer also needs the matching Filter frame.
            directionalFrameCommands.Clear();
            directionalFrameCommands.SetComputeIntParam(directionalFilterShader,"_SdfDiffuseFrame",sampleFrame);
            context.ExecuteCommandBuffer(directionalFrameCommands);
        }''')

    # Async readback callbacks may finish out of order. Preserve every payload,
    # reject duplicate/gapped frames, and feed the original strict reducer in order.
    marker = '    void AccumulateQualityPixels(string name,byte[] data,int width,int height,int f) {'
    replace(marker, '''    var orderedQualityPayloads=new Dictionary<string,SortedDictionary<int,(byte[] data,int width,int height)>>();
    void AccumulateQualityPixels(string name,byte[] data,int width,int height,int f) {
        int index=f-warmup;
        int next=qualityPixelCounts.TryGetValue(name,out int received)?received:0;
        if(index<next || index<0 || index>=count)throw new InvalidOperationException("Duplicate or invalid final statistics frame: "+name);
        if(!orderedQualityPayloads.TryGetValue(name,out var queue)) {
            queue=new SortedDictionary<int,(byte[] data,int width,int height)>();orderedQualityPayloads.Add(name,queue);
        }
        if(queue.ContainsKey(index))throw new InvalidOperationException("Duplicate queued final statistics frame: "+name);
        queue.Add(index,(data,width,height));
        while(queue.TryGetValue(next,out var payload)) {
            queue.Remove(next);
            AccumulateQualityPixelsOrdered(name,payload.data,payload.width,payload.height,next+warmup);
            next++;
        }
    }
    void AccumulateQualityPixelsOrdered(string name,byte[] data,int width,int height,int f) {''')
    replace('    void PublishQualityStatistics() {', '''    void PublishQualityStatistics() {
        if(orderedQualityPayloads.Values.Any(q=>q.Count!=0))throw new InvalidOperationException("Unconsumed final statistics frames.");''')

    gather = (folder / 'SdfScreenProbeGather.compute').read_text(encoding='utf-8-sig')
    marker = '                    uint2 coveragePixel = probe + uint2(6u * grid.x, 0u);'
    assert gather.count(marker) == 1
    gather = gather.replace(marker, marker + '''
                    if ((_SdfGatherHistoryDiagnostics & 67108864u) != 0u
                        && (_SdfGatherHistoryDiagnostics & 134217728u) == 0u)
                        coveragePixel.x += 7u * grid.x;''')
    marker = '    float4 tail = _SdfDiffuseProbeInput.Load(int3(tile + 6u * stride, 0));'
    assert gather.count(marker) == 1
    gather = gather.replace(marker, '''    // The diagnostic native atlas retains the baseline in planes 0..6.
    // Private copies already select the desired seven planes.
    if ((_SdfGatherHistoryDiagnostics & 67108864u) != 0u
        && (_SdfGatherHistoryDiagnostics & 134217728u) == 0u)
        tile += 7u * stride;
''' + marker)
    marker = '    float footprint = ScreenProbeStableRadianceFootprint(pixel, size, position, normal, eyeDepth);'
    assert gather.count(marker) == 1
    gather = gather.replace(marker, '    float footprint = (_SdfGatherHistoryDiagnostics & 67108864u) != 0u ? 1.0\n        : ScreenProbeStableRadianceFootprint(pixel, size, position, normal, eyeDepth);')
    shaders['quadrature-gather.compute.txt'] = include_paths(gather)
    output.mkdir(parents=True, exist_ok=False)
    (output / 'capture-script.cs').write_text(source, encoding='utf-8')
    for name, body in shaders.items():
        (output / name).write_text(body, encoding='utf-8')
    (output / 'targets.json').write_text(json.dumps(targets(), indent=2)+'\n')
    (output / 'protocol.json').write_text(json.dumps(dict(schema='screen-probe-directional-irradiance.v1',
        sequence=sequence, spacing=16, rays=8, warmup=1024, measuredFrames=frames,
        nativeSide='candidate', independentPixelHistories=True, sameFrameInputs=True,
        precisionChanges=False, scalarCalibration=False, directionalCoverage=True,
        scalarFootprint=False, sourceFrozen=True, radianceDomain=radiance_domain,
        spatialPasses=spatial_passes,historyWeight=history_weight,lumenSpatial=lumen_spatial,
        conservativeSpatial=conservative_spatial,ditherHistory=dither_history), indent=2)+'\n')
    snapshot_capture(output, shaders)
    return dict(root=str(output), kernels=kernels)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('output', type=Path)
    parser.add_argument('--sequence', type=int, default=1733)
    parser.add_argument('--radiance-domain', action='store_true')
    parser.add_argument('--spatial-passes', type=int, default=1)
    parser.add_argument('--history-weight', type=float, default=.5)
    parser.add_argument('--lumen-spatial', action='store_true')
    parser.add_argument('--conservative-spatial', action='store_true')
    parser.add_argument('--dither-history', action='store_true')
    args = parser.parse_args()
    print(json.dumps(generate(args.output.resolve(), args.sequence, args.radiance_domain, args.spatial_passes, args.history_weight, args.lumen_spatial, args.conservative_spatial, args.dither_history)))
