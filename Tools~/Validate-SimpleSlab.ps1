# Focused DXC validation for the frozen Simple Slab implementation.
# This does not launch Unity, run GPU tests or modify imported assets.
[CmdletBinding()]
param(
    [string] $DxcPath = 'dxc',
    [string] $CorePackageRoot = (Join-Path $PSScriptRoot '../../CoreRP')
)

$ErrorActionPreference = 'Stop'
$packageRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$coreRoot = (Resolve-Path -LiteralPath $CorePackageRoot).Path
$compiler = (Get-Command $DxcPath -ErrorAction Stop).Source
if (!(Test-Path -LiteralPath (Join-Path $coreRoot 'ShaderLibrary/Common.hlsl'))) {
    throw "Core RP ShaderLibrary not found in $coreRoot"
}

# A unique temporary include tree resolves the real package paths used by Unity.
# Junctions are removed non-recursively; their source targets are never traversed.
$includeRoot = Join-Path ([IO.Path]::GetTempPath()) ('VividSimpleSlab-' + [Guid]::NewGuid().ToString('N'))
$includePackages = Join-Path $includeRoot 'Packages'
$links = @(
    (Join-Path $includePackages 'com.vivid.render-pipelines'),
    (Join-Path $includePackages 'com.unity.render-pipelines.core')
)
$targets = @($packageRoot, $coreRoot)
$script:compiledCount = 0

function Compile-Entry([string] $RelativePath, [string] $Entry, [string] $Stage, [string] $Variant = '') {
    # Core RP's D3D11 path uses HLSL 2018 vector conditionals. Do not inherit
    # standalone DXC's HLSL 2021 default (which requires select/and/or instead).
    $arguments = @('-T', ($Stage + '_6_0'), '-E', $Entry, '-HV', '2018',
        '-D', 'SHADER_API_D3D11', '-D', 'UNITY_COMPILER_HLSL',
        '-I', $includeRoot, '-Fo', 'NUL')
    $stageDefine = switch ($Stage) {
        'cs' { 'SHADER_STAGE_COMPUTE' }
        'vs' { 'SHADER_STAGE_VERTEX' }
        'ps' { 'SHADER_STAGE_FRAGMENT' }
    }
    $arguments += @('-D', $stageDefine)
    if ($Variant) { $arguments += @('-D', $Variant) }
    & $compiler @arguments (Join-Path $packageRoot $RelativePath)
    if ($LASTEXITCODE -ne 0) {
        throw "DXC failed: $RelativePath / $Entry / $Variant"
    }
    $script:compiledCount++
}

try {
    New-Item -ItemType Directory -Path $includePackages | Out-Null
    for ($i = 0; $i -lt $links.Length; ++$i) {
        New-Item -ItemType Junction -Path $links[$i] -Target $targets[$i] | Out-Null
    }
    foreach ($variant in @('', 'PROBE_VOLUMES_L1', 'PROBE_VOLUMES_L2')) {
        foreach ($entry in @('ClearDeferredLit', 'DeferredLit_Variant0',
                'DeferredLit_Variant1', 'DeferredLit_Variant2', 'DeferredLit_Variant3')) {
            Compile-Entry 'Shaders/Material/DeferredLit.compute' $entry 'cs' $variant
        }
        foreach ($path in @('Shaders/Material/ShaderPass/SimpleDeferredLitPass.hlsl',
                'Shaders/Material/DeferredDirectionalLightingIndirectPass.hlsl')) {
            Compile-Entry $path 'Vert' 'vs' $variant
            Compile-Entry $path 'Frag' 'ps' $variant
        }
    }
    foreach ($entry in @('ClearDeferredVariantArgs', 'ClassifyDeferredExports', 'BuildDeferredVariantIndirectArgs')) {
        Compile-Entry 'Shaders/Material/MaterialClassification.compute' $entry 'cs'
    }
    # Bake and all focused BSDF/LUT/pixel-reference kernels.
    foreach ($path in @(
        'Shaders/Core/Private/VividSlabLut.compute',
        'Tests/Editor/SubSystem/GPUDriven/SimpleSlabBSDFTests.compute',
        'Tests/Editor/SubSystem/GPUDriven/SimpleSlabDirectLightingTests.compute',
        'Tests/Editor/SubSystem/GPUDriven/SimpleSlabEnergyTests.compute',
        'Tests/Editor/SubSystem/GPUDriven/SimpleSlabDeferredLightingTests.compute'
    )) {
        $source = Get-Content -Raw -LiteralPath (Join-Path $packageRoot $path)
        foreach ($match in [regex]::Matches($source, '(?m)^#pragma kernel (\w+)')) {
            Compile-Entry $path $match.Groups[1].Value 'cs'
        }
    }
    Write-Output "Simple Slab DXC validation passed: $script:compiledCount entrypoints/variants."
}
finally {
    foreach ($path in $links) {
        if (Test-Path -LiteralPath $path) {
            $link = Get-Item -LiteralPath $path -Force
            if (!$link.FullName.StartsWith($includeRoot + [IO.Path]::DirectorySeparatorChar,
                    [StringComparison]::OrdinalIgnoreCase) -or
                !($link.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw "Unexpected temporary include link: $path"
            }
            [IO.Directory]::Delete($link.FullName, $false)
        }
    }
    # Delete only the exact, newly-created empty directories.
    foreach ($path in @($includePackages, $includeRoot)) {
        if (Test-Path -LiteralPath $path) { [IO.Directory]::Delete($path, $false) }
    }
}
