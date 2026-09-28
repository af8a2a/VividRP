[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PixInstall,
    [switch]$Deploy,
    [switch]$BuildGpuSmoke
)
$ErrorActionPreference = 'Stop'
$packageRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$buildRoot = Join-Path $packageRoot 'PluginSource~/.build/pix'
$PixInstall = (Resolve-Path -LiteralPath $PixInstall).Path
if (-not (Test-Path -LiteralPath "$PixInstall/include/PixApi.h")) { throw 'PIX API Preview SDK is missing.' }

function Get-PinnedPackage([string]$Id, [string]$Version, [string]$Directory, [string]$Hash) {
    $cache = Join-Path $buildRoot 'deps'
    New-Item -ItemType Directory -Force -Path $cache | Out-Null
    $zip = Join-Path $cache "$Id.$Version.zip"
    if (-not (Test-Path -LiteralPath $zip)) {
        Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/$Id/$Version/$Id.$Version.nupkg" -OutFile $zip
    }
    if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $Hash) { throw "Hash mismatch: $zip" }
    $destination = Join-Path $cache $Directory
    # Re-extract the verified archive so a partially extracted/modified cache
    # cannot silently become a different build dependency.
    Expand-Archive -LiteralPath $zip -DestinationPath $destination -Force
    return $destination
}

$events = Get-PinnedPackage 'winpixeventruntime' '1.0.240308001' 'WinPixEventRuntime' '726ACC93D6968E2146261A1E415521747D50AD69894C2B42B5D0D4C29FD66EC4'
$d3d = Get-PinnedPackage 'microsoft.direct3d.d3d12' '1.721.1-preview' 'D3D12' '6D6038B615E82F150F4F449791EB33AC74BDB97ABA9EEAC70EB9FB63CB21A553'
$native = Join-Path $buildRoot 'native'
$smoke = if ($BuildGpuSmoke) { 'ON' } else { 'OFF' }
& cmake -S $PSScriptRoot -B $native -A x64 "-DPIX_INSTALL_DIR=$PixInstall" "-DPIX_EVENT_RUNTIME_DIR=$events" "-DPIX_D3D12_DIR=$d3d" "-DPIX_BUILD_GPU_SMOKE=$smoke"
if ($LASTEXITCODE) { throw 'CMake configure failed.' }
& cmake --build $native --config Release --parallel
if ($LASTEXITCODE) { throw 'Native build failed.' }
& ctest --test-dir $native -C Release --output-on-failure
if ($LASTEXITCODE) { throw 'Native validation checks failed.' }
& dotnet run --project (Join-Path $packageRoot 'Editor/AgenticDebugger/Tests~/PixChecks/PixChecks.csproj')
if ($LASTEXITCODE) { throw 'PIX session checks failed.' }
$projectRoot = [IO.Path]::GetFullPath((Join-Path $packageRoot '../..'))
$newtonsoft = Get-ChildItem -Path "$projectRoot/Library/PackageCache/com.unity.nuget.newtonsoft-json@*/Runtime/Newtonsoft.Json.dll" | Select-Object -First 1
if (-not $newtonsoft) { throw 'Open/resolve the containing Unity project first: Newtonsoft.Json reference is missing for M1 checks.' }
& dotnet run --project (Join-Path $packageRoot 'Editor/AgenticDebugger/Tests~/M1Checks/M1Checks.csproj') "-p:NewtonsoftPath=$($newtonsoft.FullName)"
if ($LASTEXITCODE) { throw 'M1 pipeline hook/queue/router checks failed.' }
& dotnet run --project (Join-Path $packageRoot 'Editor/AgenticDebugger/Tests~/M2Checks/M2Checks.csproj') "-p:NewtonsoftPath=$($newtonsoft.FullName)"
if ($LASTEXITCODE) { throw 'M2 analysis process/evidence checks failed.' }

if ($Deploy) {
    $plugin = Join-Path $packageRoot 'Editor/AgenticDebugger/Plugins/x86_64'
    $tool = Join-Path $packageRoot 'Tools~/PIX/bin'
    New-Item -ItemType Directory -Force -Path $plugin,$tool | Out-Null
    # Identical loaded DLLs need no replacement; analyzer-only iterations can be
    # deployed while the Editor is running. Changed native DLLs still fail if locked.
    foreach ($source in @("$native/Release/VividPixCapture.dll", "$events/bin/x64/WinPixEventRuntime.dll")) {
        $target = Join-Path $plugin ([IO.Path]::GetFileName($source))
        if (-not (Test-Path -LiteralPath $target) -or (Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $target).Hash) {
            Copy-Item -LiteralPath $source -Destination $target
        }
    }
    $analyzerSource = "$native/Release/vivid-pix-analyzer.exe"
    $analyzerTarget = "$tool/vivid-pix-analyzer.exe"
    if (-not (Test-Path -LiteralPath $analyzerTarget) -or (Get-FileHash -LiteralPath $analyzerSource).Hash -ne (Get-FileHash -LiteralPath $analyzerTarget).Hash) {
        Copy-Item -LiteralPath $analyzerSource -Destination $analyzerTarget
    }
    $license = Join-Path $events 'License.txt'
    if (Test-Path -LiteralPath $license) { Copy-Item -LiteralPath $license -Destination "$plugin/WinPixEventRuntime.License.txt" }
    $manifest = [ordered]@{
        pixInstall = $PixInstall
        pixApiSha256 = (Get-FileHash -LiteralPath "$PixInstall/pixapi.dll").Hash
        pixApiHeaderSha256 = (Get-FileHash -LiteralPath "$PixInstall/include/PixApi.h").Hash
        winPixEventRuntime = '1.0.240308001'
        d3d12Headers = '1.721.1-preview'
        analyzer = "$tool/vivid-pix-analyzer.exe"
        analyzerSha256 = (Get-FileHash -LiteralPath "$tool/vivid-pix-analyzer.exe").Hash
        nativePluginSha256 = (Get-FileHash -LiteralPath "$plugin/VividPixCapture.dll").Hash
    }
    $manifest | ConvertTo-Json | Set-Content -LiteralPath "$tool/build-manifest.json" -Encoding utf8
    Write-Host "Deployed optional PIX backend: $tool"
}
