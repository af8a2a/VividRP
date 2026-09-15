"""Compile one candidate at a time, eliminating other experiment branches."""
import hashlib
import json
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
MODE, LABEL = int(sys.argv[1]), sys.argv[2]
PATHS = ["Shaders/Core/Private/CSMShadowResolve.compute", "Shaders/Core/Private/VSMSMRT.hlsl",
    "Shaders/Core/Public/Shadow/VividVirtualShadowMapAddressing.hlsl",
    "Shaders/Core/Public/Shadow/VividVirtualShadowMapCaster.hlsl",
    "Tests/Editor/RenderPass/Shadows/VirtualShadowMapSamplingTests.compute"]
for path in PATHS:
    source = ROOT / path
    # 'variants' is captured once, after all switchable-variant measurements.
    variant = ROOT / "Temp~/vsm-depth-variants/variants" / source.name
    s = (ROOT / "Temp~/vsm-depth-variants/before" / source.name).read_text(encoding="utf-8-sig") if MODE == 0 else variant.read_text(encoding="utf-8-sig")
    if MODE:
        s = s.replace("int _VSMDepthLayoutInterleaved;", "static const int _VSMDepthLayoutInterleaved = 0;")
        s = s.replace("int _VSMDepthExperiment;", f"static const int _VSMDepthExperiment = {MODE};")
    source.write_text(s, encoding="utf-8")
(HERE / (LABEL + "-source-sha256.json")).write_text(json.dumps({p: hashlib.sha256((ROOT / p).read_bytes()).hexdigest() for p in PATHS}, indent=2))
s = (HERE / "timing.cs.txt").read_text(encoding="utf-8-sig")
s = s.replace('VSMDepthVariants_20260913/timing"', 'VSMDepthVariants_20260913/' + LABEL + '"')
values = dict(resolutions=[2048, 4096, 4096, 2048], modes=[MODE] * 4, layouts=[0] * 4, capacities=[1024] * 4)
for name, items in values.items():
    s = re.sub(r"var " + name + r" = new int\[\] \{[^}]+\};", "var " + name + " = new int[] {" + ",".join(map(str, items)) + "};", s)
(HERE / (LABEL + ".cs.txt")).write_text(s, encoding="utf-8")
(ROOT / "Temp~/vsm-depth-variants/specialized.cs").write_text(s, encoding="utf-8")
