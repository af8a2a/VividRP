# Compile with real project references, check meshlet quantization and legacy blobs,
# and regenerate the GPU-driven include through CoreRP.
# This never launches Unity, imports assets, or runs Unity Test Framework.
[CmdletBinding()]
param(
    [string] $OutputDirectory = '',
    [string] $BeeDagDirectory = '',
    [switch] $Gpu,
    [string] $DxcPath = 'dxc',
    [string] $ClangClPath = 'clang-cl'
)

$ErrorActionPreference = 'Stop'
$packageRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $packageRoot '../..')).Path
if (!$OutputDirectory) { $OutputDirectory = Join-Path $packageRoot 'Temp~/meshlet-quantization' }
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
if (!$BeeDagDirectory) {
    $candidates = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Library/Bee/artifacts') -Directory |
        Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'VividRP.Runtime.rsp') } |
        Sort-Object { (Get-Item -LiteralPath (Join-Path $_.FullName 'VividRP.Runtime.rsp')).LastWriteTimeUtc } -Descending)
    if ($candidates.Count -eq 0) { throw 'No imported project Bee VividRP.Runtime.rsp found.' }
    $BeeDagDirectory = $candidates[0].FullName
}
$beeRoot = (Resolve-Path -LiteralPath $BeeDagDirectory).Path
$runtimeText = Get-Content -Raw -LiteralPath (Join-Path $beeRoot 'VividRP.Runtime.rsp')
$editorReference = [regex]::Match($runtimeText,
    '-r:"(?<path>[^"]*/Editor/Data)/[^"]*/UnityEngine.CoreModule\.dll"')
if (!$editorReference.Success) { throw 'Cannot determine this project Editor SDK from its Bee references.' }
$editorData = $editorReference.Groups['path'].Value
$dotnet = Join-Path $editorData 'DotNetSdk/dotnet.exe'
$sdk = @(Get-ChildItem -LiteralPath (Join-Path $editorData 'DotNetSdk/sdk') -Directory |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'Roslyn/bincore/csc.dll') } |
    Sort-Object { [version] $_.Name } -Descending)[0]
$csc = Join-Path $sdk.FullName 'Roslyn/bincore/csc.dll'
if (!(Test-Path -LiteralPath $dotnet) -or !(Test-Path -LiteralPath $csc)) {
    throw 'This project Unity DotNetSdk / Roslyn compiler is missing.'
}
$utf8 = [Text.UTF8Encoding]::new($false)
$referencePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

function Invoke-Compiler([string] $Name, [string] $ResponsePath) {
    $logPath = Join-Path $outputRoot ($Name + '.compile.log')
    $ErrorActionPreference = 'Continue'
    $messages = @(& $dotnet exec $csc ('@' + $ResponsePath) 2>&1)
    $compilerExitCode = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    [IO.File]::WriteAllLines($logPath, [string[]] $messages, $utf8)
    if ($compilerExitCode -ne 0) {
        $messages | Where-Object { $_ -match 'error [A-Z]+\d+' } | Select-Object -First 30 | Write-Output
        throw "$Name compilation failed ($compilerExitCode); see $logPath"
    }
    Write-Output "$Name compiled with the project Bee response file. Log: $logPath"
}

Push-Location $projectRoot
try {
    foreach ($assembly in @('VividRP.Runtime', 'VividRP.Editor', 'VividRP.Editor.Tests')) {
        $sourceRsp = Join-Path $beeRoot ($assembly + '.rsp')
        if (!(Test-Path -LiteralPath $sourceRsp)) { throw "Bee response file missing: $sourceRsp" }
        $lines = [Collections.Generic.List[string]]::new()
        foreach ($line in Get-Content -LiteralPath $sourceRsp) {
            if ($line -match '^-out:') {
                $lines.Add('-out:"' + (Join-Path $outputRoot ($assembly + '.dll')) + '"')
            } elseif ($line -match '^-refout:') {
                $lines.Add('-refout:"' + (Join-Path $outputRoot ($assembly + '.ref.dll')) + '"')
            } elseif ($line -match '^-r:"(?<path>[^"]+)"$') {
                $path = $Matches['path']
                if (![IO.Path]::IsPathRooted($path)) { $path = Join-Path $projectRoot $path }
                $leaf = [IO.Path]::GetFileName($path)
                if ($leaf -in @('VividRP.Runtime.ref.dll', 'VividRP.Editor.ref.dll')) {
                    $path = Join-Path $outputRoot $leaf
                }
                $lines.Add('-r:"' + $path + '"')
                [void] $referencePaths.Add($path)
            } else { $lines.Add($line) }
        }
        $responsePath = Join-Path $outputRoot ($assembly + '.rsp')
        [IO.File]::WriteAllLines($responsePath, $lines, $utf8)
        Invoke-Compiler $assembly $responsePath
    }
} finally { Pop-Location }

# Resolve package execution assemblies from the imported project, while preferring
# the three newly compiled assemblies. No substitute types or stubs are compiled.
$executionReferences = [Collections.Generic.List[string]]::new()
foreach ($path in $referencePaths) {
    $name = [IO.Path]::GetFileName($path) -replace '\.ref\.dll$', '.dll'
    $temporary = Join-Path $outputRoot $name
    $imported = Join-Path $projectRoot ('Library/ScriptAssemblies/' + $name)
    $implementation = $path -replace '\.ref\.dll$', '.dll'
    if (Test-Path -LiteralPath $temporary) { $executionReferences.Add($temporary) }
    elseif (Test-Path -LiteralPath $imported) { $executionReferences.Add($imported) }
    elseif (Test-Path -LiteralPath $implementation) { $executionReferences.Add($implementation) }
    else { $executionReferences.Add($path) }
}
[IO.File]::WriteAllText((Join-Path $outputRoot 'references.json'),
    (ConvertTo-Json -InputObject @($executionReferences) -Compress), $utf8)
$nunit = @($executionReferences | Where-Object { [IO.Path]::GetFileName($_) -eq 'nunit.framework.dll' })[0]
if (!$nunit) { throw 'The real project NUnit reference was not found.' }

$runnerSource = @'
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

internal static class Program
{
    static readonly Dictionary<string, string> References = new(StringComparer.OrdinalIgnoreCase);
    static int Main(string[] args)
    {
        string output = Path.GetFullPath(args[0]);
        foreach (string path in JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(output, "references.json"))))
            References.TryAdd(Path.GetFileNameWithoutExtension(path), path);
        AssemblyLoadContext.Default.Resolving += (_, name) => References.TryGetValue(name.Name, out string path)
            ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        return Run(output);
    }
    static int Run(string output)
    {
        int passed = 0, failed = 0;
        Assembly tests = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(output, "VividRP.Editor.Tests.dll"));
        foreach (string fixtureName in new[] { "VividMeshletCollectionBinarySerializerTests", "VividGPUDrivenBufferSetTests" })
        {
            Type type = tests.GetType("VividRP.Editor.Tests." + fixtureName, true);
            object instance = Activator.CreateInstance(type);
            foreach (MethodInfo method in type.GetMethods().OrderBy(m => m.Name))
            {
                if (fixtureName == "VividGPUDrivenBufferSetTests" && !method.Name.StartsWith("MeshletVertex")
                    && !method.Name.StartsWith("MeshletMetadataPacking")
                    && !method.Name.StartsWith("MeshLODShared")
                    && method.Name != "InstanceDataLayout_HasHlslFieldOffsets"
                    && method.Name != "MeshLODNodeLayout_PreservesVersion6CPUFieldOffsets"
                    && method.Name != "GPUDataLayouts_HaveExpectedStrides") continue;
                var cases = method.GetCustomAttributes<TestCaseAttribute>().ToArray();
                if (method.GetCustomAttribute<TestAttribute>() == null && cases.Length == 0) continue;
                foreach (object[] parameters in cases.Length == 0 ? new[] { Array.Empty<object>() } : cases.Select(c => c.Arguments))
                {
                    try { method.Invoke(instance, parameters); passed++; Console.WriteLine("PASS " + method.Name); }
                    catch (Exception e) { failed++; Console.WriteLine("FAIL " + method.Name + ": " + (e.InnerException ?? e)); }
                }
            }
        }
        EmitGpuVectors(output);
        // Run the real CoreRP C# -> HLSL generator for this source file only.
        Assembly runtime = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(output, "VividRP.Runtime.dll"));
        Assembly coreEditor = AssemblyLoadContext.Default.LoadFromAssemblyPath(References["Unity.RenderPipelines.Core.Editor"]);
        Type generatorType = coreEditor.GetType("UnityEditor.Rendering.ShaderTypeGenerator", true);
        IList generators = (IList) Activator.CreateInstance(typeof(List<>).MakeGenericType(generatorType));
        string sourcePath = null;
        foreach (Type type in runtime.GetTypes())
        {
            object attr = type.GetCustomAttributes().FirstOrDefault(a => a.GetType().Name == "GenerateHLSL");
            if (attr == null) continue;
            string path = (string) attr.GetType().GetField("sourcePath").GetValue(attr);
            if (!path.Replace('\\', '/').EndsWith("/VividGPUDrivenStructs.cs")) continue;
            sourcePath = Path.GetFullPath(path);
            generators.Add(Activator.CreateInstance(generatorType, type, attr));
        }
        if (sourcePath == null || generators.Count == 0) throw new Exception("Missing GPU-driven generator types");
        MethodInfo generate = coreEditor.GetType("UnityEditor.Rendering.CSharpToHLSL", true)
            .GetMethod("GenerateAsync", BindingFlags.Static | BindingFlags.NonPublic);
        ((Task) generate.Invoke(null, new object[] { sourcePath + ".hlsl", Path.ChangeExtension(sourcePath, "custom") + ".hlsl", generators }))
            .GetAwaiter().GetResult();
        Console.WriteLine("Generated " + sourcePath + ".hlsl using CoreRP's generator.");
        Console.WriteLine($"Pure managed checks: {passed} passed, {failed} failed. No Unity Test Framework execution.");
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { passed, failed }));
        return failed == 0 ? 0 : 1;
    }
    static void EmitGpuVectors(string output)
    {
        Assembly runtime = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(output, "VividRP.Runtime.dll"));
        Type packing = runtime.GetType("VividRP.Runtime.GPUDriven.VividMeshletVertexPacking", true);
        Type vertex = runtime.GetType("VividRP.Runtime.GPUDriven.VividMeshletVertex", true);
        MethodInfo pack = packing.GetMethod("Pack");
        Type f3 = pack.GetParameters()[0].ParameterType, f4 = pack.GetParameters()[2].ParameterType, f2 = pack.GetParameters()[3].ParameterType;
        MethodInfo normalDecode = packing.GetMethods().Single(m => m.Name == "UnpackNormal" && m.GetParameters()[0].ParameterType.IsByRef);
        MethodInfo tangentDecode = packing.GetMethods().Single(m => m.Name == "UnpackTangent" && m.GetParameters()[0].ParameterType.IsByRef);
        object Vec(Type t, params float[] values) => Activator.CreateInstance(t, values.Cast<object>().ToArray());
        float Component(object v, string name) => (float)v.GetType().GetField(name).GetValue(v);
        using var packedOut = new BinaryWriter(File.Create(Path.Combine(output, "vertices.bin")));
        using var decodedOut = new BinaryWriter(File.Create(Path.Combine(output, "expected.bin")));
        var random = new Random(7921);
        float R() => (float)(random.NextDouble() * 2 - 1);
        for (int i = 0; i < 4096; i++)
        {
            object n = i % 8 == 0 ? Vec(f3, 0, 0, 0) : i % 8 == 1 ? Vec(f3, 0, 0, -1)
                : i % 8 == 2 ? Vec(f3, 0.001f, -0.001f, -1) : Vec(f3, R(), R(), R());
            object t = i % 8 == 3 ? Vec(f4, 0, 0, 0, 0) : Vec(f4, R(), R(), R(), i % 2 == 0 ? -1 : 1);
            float scale = (float)Math.Pow(2, i % 61 - 30);
            object v = pack.Invoke(null, new[] { Vec(f3, R()*scale, R()*scale, R()*scale), n, t, Vec(f2, R()*scale, R()*scale) });
            foreach (string field in new[] { "PositionXY", "PositionYZ", "PositionZUV", "UVAndFlags", "TangentFrame" })
                packedOut.Write((uint)vertex.GetField(field).GetValue(v));
            object pos = vertex.GetProperty("Position").GetValue(v), uv = vertex.GetProperty("UV").GetValue(v);
            object normal = normalDecode.Invoke(null, new[] { v }), tangent = tangentDecode.Invoke(null, new[] { v });
            foreach (string name in new[] { "x", "y", "z" }) decodedOut.Write(Component(pos, name));
            decodedOut.Write(1f);
            foreach (string name in new[] { "x", "y", "z" }) decodedOut.Write(Component(normal, name));
            decodedOut.Write(0f);
            foreach (string name in new[] { "x", "y", "z", "w" }) decodedOut.Write(Component(tangent, name));
            decodedOut.Write(Component(uv, "x")); decodedOut.Write(Component(uv, "y"));
            decodedOut.Write(0f); decodedOut.Write(0f);
        }
        Console.WriteLine("Exported 4096 production C# encoder/decoder vectors for the GPU probe.");
    }

}

'@
[IO.File]::WriteAllText((Join-Path $outputRoot 'Program.cs'), $runnerSource, $utf8)
$targetingPack = @(Get-ChildItem -LiteralPath (Join-Path $editorData 'DotNetSdk/packs/Microsoft.NETCore.App.Ref') -Directory |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'ref/net10.0/System.Runtime.dll') } |
    Sort-Object { [version] $_.Name } -Descending)[0]
if (!$targetingPack) { throw 'The project Unity SDK does not contain the .NET 10 reference pack.' }
$runnerRsp = [Collections.Generic.List[string]]::new()
$runnerRsp.Add('-nologo')
$runnerRsp.Add('-target:exe')
$runnerRsp.Add('-out:"' + (Join-Path $outputRoot 'Validate.dll') + '"')
foreach ($reference in Get-ChildItem -LiteralPath (Join-Path $targetingPack.FullName 'ref/net10.0') -Filter '*.dll') {
    $runnerRsp.Add('-r:"' + $reference.FullName + '"')
}
$runnerRsp.Add('-r:"' + $nunit + '"')
$runnerRsp.Add('"' + (Join-Path $outputRoot 'Program.cs') + '"')
$runnerRspPath = Join-Path $outputRoot 'Validate.rsp'
[IO.File]::WriteAllLines($runnerRspPath, $runnerRsp, $utf8)
Invoke-Compiler 'Validate' $runnerRspPath
[IO.File]::WriteAllText((Join-Path $outputRoot 'Validate.runtimeconfig.json'),
    '{"runtimeOptions":{"tfm":"net10.0","framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}', $utf8)
$ErrorActionPreference = 'Continue'
$runnerArguments = @((Join-Path $outputRoot 'Validate.dll'), $outputRoot)
$testOutput = @(& $dotnet @runnerArguments 2>&1)
$testExitCode = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
[IO.File]::WriteAllLines((Join-Path $outputRoot 'tests.log'), [string[]] $testOutput, $utf8)
$testOutput | Write-Output
if ($testExitCode -ne 0) { throw "Meshlet quantization checks failed ($testExitCode)." }

if ($Gpu) {
    $dxc = (Get-Command $DxcPath -ErrorAction Stop).Source
    $clang = (Get-Command $ClangClPath -ErrorAction Stop).Source
    $shader = @'
#include "Runtime/SubSystem/GPUDriven/VividGPUDrivenStructs.cs.hlsl"
#include "Shaders/Core/Public/GPUDriven/VividMeshletDecode.hlsli"
StructuredBuffer<VividMeshletVertex> Source : register(t0);
RWStructuredBuffer<float4> Result : register(u0);
[numthreads(64,1,1)]
void Main(uint3 id : SV_DispatchThreadID)
{
    VividDecodedMeshletVertex v = DecodeVividMeshletVertex(Source[id.x]);
    Result[id.x * 4 + 0] = v.Position;
    Result[id.x * 4 + 1] = v.Normal;
    Result[id.x * 4 + 2] = v.Tangent;
    Result[id.x * 4 + 3] = v.UV;
}
'@
    $source = Join-Path $outputRoot 'DecodeProbe.hlsl'
    $dxil = Join-Path $outputRoot 'DecodeProbe.dxil'
    $exe = Join-Path $outputRoot 'GPUProbe.exe'
    [IO.File]::WriteAllText($source, $shader, $utf8)
    & $dxc -T cs_6_0 -E Main -I $packageRoot -Fo $dxil $source
    if ($LASTEXITCODE -ne 0) { throw 'GPU decoder DXC compilation failed.' }
    & $clang /nologo /std:c++17 /EHsc /O2 (Join-Path $PSScriptRoot 'MeshletQuantizationGPUProbe.cpp') `
        ('/Fo' + (Join-Path $outputRoot 'GPUProbe.obj')) ('/Fe' + $exe) /link d3d12.lib dxgi.lib
    if ($LASTEXITCODE -ne 0) { throw 'Native D3D12 probe compilation failed; Windows SDK and linker are required.' }
    $gpuOutput = @(& $exe $dxil (Join-Path $outputRoot 'vertices.bin') (Join-Path $outputRoot 'expected.bin') 2>&1)
    $gpuExitCode = $LASTEXITCODE
    [IO.File]::WriteAllLines((Join-Path $outputRoot 'gpu.log'), [string[]] $gpuOutput, $utf8)
    $gpuOutput | Write-Output
    if ($gpuExitCode -ne 0) { throw "GPU decoder comparison failed ($gpuExitCode)." }
}
