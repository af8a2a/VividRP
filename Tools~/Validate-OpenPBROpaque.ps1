# Focused, non-Unity validation of the OpenPBROpaqueV1 semantic contract.
# Uses the real contract, hash utility, Unity.Mathematics and NUnit fixture.
# Does not import assets, launch Unity or execute shaders on the GPU.
[CmdletBinding()]
param(
    [string] $DxcPath = 'dxc',
    [string] $DotnetPath = 'dotnet',
    [string] $OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'
$packageRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $packageRoot '../..')).Path
$compiler = (Get-Command $DxcPath -ErrorAction Stop).Source
$dotnet = (Get-Command $DotnetPath -ErrorAction Stop).Source
$mathematics = Join-Path $projectRoot 'Library/ScriptAssemblies/Unity.Mathematics.dll'
if (!(Test-Path -LiteralPath $mathematics)) {
    throw "Import the target project first: Unity.Mathematics assembly missing at $mathematics"
}
# Newer Unity versions forward Mathematics value types into an engine module.
# Resolve those references from this project's generated csproj, not another Editor.
$runtimeProjectPath = Join-Path $projectRoot 'VividRP.Runtime.csproj'
$engineReferences = ''
if (Test-Path -LiteralPath $runtimeProjectPath) {
    [xml] $runtimeProject = Get-Content -Raw -LiteralPath $runtimeProjectPath
    foreach ($name in @('UnityEngine.MathematicsModule', 'UnityEngine.CoreModule')) {
        $reference = @($runtimeProject.Project.ItemGroup.Reference |
            Where-Object { $_.Include -eq $name })
        if ($reference.Count -eq 1) {
            $path = [string] $reference[0].HintPath
            if (![IO.Path]::IsPathRooted($path)) { $path = Join-Path $projectRoot $path }
            if (!(Test-Path -LiteralPath $path)) { throw "Project reference missing: $path" }
            $escaped = [System.Security.SecurityElement]::Escape($path)
            $engineReferences += "<Reference Include=`"$name`"><HintPath>$escaped</HintPath></Reference>`n"
        }
    }
}
$nunitPackages = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Library/PackageCache') `
    -Directory -Filter 'com.unity.ext.nunit@*')
$nunitPaths = @($nunitPackages | ForEach-Object {
    $path = Join-Path $_.FullName 'net472/unity-custom/nunit.framework.dll'
    if (Test-Path -LiteralPath $path) { $path }
})
if ($nunitPaths.Count -ne 1) {
    throw "Expected one imported Unity NUnit assembly; found $($nunitPaths.Count)."
}
if (!$OutputDirectory) {
    $OutputDirectory = Join-Path $packageRoot ('Temp~/OpenPBROpaqueValidation-' + [Guid]::NewGuid().ToString('N'))
}
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

# Compile the actual shared hash implementation, without unrelated Unity material
# compiler types. Keep the extracted source verbatim; no replacement hash/stubs.
$identity = Get-Content -Raw -LiteralPath (Join-Path $packageRoot 'Runtime/SubSystem/GPUDriven/Material/MaterialProgramIdentity.cs')
$hashMatch = [regex]::Match($identity,
    '(?s)    internal static class MaterialProgramHashUtility\r?\n    \{.*?(?=\r?\n    internal static class MaterialProgramArtifactSetHashBuilder)')
if (!$hashMatch.Success) { throw 'Cannot locate the real MaterialProgramHashUtility.' }
$hashSource = "using System;`nnamespace VividRP.Runtime.GPUDriven`n{`n" + $hashMatch.Value + "`n}`n"
Set-Content -LiteralPath (Join-Path $outputRoot 'MaterialProgramHashUtility.cs') -Value $hashSource -Encoding utf8

function Xml-Escape([string] $Value) { [System.Security.SecurityElement]::Escape($Value) }
$contractPath = Xml-Escape (Join-Path $packageRoot 'Runtime/SubSystem/GPUDriven/Material/OpenPBROpaqueContract.cs')
$testsPath = Xml-Escape (Join-Path $packageRoot 'Tests/Editor/SubSystem/GPUDriven/OpenPBROpaqueContractTests.cs')
$mathPath = Xml-Escape $mathematics
$nunitPath = Xml-Escape $nunitPaths[0]
# This target matches the installed .NET SDK; no NuGet packages are required.
$project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <Nullable>disable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$contractPath" />
    <Compile Include="$testsPath" />
    <Compile Include="MaterialProgramHashUtility.cs" />
    <Compile Include="Program.cs" />
    <Reference Include="Unity.Mathematics"><HintPath>$mathPath</HintPath></Reference>
    $engineReferences
    <Reference Include="nunit.framework"><HintPath>$nunitPath</HintPath></Reference>
  </ItemGroup>
</Project>
"@
Set-Content -LiteralPath (Join-Path $outputRoot 'Validate.csproj') -Value $project -Encoding utf8
$runner = @'
using System;
using System.Reflection;
using NUnit.Framework;
using VividRP.Editor.Tests;
using VividRP.Runtime.GPUDriven;

class Program
{
    static int Main()
    {
        Console.WriteLine($"OpenPBROpaqueV1 fingerprint: 0x{OpenPBROpaqueContract.Fingerprint:X16}");
        var fixture = new OpenPBROpaqueContractTests();
        int passed = 0, failed = 0;
        foreach (MethodInfo method in fixture.GetType().GetMethods())
        {
            var cases = method.GetCustomAttributes<TestCaseAttribute>();
            bool hasCases = false;
            foreach (TestCaseAttribute testCase in cases)
            {
                hasCases = true;
                Run(method, testCase.Arguments);
            }
            if (!hasCases && method.GetCustomAttribute<TestAttribute>() != null)
                Run(method, Array.Empty<object>());
        }
        Console.WriteLine($"Pure managed contract cases: {passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;

        void Run(MethodInfo method, object[] args)
        {
            try { method.Invoke(fixture, args); passed++; }
            catch (Exception exception)
            {
                failed++;
                Console.WriteLine($"FAIL {method.Name}: {exception.InnerException ?? exception}");
            }
        }
    }
}
'@
Set-Content -LiteralPath (Join-Path $outputRoot 'Program.cs') -Value $runner -Encoding utf8
& $dotnet run --project (Join-Path $outputRoot 'Validate.csproj') --configuration Release --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Pure managed OpenPBR contract checks failed.' }

$profilePath = (Join-Path $packageRoot 'Shaders/Core/Public/VividOpenPBROpaque.hlsl').Replace('\', '/')
$probe = @"
#include "$profilePath"
StructuredBuffer<VividOpenPBROpaqueInputs> _Inputs;
RWStructuredBuffer<float4> _Output;
uint _RequestedFeatures;
float3 _ViewDirection;
float3 _LightDirection;
float3 _Random;

[numthreads(1, 1, 1)]
void ValidateProfile(uint3 id : SV_DispatchThreadID)
{
    VividOpenPBROpaqueInputs inputs = _Inputs[id.x];
    OpenPBR_ResolvedInputs resolved;
    bool valid = VividTryResolveOpenPBROpaqueInputs(inputs, _RequestedFeatures, resolved);
    _Output[id.x] = float4(VividValidateOpenPBROpaqueInputs(inputs, _RequestedFeatures),
        VividValidateOpenPBROpaqueResolvedInputs(resolved), valid, 0.0f);
}

[numthreads(1, 1, 1)]
void EvaluateProfile(uint3 id : SV_DispatchThreadID)
{
    OpenPBR_ResolvedInputs resolved;
    if (!VividTryResolveOpenPBROpaqueInputs(_Inputs[id.x], _RequestedFeatures, resolved))
    { _Output[id.x] = 0.0f.xxxx; return; }
    OpenPBR_PreparedBsdf prepared = VividPrepareOpenPBROpaque(resolved, _ViewDirection);
    OpenPBR_DiffuseSpecular response = openpbr_eval(prepared, _LightDirection);
    _Output[id.x] = float4(response.diffuse + response.specular + prepared.emission,
        openpbr_pdf(prepared, _LightDirection));
}

[numthreads(1, 1, 1)]
void SampleProfile(uint3 id : SV_DispatchThreadID)
{
    OpenPBR_ResolvedInputs resolved;
    if (!VividTryResolveOpenPBROpaqueInputs(_Inputs[id.x], _RequestedFeatures, resolved))
    { _Output[id.x] = 0.0f.xxxx; return; }
    OpenPBR_PreparedBsdf prepared = VividPrepareOpenPBROpaque(resolved, _ViewDirection);
    float3 direction;
    OpenPBR_DiffuseSpecular weight;
    float pdf;
    OpenPBR_BsdfLobeType sampledType;
    openpbr_sample(prepared, _Random, direction, weight, pdf, sampledType);
    if (pdf > 0.0f)
    {
        _Output[id.x * 2u] = float4(weight.diffuse + weight.specular, pdf);
        _Output[id.x * 2u + 1u] = float4(direction, sampledType);
    }
    else
    {
        _Output[id.x * 2u] = 0.0f.xxxx;
        _Output[id.x * 2u + 1u] = 0.0f.xxxx;
    }
}
"@
$probePath = Join-Path $outputRoot 'ProfileProbe.hlsl'
Set-Content -LiteralPath $probePath -Value $probe -Encoding utf8
$warningCount = 0
foreach ($entry in @('ValidateProfile', 'EvaluateProfile', 'SampleProfile')) {
    $arguments = @('-T', 'cs_6_0', '-E', $entry, '-Fo', (Join-Path $outputRoot "$entry.dxil"), $probePath)
    $diagnostics = & $compiler @arguments 2>&1
    $exitCode = $LASTEXITCODE
    $diagnostics | Set-Content -LiteralPath (Join-Path $outputRoot "$entry.log") -Encoding utf8
    $warningCount += @($diagnostics | Select-String -Pattern '\bwarning:').Count
    if ($exitCode -ne 0) { throw "DXC failed: $entry`n$($diagnostics -join "`n")" }
}
Write-Output 'OpenPBROpaqueV1 DXC validation passed: 3 entrypoints (validation, prepare/eval/pdf, sample).'
if ($warningCount -gt 0) {
    Write-Output "DXC warning diagnostics: $warningCount; retained in the entrypoint logs."
}
Write-Output "Validation artifacts: $outputRoot"
