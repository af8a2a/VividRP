"""Compile the diagnostic kernels and actual-insertion raster entry points.

This is an offline DXC check, not a Unity GPU execution.
"""
from pathlib import Path
import hashlib
import json
import subprocess

here = Path(__file__).resolve().parent
root = here.parents[2]
out = here / 'capacity-dxc'
out.mkdir(exist_ok=True)
compiler = Path('C:/VulkanSDK/1.4.350.0/Bin/dxc.exe')
include = root / 'Temp~/vsm-bnd-compare/dxc2/include'
source = here / 'VSMCapacityInsertion.shader.txt'
hlsl = source.read_text(encoding='utf-8').split('HLSLPROGRAM', 1)[1].split('ENDHLSL', 1)[0]
(out / 'capacity-insertion.hlsl').write_text(hlsl, encoding='utf-8')
entries = [(here / 'VSMCapacityAudit.compute.txt', 'VSMCapacityAuditPages', 'cs_6_2'),
           (here / 'VSMCapacityAudit.compute.txt', 'VSMCapacityReduce', 'cs_6_2'),
           (here / 'VSMCapacityFixture.compute.txt', 'VSMCapacityFixture', 'cs_6_2'),
           (here / 'VSMCapacityFixture.compute.txt', 'VSMCapacityClearLayers', 'cs_6_2'),
           (out / 'capacity-insertion.hlsl', 'Vert', 'vs_6_2'),
           (out / 'capacity-insertion.hlsl', 'Frag', 'ps_6_2')]
results = []
for source, kernel, target in entries:
    command = [str(compiler), '-T', target, '-E', kernel, '-D', 'SHADER_API_D3D11=1',
               '-D', 'UNITY_COMPILER_DXC=1', '-I', str(include), '-Fo', str(out / (kernel + '.dxil')), str(source)]
    result = subprocess.run(command, capture_output=True, text=True)
    (out / (kernel + '.log')).write_text(result.stdout + result.stderr, encoding='utf-8')
    results.append(dict(source=str(source.relative_to(here)), kernel=kernel, target=target,
                        sha256=hashlib.sha256(source.read_bytes()).hexdigest(), exit_code=result.returncode))
report = dict(total=len(results), passed=sum(r['exit_code'] == 0 for r in results), results=results)
(here / 'capacity-dxc.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps(dict(total=report['total'], passed=report['passed'])))
if report['total'] != report['passed']:
    for result in results:
        if result['exit_code']:
            print((out / (result['kernel'] + '.log')).read_text(encoding='utf-8'))
    raise SystemExit(1)
