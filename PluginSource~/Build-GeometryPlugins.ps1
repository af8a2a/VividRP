[CmdletBinding()]
param(
    [string] $Generator
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Generator) -and
    (Get-Command ninja -ErrorAction SilentlyContinue) -and
    (Get-Command clang-cl -ErrorAction SilentlyContinue) -and
    (Get-Command lld-link -ErrorAction SilentlyContinue)) {
    $Generator = 'Ninja'
}

function Invoke-CMake {
    param([string[]] $Arguments)

    & cmake @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "cmake $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Get-ConfigureArguments {
    param(
        [string] $SourceDirectory,
        [string] $BuildDirectory
    )

    $arguments = @('-S', $SourceDirectory, '-B', $BuildDirectory)
    if ([string]::IsNullOrWhiteSpace($Generator)) {
        return $arguments + @('-A', 'x64')
    }

    $arguments += @('-G', $Generator)
    if ($Generator -like 'Visual Studio*') {
        $arguments += @('-A', 'x64')
    }
    else {
        $arguments += '-DCMAKE_BUILD_TYPE=Release'
        if ($Generator -eq 'Ninja' -and
            (Get-Command clang-cl -ErrorAction SilentlyContinue) -and
            (Get-Command lld-link -ErrorAction SilentlyContinue)) {
            $arguments += @(
                '-DCMAKE_C_COMPILER=clang-cl',
                '-DCMAKE_CXX_COMPILER=clang-cl',
                '-DCMAKE_LINKER=lld-link'
            )
        }
    }

    return $arguments
}

$toolchainName = if ([string]::IsNullOrWhiteSpace($Generator)) {
    'Default'
}
else {
    $Generator -replace '[^A-Za-z0-9_.-]', '_'
}
$buildRoot = Join-Path (Join-Path $PSScriptRoot '.build') $toolchainName
$meshOptimizerSource = Join-Path $PSScriptRoot 'MeshOptimizer~'
$meshOptimizerBuild = Join-Path $buildRoot 'MeshOptimizer'

Invoke-CMake (Get-ConfigureArguments $meshOptimizerSource $meshOptimizerBuild)
Invoke-CMake @('--build', $meshOptimizerBuild, '--config', 'Release', '--target', 'meshoptimizer')

$meshOptimizerDll = Join-Path $meshOptimizerBuild 'bin/Release/meshoptimizer.dll'
if (-not (Test-Path -LiteralPath $meshOptimizerDll -PathType Leaf)) {
    $meshOptimizerDll = Join-Path $meshOptimizerBuild 'bin/meshoptimizer.dll'
}
if (-not (Test-Path -LiteralPath $meshOptimizerDll -PathType Leaf)) {
    throw "MeshOptimizer build succeeded without producing meshoptimizer.dll."
}

$packageRoot = Split-Path $PSScriptRoot -Parent
$meshOptimizerDestination = Join-Path $packageRoot 'Runtime/SubSystem/Plugin/MeshOptimizer/Plugins/x86_64/meshoptimizer.dll'

Copy-Item -LiteralPath $meshOptimizerDll -Destination $meshOptimizerDestination -Force

Get-FileHash -Algorithm SHA256 $meshOptimizerDestination |
    Select-Object Path, Hash
