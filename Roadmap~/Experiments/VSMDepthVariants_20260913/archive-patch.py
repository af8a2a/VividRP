"""Preserve Git's raw patch bytes; PowerShell line rewriting breaks mixed EOLs."""
from pathlib import Path
import subprocess

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
paths = ["Shaders/Core/Private/CSMShadowResolve.compute", "Shaders/Core/Private/VSMSMRT.hlsl",
         "Shaders/Core/Public/Shadow/VividVirtualShadowMapAddressing.hlsl",
         "Shaders/Core/Public/Shadow/VividVirtualShadowMapCaster.hlsl",
         "Tests/Editor/RenderPass/Shadows/VirtualShadowMapSamplingTests.compute"]
git = "D:/Scoop/apps/git/current/bin/git.exe"
r = subprocess.run([git, "diff", "--no-index", "--no-renames", "Temp~/vsm-depth-variants/before",
                    "Temp~/vsm-depth-variants/variants"], cwd=ROOT, capture_output=True)
assert r.returncode == 1, r.stderr
patch = r.stdout
for path in paths:
    name = Path(path).name
    for side, folder in (("a", "before"), ("b", "variants")):
        patch = patch.replace(f"{side}/Temp~/vsm-depth-variants/{folder}/{name}".encode(), f"{side}/{path}".encode())
target = HERE / "experiment.patch"
target.write_bytes(patch)
r = subprocess.run([git, "apply", "--check", str(target)], cwd=ROOT, capture_output=True, text=True)
assert r.returncode == 0, r.stderr
print("Experimental patch applies cleanly to the restored source")
