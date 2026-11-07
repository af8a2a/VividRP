"""Compile UberPost's complete keyword matrix without importing the Unity project.

The scratch tree contains copied include files, never junctions or symlinks.
This checks DXC compilation, not Unity's pragma handling or rendered output.
"""

import argparse
import itertools
import json
from pathlib import Path
import re
import shutil
import subprocess
import tempfile


KEYWORDS = (
    "_FILM_GRAIN", "_VIGNETTE", "_BLOOM", "_BLOOM_HQ", "_BLOOM_DIRT",
    "HDR_COLORSPACE_CONVERSION",
)


def copy_includes(source, destination, roots, scratch, visited):
    pair = (source, destination)
    if pair in visited:
        return
    visited.add(pair)
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source, destination)
    text = source.read_text(encoding="utf-8-sig")
    for include in re.findall(
        r'^\s*#\s*include(?:_with_pragmas)?\s*"([^"]+)"', text, re.MULTILINE
    ):
        target = source.parent / include
        output = destination.parent / include
        for prefix, root in roots.items():
            if include.startswith(prefix + "/"):
                target = root / include[len(prefix) + 1:]
                output = scratch / include
                break
        # Conditional includes for other platforms may be absent. DXC diagnoses
        # any missing include actually required by the selected platform.
        if target.is_file():
            copy_includes(target, output, roots, scratch, visited)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dxc", default="dxc")
    parser.add_argument("--core-package-root", type=Path)
    parser.add_argument("--output-directory", type=Path)
    parser.add_argument("--backend", choices=("both", "dxil", "spirv"), default="both")
    parser.add_argument("--prepare-unity-project", action="store_true",
                        help="Prepare an isolated Unity smoke project instead of running standalone DXC")
    args = parser.parse_args()
    package = Path(__file__).resolve().parent.parent
    core = (args.core_package_root or package.parent / "CoreRP").resolve()
    if not (core / "ShaderLibrary/Common.hlsl").is_file():
        parser.error("--core-package-root must contain ShaderLibrary/Common.hlsl")
    compiler = shutil.which(args.dxc)
    if not compiler and not args.prepare_unity_project:
        parser.error("DXC executable not found")
    parent = args.output_directory.resolve() if args.output_directory else None
    if parent:
        parent.mkdir(parents=True, exist_ok=True)
    scratch = Path(tempfile.mkdtemp(prefix="VividHlsl2021-", dir=parent)).resolve()
    project = package.parent.parent
    if scratch.is_relative_to(project):
        parser.error("Scratch directory must be outside the containing Unity project")
    roots = {
        "Packages/com.vivid.render-pipelines": package,
        "Packages/com.unity.render-pipelines.core": core,
    }
    shader = package / "Shaders/Core/Private/UberPost.shader"
    text = shader.read_text(encoding="utf-8-sig")
    if not re.search(r"^\s*#pragma hlsl_version 2021\s*$", text, re.MULTILINE):
        parser.error("UberPost must explicitly request HLSL 2021")
    visited = set()
    copy_includes(shader, scratch / "UberPost.shader", roots, scratch, visited)
    if args.prepare_unity_project:
        (scratch / "Assets/Editor").mkdir(parents=True)
        (scratch / "ProjectSettings").mkdir()
        shutil.copyfile(project / "ProjectSettings/ProjectVersion.txt", scratch / "ProjectSettings/ProjectVersion.txt")
        shutil.copyfile(shader, scratch / "Assets/UberPost.shader")
        baseline = text.replace('"Hidden/VividRP/UberPost"', '"Hidden/VividRP/UberPost2018"')
        baseline = baseline.replace("#pragma hlsl_version 2021", "// Unity's default HLSL language version (2018).")
        (scratch / "Assets/UberPost2018.shader").write_text(baseline, encoding="utf-8")
        shutil.copyfile(package / "Tools~/Hlsl2021UnityProbe.cs", scratch / "Assets/Editor/Hlsl2021UnityProbe.cs")
        for prefix in roots:
            manifest = {"name": prefix.split("/")[-1], "version": "0.0.1", "displayName": "HLSL 2021 include-only probe"}
            (scratch / prefix / "package.json").write_text(json.dumps(manifest), encoding="utf-8")
        (scratch / "Packages/manifest.json").write_text(json.dumps({"dependencies": {}}), encoding="utf-8")
        (scratch / "Assets/LanguageProbe.shader").write_text("""Shader "Hidden/Hlsl2021LanguageProbe" {
SubShader { Pass { ZWrite Off ZTest Always Cull Off
HLSLPROGRAM
#pragma target 5.0
#pragma vertex Vert
#pragma fragment Frag
#pragma use_dxc
#pragma hlsl_version 2021
#pragma multi_compile_local _ PROBE_TEMPLATES
#if defined(PROBE_TEMPLATES)
template<typename T> T Twice(T value) { return value + value; }
struct Value {
    uint data;
    Value operator +(Value other) { Value result = {data + other.data}; return result; }
};
#endif
float4 Vert(uint vertexID : SV_VertexID) : SV_POSITION {
    return float4(float2((vertexID << 1) & 2, vertexID & 2) * 2 - 1, 0, 1);
}
bool SideEffect(inout uint side) { side = 99; return true; }
float4 Frag(float4 position : SV_POSITION) : SV_Target {
    #if defined(PROBE_TEMPLATES)
    Value a = {19}; Value b = {2}; Value c = a + b;
    uint value = Twice(c.data);
    #else
    uint value = 42;
    #endif
    uint side = 0;
    bool unused = position.x < 0 && SideEffect(side);
    return float4(value / 100.0, side / 100.0, unused ? 1 : 0, 1);
}
ENDHLSL
} } }
""", encoding="utf-8")
        print(f"Unity project: {scratch}")
        print("Run with Unity 7000.0.0a16, -force-d3d12 -executeMethod Hlsl2021UnityProbe.Run")
        return
    source = scratch / "UberPost.hlsl"
    source.write_text(text.split("HLSLPROGRAM", 1)[1].split("ENDHLSL", 1)[0], encoding="utf-8")
    report = {
        "compiler": compiler,
        "compiler_version": subprocess.check_output([compiler, "--version"], text=True).strip(),
        "shader": str(shader),
        "scratch": str(scratch),
        "copied_files": len(visited),
        "checks": [],
    }
    # Deliberately set -HV: standalone DXC does not interpret Unity pragmas.
    # SHADER_API_D3D11 is Unity's shared D3D shader-library path, also used by DX12.
    try:
        for backend, api in (("dxil", "SHADER_API_D3D11"), ("spirv", "SHADER_API_VULKAN")):
            if args.backend not in ("both", backend):
                continue
            for version in ("2018", "2021"):
                for mask, flags in enumerate(itertools.product((False, True), repeat=len(KEYWORDS))):
                    keywords = [key for key, enabled in zip(KEYWORDS, flags) if enabled]
                    for entry, stage, stage_define in (
                        ("Vert", "vs", "SHADER_STAGE_VERTEX"),
                        ("Frag", "ps", "SHADER_STAGE_FRAGMENT"),
                    ):
                        name = f"{backend}-{version}-{mask:02d}-{entry}"
                        arguments = [
                            compiler, "-T", stage + "_6_0", "-E", entry, "-HV", version,
                            "-I", str(scratch), "-Fo", str(scratch / (name + ".bin")),
                        ]
                        for define in (api, "UNITY_COMPILER_HLSL", "UNITY_COMPILER_DXC", stage_define, "SHADER_TARGET=35", *keywords):
                            arguments += ["-D", define]
                        if backend == "spirv":
                            arguments += ["-spirv", "-fspv-target-env=vulkan1.1", "-fvk-use-dx-layout", "-fvk-auto-shift-bindings"]
                        result = subprocess.run([*arguments, str(source)], capture_output=True, text=True)
                        diagnostics = result.stdout + result.stderr
                        (scratch / (name + ".log")).write_text(diagnostics, encoding="utf-8")
                        report["checks"].append({"name": name, "keywords": keywords, "exit_code": result.returncode})
                        if result.returncode:
                            raise RuntimeError(f"{name} failed:\n{diagnostics}")
                print(f"Passed {backend} HLSL {version}: 64 variants x 2 entrypoints", flush=True)
    finally:
        (scratch / "report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
        print(f"Report: {scratch / 'report.json'}", flush=True)
    print(f"Passed {len(report['checks'])} compilations. Unity import and GPU validation are separate.")


if __name__ == "__main__":
    main()
