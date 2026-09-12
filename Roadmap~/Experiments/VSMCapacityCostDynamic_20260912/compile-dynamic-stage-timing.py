"""Compile the archived candidate with Unity's current Editor references, without import."""
from pathlib import Path
import hashlib
import json
import subprocess
import sys

report = Path(__file__).resolve().parent
project = report.parents[4]
response = project / 'Library/Bee/artifacts/1900b0aE.dag/VividRP.Editor.rsp'
meshlet = '--meshlet' in sys.argv
output = report / ('dynamic-meshlet-compile' if meshlet else 'dynamic-stage-timing-compile')
output.mkdir(exist_ok=True)
sources = [report / ('VSMDynamicMeshletBaselineProbe.cs.txt' if meshlet else 'VSMDynamicStageTimingProbe.cs.txt'), report / 'VSMStageTimingCapture.cs.txt']
options = []
for line in response.read_text(encoding='utf-8-sig').splitlines():
    if not line.startswith(('-', '/')):
        if meshlet and not any(name in line for name in ['VSMDynamicBaselineProbe.cs', 'VSMStageTimingCapture.cs']):
            options.append(line)
        continue
    if line.lower().startswith(('-out:', '-refout:', '-analyzer:', '/analyzer:', '/additionalfile:', '-additionalfile:')):
        if meshlet and 'VividRP.RenderPassNodeGenerator.dll' in line:
            options.append(line)
        continue
    options.append(line)
options += [f'-out:"{output / "VividRP.Editor.dll"}"', f'-refout:"{output / "VividRP.Editor.ref.dll"}"']
options += [f'"{source}"' for source in sources]
rsp = output / 'VividRP.Editor.rsp'
rsp.write_text('\n'.join(options), encoding='utf-8')
cmd = ['E:/Unity/6000.7.0a6/Editor/Data/DotNetSdk/dotnet.exe', 'exec',
       'E:/Unity/6000.7.0a6/Editor/Data/DotNetSdk/sdk/10.0.301/Roslyn/bincore/csc.dll',
       '/nostdlib', '/noconfig', '@' + str(rsp)]
result = subprocess.run(cmd, cwd=project, capture_output=True, text=True, encoding='utf-8', errors='replace')
(output / 'compile.log').write_text(result.stdout + result.stderr, encoding='utf-8')
summary = {'status': 'pass' if result.returncode == 0 else 'failed', 'exit_code': result.returncode,
           'compiler': cmd[2], 'unity_imported': False, 'unity_test_framework_run': False,
           'sources': {str(p.relative_to(report)): hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}}
(output / 'validation.json').write_text(json.dumps(summary, indent=2) + '\n', encoding='utf-8')
print(json.dumps(summary, indent=2))
if result.returncode:
    print(result.stdout + result.stderr)
raise SystemExit(result.returncode)
