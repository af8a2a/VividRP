[CmdletBinding()]
param([Parameter(Mandatory)][string]$PixInstall)
$ErrorActionPreference = 'Stop'
$packageRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$build = Join-Path $packageRoot 'PluginSource~/.build/pix'
$exe = Join-Path $build 'native/Release'
$output = Join-Path $build ('smoke-' + [Guid]::NewGuid().ToString('N'))
& "$exe/PixCaptureSmoke.exe" $PixInstall $output
if ($LASTEXITCODE) { throw 'Native GPU capture failed.' }
$cases = @(
    @{File='valid'; Session='smoke'; Pass='SmokePass'; Code='ok'},
    @{File='valid2'; Session='smoke'; Pass='SmokePass'; Code='ok'},
    @{File='marker_only'; Session='smoke'; Pass='SmokePass'; Code='no_target_gpu_work'},
    @{File='missing_end'; Session='smoke'; Pass='SmokePass'; Code='target_frame_marker_missing'},
    @{File='valid'; Session='old'; Pass='SmokePass'; Code='target_frame_marker_missing'},
    @{File='valid'; Session='smoke'; Pass='OtherPass'; Code='expected_pass_missing'},
    @{File='absent'; Session='smoke'; Pass='SmokePass'; Code='open_capture_failed'},
    @{File='async_valid'; Session='smoke'; Pass='AsyncSmokePass'; Code='ok'; Mode='all'},
    @{File='async_valid2'; Session='smoke'; Pass='AsyncSmokePass'; Code='ok'; Mode='all'},
    @{File='async_marker_only'; Session='smoke'; Pass='AsyncSmokePass'; Code='no_target_gpu_work'; Mode='all'},
    @{File='async_missing_end'; Session='smoke'; Pass='AsyncSmokePass'; Code='target_frame_marker_missing'; Mode='all'},
    @{File='valid'; Session='smoke'; Pass='SmokePass'; Code='target_frame_marker_missing'; Mode='all'},
    @{File='async_valid'; Session='smoke'; Pass='AsyncSmokePass'; Code='expected_pass_missing'; Mode='graphics'}
)
$index = 0
foreach ($case in $cases) {
    $resultPath = Join-Path $output "result-$index.json"
    $mode = if ($case.Mode) { $case.Mode } else { 'graphics' }
    & "$exe/vivid-pix-analyzer.exe" validate $PixInstall "$output/$($case.File).wpix" $case.Session $case.Pass $resultPath $mode 2> "$output/result-$index.stderr.log" | Out-Null
    $exitCode = $LASTEXITCODE
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    $expectedSuccess = $case.Code -eq 'ok'
    if ($result.success -ne $expectedSuccess -or $result.code -ne $case.Code -or ($expectedSuccess -ne ($exitCode -eq 0))) {
        throw "Unexpected result for case ${index}: $($result | ConvertTo-Json -Compress) (exit $exitCode)"
    }
    if ($expectedSuccess -and ($result.boundaryMode -ne $mode -or $result.validatedQueueScopes -ne $(if ($mode -eq 'all') { 4 } else { 1 }))) {
        throw "Missing queue boundary proof for case $index"
    }
    $index++
}
Write-Host "$index real PIX validation checks passed. Evidence: $output"
@{
    success = $true
    validationChecks = $index
    nativeProtocolChecks = 'busy ownership, stale callbacks, cancellation after managed-domain loss'
    unityInterfaceDelivery = 'simulated; real D3D12 device and production native capture code'
    pixInstall = $PixInstall
    evidenceDirectory = $output
} | ConvertTo-Json | Set-Content -LiteralPath "$output/summary.json" -Encoding utf8
# Negative cases deliberately return 2; do not leak the last expected rejection
# as the exit status of the entire successful smoke script.
exit 0
