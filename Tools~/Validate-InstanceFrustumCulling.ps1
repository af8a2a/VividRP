# Compile every production instance-culling kernel, verify half instructions,
# and execute the shared predicate on D3D12 against an independent corner oracle.
[CmdletBinding()]
param(
    [string] $DxcPath = 'dxc',
    [string] $ClangClPath = 'clang-cl',
    [string] $PythonPath = 'python',
    [string] $UnityCgIncludes = '',
    [string] $OutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
$packageRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (!$OutputDirectory) { $OutputDirectory = Join-Path $packageRoot 'Temp~/instance-frustum' }
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$dxc = (Get-Command $DxcPath).Source
$clang = (Get-Command $ClangClPath).Source
$python = (Get-Command $PythonPath).Source
if (!$UnityCgIncludes) {
    $projectRoot = (Resolve-Path -LiteralPath (Join-Path $packageRoot '../..')).Path
    $bee = Get-ChildItem (Join-Path $projectRoot 'Library/Bee/artifacts') -Directory |
        Where-Object { Test-Path (Join-Path $_.FullName 'VividRP.Runtime.rsp') } |
        Sort-Object { (Get-Item (Join-Path $_.FullName 'VividRP.Runtime.rsp')).LastWriteTimeUtc } -Descending |
        Select-Object -First 1
    if (!$bee) { throw 'Supply UnityCgIncludes, or import the containing project first.' }
    $text = Get-Content (Join-Path $bee.FullName 'VividRP.Runtime.rsp') -Raw
    $editor = [regex]::Match($text, '-r:"(?<root>[^"]*/Editor/Data)/Managed/UnityEngine/UnityEngine.CoreModule.dll"')
    if (!$editor.Success) { throw 'Cannot locate this project Editor from Bee references.' }
    $UnityCgIncludes = Join-Path $editor.Groups['root'].Value 'Resources/CGIncludes'
}
$includeRoot = Join-Path $outputRoot 'includes'
$packagesRoot = Join-Path $includeRoot 'Packages'
New-Item -ItemType Directory -Path $packagesRoot -Force | Out-Null
$aliases = @(
    @{ Name = 'com.vivid.render-pipelines'; Target = $packageRoot },
    @{ Name = 'com.unity.render-pipelines.core'; Target = (Resolve-Path (Join-Path $packageRoot '../CoreRP')).Path }
)
$createdLinks = [Collections.Generic.List[string]]::new()

function Compile-Shader([string] $Name, [string[]] $CompilerArguments) {
    $messages = @(& $dxc @CompilerArguments 2>&1)
    $code = $LASTEXITCODE
    [IO.File]::WriteAllLines((Join-Path $outputRoot ($Name + '.log')), [string[]]$messages)
    if ($code -ne 0) { throw "DXC failed for $Name`: $($messages -join "`n")" }
    Write-Output "DXC passed: $Name"
}

try {
    foreach ($alias in $aliases) {
        $path = Join-Path $packagesRoot $alias.Name
        if (!(Test-Path -LiteralPath $path)) {
            New-Item -ItemType Junction -Path $path -Target $alias.Target | Out-Null
            $createdLinks.Add($path)
        }
        $item = Get-Item -LiteralPath $path -Force
        if (!($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or @($item.Target)[0] -ne $alias.Target) {
            throw "Unexpected include alias: $path"
        }
    }
    $source = Join-Path $packageRoot 'Shaders/Core/Private/GPUDriven/GPUInstanceCulling.compute'
    foreach ($line in Get-Content $source) {
        if ($line -notmatch '^#pragma kernel (\w+)(.*)$') { continue }
        $kernel = $Matches[1]
        $defines = $Matches[2].Trim() -split '\s+'
        $arguments = @('-T', 'cs_6_2', '-enable-16bit-types', '-E', $kernel, '-HV', '2018',
            '-D', 'SHADER_API_D3D11', '-D', 'UNITY_COMPILER_HLSL', '-D', 'UNITY_COMPILER_DXC',
            '-D', 'SHADER_STAGE_COMPUTE', '-D', 'SHADER_TARGET=50',
            '-I', $includeRoot, '-I', $UnityCgIncludes,
            '-Fo', (Join-Path $outputRoot ($kernel + '.dxil')), '-Fc', (Join-Path $outputRoot ($kernel + '.asm')))
        foreach ($define in $defines) { if ($define) { $arguments += @('-D', $define) } }
        Compile-Shader $kernel ($arguments + $source)
    }
    $productionAssembly = Get-Content (Join-Path $outputRoot 'CS.asm') -Raw
    if ($productionAssembly -notmatch 'dx.op.dot3.f16') { throw 'Production CS has no FP16 dot products.' }
    if ($productionAssembly -match 'Minimum-precision data types') { throw 'Expected strict native FP16, not minimum precision.' }
} finally {
    # Delete only the links created here. Never traverse their source targets.
    foreach ($path in $createdLinks) {
        $item = Get-Item -LiteralPath $path -Force
        if (!$item.FullName.StartsWith($includeRoot + [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::OrdinalIgnoreCase) -or !($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Unexpected temporary include link: $path"
        }
        [IO.Directory]::Delete($item.FullName, $false)
    }
}

$exe = Join-Path $outputRoot 'GPUProbe.exe'
& $clang /nologo /std:c++17 /EHsc /O2 (Join-Path $PSScriptRoot 'InstanceFrustumGPUProbe.cpp') `
    ('/Fo' + (Join-Path $outputRoot 'GPUProbe.obj')) ('/Fe' + $exe) /link d3d12.lib dxgi.lib
if ($LASTEXITCODE -ne 0) { throw 'Native probe compilation failed; Windows SDK and linker are required.' }
& $python (Join-Path $PSScriptRoot 'Validate-InstanceFrustumCulling.py') generate $outputRoot
if ($LASTEXITCODE -ne 0) { throw 'Oracle generation failed.' }
foreach ($mode in @('native16', 'fp32')) {
    $dxil = Join-Path $outputRoot ($mode + '.dxil')
    $result = Join-Path $outputRoot ($mode + '.bin')
    $arguments = @('-T', 'cs_6_2', '-E', 'Main', '-Fo', $dxil)
    if ($mode -eq 'native16') { $arguments += '-enable-16bit-types' }
    if ($mode -eq 'fp32') { $arguments += @('-D', 'PROBE_FP32') }
    Compile-Shader $mode ($arguments + (Join-Path $PSScriptRoot 'InstanceFrustumProbe.hlsl'))
    & $exe $dxil (Join-Path $outputRoot 'cases.bin') $result
    if ($LASTEXITCODE -ne 0) { throw "D3D12 execution failed: $mode" }
    & $python (Join-Path $PSScriptRoot 'Validate-InstanceFrustumCulling.py') check $outputRoot --results $result
    if ($LASTEXITCODE -ne 0) { throw "Corner oracle comparison failed: $mode" }
}
Write-Output 'Production kernels and independent GPU regressions passed. No Unity tests or scene rendering were run.'
