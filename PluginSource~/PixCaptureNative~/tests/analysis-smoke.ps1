[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PixInstall,
    [Parameter(Mandatory)][string]$CaptureDirectory
)
$ErrorActionPreference = 'Stop'
$packageRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$exe = Join-Path $packageRoot 'PluginSource~/.build/pix/native/Release/vivid-pix-analyzer.exe'
$CaptureDirectory = (Resolve-Path -LiteralPath $CaptureDirectory).Path
$capture = Join-Path $CaptureDirectory 'async_valid.wpix'
$digest = (Get-FileHash -LiteralPath $capture -Algorithm SHA256).Hash.ToLowerInvariant()
$output = Join-Path $packageRoot ('PluginSource~/.build/pix/analysis-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output | Out-Null
$script:checks = 0
function Assert-Result([bool]$Value, [string]$Message) {
    if (-not $Value) { throw $Message }
    $script:checks++
}
function Invoke-Analysis([string]$Name, [string]$Action, [string[]]$Options = @(), [string]$Code = 'ok',
    [string]$File = $capture, [string]$Session = 'smoke', [string]$Pass = 'AsyncSmokePass') {
    $resultPath = Join-Path $output ($Name + '.json')
    & $exe $Action $PixInstall $File $Session $Pass $resultPath all --request_id $Name --timeout_seconds 60 @Options 2> "$output/$Name.stderr.log" | Out-Null
    $nativeExit = $LASTEXITCODE
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    Assert-Result ($result.schemaVersion -eq 3 -and $result.requestId -eq $Name -and $result.action -eq $Action -and
        $result.sessionId -eq $Session -and $result.capturePath -eq $File -and $result.expectedPass -eq $Pass -and $result.boundaryMode -eq 'all') "Identity mismatch: $Name"
    if ($Code -eq 'optional_occupancy') {
        Assert-Result (($result.success -and $nativeExit -eq 0) -or ($result.unsupported -and $nativeExit -eq 2 -and $result.hresult -lt 0)) 'Occupancy must succeed or explicitly report unsupported.'
    } else {
        Assert-Result ($result.code -eq $Code -and $result.success -eq ($Code -eq 'ok') -and (($nativeExit -eq 0) -eq ($Code -eq 'ok'))) "Unexpected $Name result: $($result.code), exit $nativeExit"
    }
    if ($File -eq $capture) { Assert-Result ($result.captureHash -eq $digest) "Digest mismatch: $Name" }
    return $result
}
$events = Invoke-Analysis 'events' 'events' -Options @('--marker','AsyncSmokePass','--count','256')
$dispatch = $events.data.items | Where-Object name -Match '^Dispatch' | Select-Object -First 1
Assert-Result ($null -ne $dispatch) 'Expected nonzero dispatch event.'
$selection = @('--queue', [string]$dispatch.queueId, '--event', [string]$dispatch.eventIndex, '--capture_hash', $digest)
$page = Invoke-Analysis 'page-1' 'events' -Options @('--marker','AsyncSmokePass','--count','1')
$next = Invoke-Analysis 'page-2' 'events' -Options @('--marker','AsyncSmokePass','--count','1','--offset',[string]$page.data.nextOffset)
Assert-Result ($page.data.items.Count -eq 1 -and $next.data.items.Count -eq 1 -and $page.data.items[0].eventIndex -ne $next.data.items[0].eventIndex) 'Pagination repeated an event.'
$empty = Invoke-Analysis 'empty-page' 'events' -Options @('--offset','100000')
Assert-Result ($empty.data.items.Count -eq 0 -and $null -eq $empty.data.nextOffset) 'Out-of-range page must be empty.'
$event = Invoke-Analysis 'event' 'event' -Options $selection
Assert-Result ($event.data.eventIndex -eq $dispatch.eventIndex -and $event.data.apiCallData.Length -gt 0) 'Missing event API evidence.'
$pipeline = Invoke-Analysis 'pipeline' 'pipeline' -Options $selection
Assert-Result ($pipeline.data.rootSignature.description.parameterCount -eq 1 -and $pipeline.data.shaderCount -ge 1 -and $pipeline.data.subobjectCount -ge 1) 'Missing pipeline/root/shader identity.'
$resources = Invoke-Analysis 'resources' 'resources' -Options $selection
Assert-Result ($resources.data.access -eq 'static_bindings_only' -and $resources.data.views.items[0].resource.width -eq '256') 'Missing UAV resource binding.'
$inventory = Invoke-Analysis 'inventory' 'resources' -Options @('--count','1')
Assert-Result ($inventory.data.items.Count -eq 1 -and $inventory.data.total -gt 0) 'Missing resource inventory.'
$accessed = Invoke-Analysis 'accessed' 'accessed_resources' -Options $selection
Assert-Result ($accessed.data.access -eq 'replay_accessed') 'Static bindings mislabeled as dynamic accesses.'
$timing = Invoke-Analysis 'timing' 'timing' -Options $selection
Assert-Result ($timing.data.available -and $timing.data.unit -eq 'nanoseconds' -and $null -ne $timing.data.timing.eopDuration) 'Missing event timing.'
$counters = Invoke-Analysis 'counters' 'counters' -Options @('--count','256')
$counter = $counters.data.items | Where-Object name -EQ 'CS Invocations' | Select-Object -First 1
Assert-Result ($null -ne $counter) 'CS counter missing from catalog.'
$value = Invoke-Analysis 'counter-value' 'counters' -Options ($selection + @('--counter',[string]$counter.id))
Assert-Result ($value.data.available -and $value.data.value.rawBits -match '^\d+$') 'Missing raw counter evidence.'
Assert-Result ($value.data.value.decoded -or $null -eq $value.data.value.valueText) 'Unknown counter encoding must not fabricate a value.'
$occupancy = Invoke-Analysis 'occupancy' 'occupancy' -Code 'optional_occupancy'
$drpix = Invoke-Analysis 'drpix' 'drpix'
Assert-Result ($drpix.data.total -gt 0) 'Missing Dr. PIX catalog.'
$experiment = Invoke-Analysis 'experiment' 'drpix' -Options ($selection + @('--experiment', [string]$drpix.data.items[0].guid))
Assert-Result ($experiment.data.metrics.total -gt 0) 'Missing Dr. PIX metrics.'
$null = Invoke-Analysis 'stale-session' 'events' -Session 'old' -Code 'capture_validation_failed'
$null = Invoke-Analysis 'wrong-pass' 'events' -Pass 'OtherPass' -Code 'capture_validation_failed'
$null = Invoke-Analysis 'missing-boundary' 'events' -File (Join-Path $CaptureDirectory 'async_missing_end.wpix') -Code 'capture_validation_failed'
$null = Invoke-Analysis 'empty-capture' 'events' -File (Join-Path $CaptureDirectory 'async_marker_only.wpix') -Code 'capture_validation_failed'
$null = Invoke-Analysis 'wrong-queue' 'event' -Options @('--queue','4294967295','--event','6') -Code 'event_not_found'
$null = Invoke-Analysis 'wrong-hash' 'events' -Options @('--capture_hash', ('0' * 64)) -Code 'capture_hash_mismatch'
$saved = (Get-FileHash -LiteralPath "$output/events.json").Hash
& $exe events $PixInstall $capture smoke AsyncSmokePass "$output/events.json" all 2> "$output/no-overwrite.stderr.log" | Out-Null
Assert-Result ($LASTEXITCODE -ne 0 -and (Get-FileHash -LiteralPath "$output/events.json").Hash -eq $saved) 'Existing evidence was overwritten.'
@{
    success = $true; checks = $script:checks; capturePath = $capture; captureHash = $digest
    occupancySupported = [bool]$occupancy.success; occupancyHresult = $occupancy.hresult
    counterFormat = $value.data.value.format; counterDecoded = $value.data.value.decoded
    unityExecution = 'not exercised; real standalone D3D12 capture and replay'
    evidenceDirectory = $output
} | ConvertTo-Json | Set-Content -LiteralPath "$output/summary.json" -Encoding utf8
Write-Host "$script:checks real PIX analysis checks passed. Occupancy supported: $($occupancy.success). Evidence: $output"
exit 0
