"""Compile both caster entry paths against the archived experiment headers."""
from pathlib import Path
import hashlib
import json
import subprocess

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
SOURCE = ROOT / "Temp~/vsm-depth-variants/variants"
OUT = ROOT / "Temp~/vsm-depth-variants/caster-validation"
OUT.mkdir(parents=True, exist_ok=True)
caster = (SOURCE / "VividVirtualShadowMapCaster.hlsl").read_text(encoding="utf-8-sig")
caster = caster.replace("Packages/com.vivid.render-pipelines/Shaders/Core/Public/Shadow/VividVirtualShadowMapAddressing.hlsl",
    (SOURCE / "VividVirtualShadowMapAddressing.hlsl").as_posix())
(OUT / "caster.hlsl").write_text(caster)
results = []
for name, define, call in (("compatibility", "VIVID_VSM_CASTER", "VividWriteVSMDepth(position)"),
                           ("page", "VIVID_VSM_PAGE_CASTER", "VividWriteVSMPageDepth(position,0u)")):
    wrapper = OUT / (name + ".hlsl")
    wrapper.write_text(f'#define {define} 1\n#include "caster.hlsl"\nvoid main(float4 position:SV_Position) {{ {call}; }}\n')
    result = subprocess.run(["C:/VulkanSDK/1.4.350.0/Bin/dxc.exe", "-T", "ps_6_6", "-E", "main",
                             "-Fo", str(OUT / (name + ".dxil")), str(wrapper)], capture_output=True, text=True)
    (OUT / (name + ".log")).write_text(result.stdout + result.stderr)
    results.append(dict(name=name, exit_code=result.returncode))
report = dict(results=results, source_sha256={p.name: hashlib.sha256(p.read_bytes()).hexdigest()
    for p in (SOURCE / "VividVirtualShadowMapCaster.hlsl", SOURCE / "VividVirtualShadowMapAddressing.hlsl")})
(HERE / "caster-dxc.json").write_text(json.dumps(report, indent=2))
assert all(r["exit_code"] == 0 for r in results), results
print("Both archived experimental caster paths compiled successfully")
