"""Extract archived same-direction rays for bounded, read-only card diagnostics."""
import argparse
import json
from pathlib import Path
import numpy as np
from ScreenProbeFirstHitOracle import ArchivedRays, Scene, map_entities, project_receivers, digest
from ProbeAnchorCalibration import normal_from_packed


def replay(output, inputs, candidate, lighting=False):
    """Compile the same archived march policy with either card query."""
    import shutil
    workspace=Path(__file__).resolve().parents[2]
    output.mkdir(parents=True,exist_ok=False)
    for name in ('rays.bin','protocol.json','identities.json'):
        shutil.copyfile(inputs/name,output/name)
    body=(workspace/'Shaders/Core/Public/GPUDriven/MeshSdfTrace.hlsl').read_text(encoding='utf-8-sig')
    limit='uint(min(2048.0,max(192.0,ceil(length(float3(field.dimensions-1u))/(0.075*0.7))+1.0)))'
    for expression in ('samples++ >= 192','step < 192'):
        assert body.count(expression)==1
        body=body.replace(expression,expression.replace('192',limit))
    surface=(workspace/'Shaders/Core/Public/GPUDriven/SdfSurfaceCache.hlsl' if candidate else inputs/'before-SdfSurfaceCache.hlsl').read_text(encoding='utf-8-sig')
    if candidate:
        helper=Path(__file__).with_name('ScreenProbePlanarCardCandidate.hlsl.txt').read_text()
        assert 'bool VividTrySampleSdfSurfaceAlongRay' not in surface
        surface=surface.replace('void VividGetSdfCardProjection',helper+'void VividGetSdfCardProjection',1)
    for name in ('MeshSdfTrace.hlsl','SdfMaterialSidedness.hlsl'):
        surface=surface.replace(f'#include "{name}"',f'#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/{name}"')
    shader=Path(__file__).with_name('ScreenProbeCardCoverage.compute.txt').read_text()
    include='#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/SdfSurfaceCache.hlsl"'
    assert shader.count(include)==1
    shader=shader.replace(include,body+'\n'+surface)
    if candidate:
        shader=shader.replace('VividTrySampleSdfSurface(hit,pos,','VividTrySampleSdfSurfaceAlongRay(hit,d.xyz,pos,')
    script=Path(__file__).with_name('ScreenProbeCardCoverage.cs.txt').read_text().replace('__OUTPUT__',output.as_posix())
    if lighting:
        shader=shader.replace('StructuredBuffer<float4> _CardRays;',
            '#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/SdfScreenProbeCommon.hlsl"\n'
            '#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/SdfSurfaceCacheLighting.hlsl"\nStructuredBuffer<float4> _CardRays;')
        shader=shader.replace('uint o=i*8u;','uint o=i*10u;').replace('j<8;','j<10;')
        marker='    _CardResults[o+6]=float4(texel.xy,error,dot(original,-d.xyz));'
        assert shader.count(marker)==1
        shader=shader.replace(marker,marker+'\n    if(covered){\n'
            '        _CardResults[o+8]=float4(surface.emission.rgb,VividSdfMaterialSideVisible(surface.emission.w,original,-d.xyz));\n'
            '        _CardResults[o+9]=float4(TraceSdfSurfaceRadiance(pos,lightingNormal,original,d.xyz,hit,surface,texel,-1.0),1);\n    }')
        script=script.replace('count*8,16','count*10,16')
        marker='        rays=new GraphicsBuffer'
        # Use the recorded source rather than the shared pass's last camera.
        # Verify both its hash and the eight fixture slots before replaying it.
        lighting=inputs/'baseline-a-v3/capture/1151-radiosity.bin'
        binding='''        var lightingBytes=System.IO.File.ReadAllBytes("__LIGHTING__");
        if(Hash(lightingBytes)!="40252e7ae82c1da6ae025dbf012a3ce232aa54ea10bfbf1ca9a7998df93270a8")throw new Exception("Frozen lighting changed");
        Write(output+"/radiosity.bin",lightingBytes);
        lightingTexture=new Texture2D(128,24,TextureFormat.RGBAHalf,false,true);
        lightingTexture.LoadRawTextureData(lightingBytes);lightingTexture.Apply(false,false);
        shader.SetTexture(k,"_SdfRadiosityInput",lightingTexture);
        var work=(GraphicsBuffer)scene.GetType().GetProperty("RadiosityWork",inst).GetValue(scene);
        var workValues=new uint[work.count];work.GetData(workValues);
        for(int i=0;i<8;++i)if(workValues[i+1]!=i*6)throw new Exception("Fixture lighting slot changed");
        shader.SetBuffer(k,"_SdfRadiosityWork",work);
        shader.SetInt("_SdfRadiosityLayout",48);
        shader.SetVector("_SdfDiffuseLightPositionRange",Vector4.zero);
'''.replace('__LIGHTING__',lighting.as_posix())
        assert script.count(marker)==1;script=script.replace(marker,binding+marker)
        script=script.replace('ComputeShader shader=null;','Texture2D lightingTexture=null;ComputeShader shader=null;')
        script=script.replace('}finally{rays?.Dispose();','}finally{if(lightingTexture!=null)UnityEngine.Object.DestroyImmediate(lightingTexture);rays?.Dispose();')
    (output/'diagnostic.compute.txt').write_text(shader)
    (output/'diagnostic.cs').write_text(script)


def build(root, capture, identities):
    reader = ArchivedRays(capture)
    try:
        frame = 1024
        fixture = json.loads((capture / 'live-fixture.json').read_text())
        scene = Scene(fixture, 'black')
        old_path=capture.parent.parent/'ScreenProbeFirstHitOracle-20261116-190000'/'identities.json'
        old=json.loads(old_path.read_text(encoding='utf-8-sig'))
        mapping = map_entities(scene, old)
        live_mapping=map_entities(scene, identities)
        live_by_object={item:instance for item,instance in live_mapping.values()}
        assert old['fields']==identities['fields'],'SDF field geometry changed'
        receivers = reader.read(frame, 'origin-receivers-full', 'origin-receivers')
        geometry = reader.read(frame, 'origin-geometry-full', 'origin-native')
        valid, objects, positions, normals, projection = project_receivers(scene, geometry, receivers, mapping)
        ray_valid = np.repeat(valid, 8, axis=1)
        first = reader.read(frame, 'origin-first-hit-full', 'origin-native')
        direction = first[2::3, :, :3][ray_valid]
        source = reader.read(frame, 'origin-ray-diagnostics-full', 'origin-native')[ray_valid]
        rows = np.empty((len(direction), 3, 4), '<f4')
        rows[:, 0, :3] = np.repeat(geometry[..., :3][valid], 8, axis=0)
        rows[:, 0, 3] = np.repeat([live_by_object[int(i)] for i in objects], 8)
        rows[:, 1, :3] = np.repeat(normal_from_packed(geometry[..., 3][valid]), 8, axis=0)
        rows[:, 1, 3] = .01
        rows[:, 2, :3] = direction
        rows[:, 2, 3] = np.repeat(objects, 8)
        path = root / 'rays.bin'
        assert not path.exists()
        rows.tofile(path)
        np.savez_compressed(root / 'archived.npz', first=first[0::3][ray_valid], meta=first[1::3][ray_valid], radiance=source,
                            targets=rows, exact_position=np.repeat(positions, 8, axis=0), exact_normal=np.repeat(normals, 8, axis=0))
        protocol = dict(schema='screen-probe-card-coverage.v1', capture=str(capture), frame=frame, count=len(rows),
                        raysSha256=digest(path), identitiesSha256=digest(root / 'identities.json'),oldIdentitiesSha256=digest(old_path),
                        archiveSha256=reader.archive_sha, consumed=reader.consumed, projection=projection,
                        layerMask=next(x['layerMask'] for x in identities['instances'] if x['index'] == int(rows[0, 0, 3])),
                        note='Actual archived receiver/direction. Read-only diagnostic dispatch; no production history or ray budget change.')
        (root / 'protocol.json').write_text(json.dumps(protocol, indent=2)+'\n')
        print(json.dumps(dict(count=len(rows), layerMask=protocol['layerMask'])))
    finally:
        reader.close()


if __name__ == '__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('root',type=Path);p.add_argument('capture',type=Path)
    p.add_argument('--replay',action='store_true');p.add_argument('--candidate',action='store_true');p.add_argument('--lighting',action='store_true')
    a=p.parse_args()
    if a.replay:replay(a.root.resolve(),a.capture.resolve(),a.candidate,a.lighting)
    else:build(a.root,a.capture,json.loads((a.root/'identities.json').read_text(encoding='utf-8-sig')))
