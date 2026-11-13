param(
    [string]$Project = 'E:/UnrealProjects/VividGICornellReference/VividGICornellReference.uproject',
    [string]$Editor = 'E:/UnrealEngine/Engine/Binaries/Win64/UnrealEditor.exe',
    [string]$Output = 'E:/UnrealProjects/VividGICornellReference/Saved/LinearGI-20261113/physical-clamp',
    [ValidateRange(0.001,4)][double]$PreExposure = 0.0625,
    [int]$WarmupFrames = 512,
    [ValidatePattern('^[a-zA-Z0-9_-]*$')][string]$CaseName = '',
    [string[]]$DiagnosticCommands = @()
)
$ErrorActionPreference = 'Stop'
$numberFormat = [System.Globalization.CultureInfo]::InvariantCulture
$preExposureText = $PreExposure.ToString('G', $numberFormat)
$clampScale = $PreExposure / 0.0625
$probeMax = (10 * $clampScale).ToString('G', $numberFormat)
$radiosityMax = (40 * $clampScale).ToString('G', $numberFormat)
$reflectionMax = [int][math]::Round(40 * $clampScale)
$caseDirectory = Join-Path $Output "preexposure_$preExposureText"
if ($CaseName) { $caseDirectory = Join-Path $Output $CaseName }
if (Test-Path -LiteralPath $caseDirectory) { throw "Refusing to overwrite $caseDirectory" }
New-Item -ItemType Directory -Path $caseDirectory -Force | Out-Null
$commandList = @(
    'r.ScreenPercentage 100', 'r.DynamicRes.OperationMode 0',
    'sg.GlobalIlluminationQuality 3', 'sg.ReflectionQuality 3',
    'r.Lumen.HardwareRayTracing 0', 'r.Lumen.TraceMeshSDFs 1',
    'r.Lumen.ScreenProbeGather.ShortRangeAO 0', 'r.AmbientOcclusionLevels 0',
    'r.SetNearClipPlane 10', "r.EyeAdaptation.PreExposureOverride $preExposureText",
    "r.Lumen.ScreenProbeGather.MaxRayIntensity $probeMax",
    "r.LumenScene.Radiosity.MaxRayIntensity $radiosityMax",
    "r.Lumen.Reflections.MaxRayIntensity $reflectionMax"
) + $DiagnosticCommands + @(
    'r.DumpGPU.Root DiffuseIndirectComposite*', 'r.DumpGPU.Texture 2',
    'r.DumpGPU.Buffer 0', 'r.DumpGPU.Upload 0', 'r.DumpGPU.Explore 0',
    "r.DumpGPU.Directory $caseDirectory/dump", "r.DumpGPU.FrameDelay $WarmupFrames", 'DumpGPU'
)
$commands = $commandList -join ','
# Benchmark uses 30 simulation ticks per second in this build. Keep ample room after the dump.
$seconds = [math]::Ceiling(($WarmupFrames + 128) / 30.0)
$captureMap = '/Game/VividReference/Maps/CornellBoxEmissiveCeiling?game=/Game/VividReference/Capture/BP_LinearGICaptureGameMode.BP_LinearGICaptureGameMode_C'
$arguments = @($Project, $captureMap,
    '-game', '-RenderOffscreen', '-unattended', '-nop4', '-nosplash', '-nosound', '-d3d12',
    '-windowed', '-ResX=1920', '-ResY=1080', '-ForceRes', '-benchmark', "-seconds=$seconds",
    ('-ExecCmds="' + $commands + '"'), "-abslog=$caseDirectory/capture.log")
@{ engine = 'Unreal 5.7.4'; pre_exposure = $PreExposure; width = 1920; height = 1080;
   quantity = 'irradiance_over_pi'; receiving_albedo_in_rgb = $false; short_range_ao = $false;
   camera = @(-380,0,150); near_clip_cm = 10; warmup_frames = $WarmupFrames;
   probe_max_ray_intensity = [double]$probeMax; radiosity_max_ray_intensity = [double]$radiosityMax;
   reflection_max_ray_intensity = $reflectionMax; physical_clamp_policy = 'Preserve EV4 native cutoffs (10/40/40 at pre-exposure 1/16)';
   diagnostic_commands = $DiagnosticCommands; case_name = $CaseName;
   root_filter = 'DiffuseIndirectComposite*'; project = $Project; map = $captureMap
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$caseDirectory/capture-request.json"
$process = Start-Process -FilePath $Editor -ArgumentList $arguments -WindowStyle Hidden -PassThru
$process.Id | Set-Content -LiteralPath "$caseDirectory/process-id.txt"
Write-Output "Linear GI capture PID $($process.Id): $caseDirectory"
$deadline = [DateTime]::UtcNow.AddMinutes(8)
while (-not $process.HasExited -and [DateTime]::UtcNow -lt $deadline) {
    Start-Sleep -Milliseconds 500
    $process.Refresh()
}
if (-not $process.HasExited) {
    # Only the diagnostic process started above is owned by this script.
    $process.Kill()
    throw 'Diagnostic Unreal capture timed out.'
}
if ($process.ExitCode -ne 0) { throw "Diagnostic Unreal exited with $($process.ExitCode)" }
$status = Get-ChildItem -LiteralPath "$caseDirectory/dump" -Filter Status.txt -Recurse |
    Where-Object { (Get-Content -LiteralPath $_.FullName -Raw).Trim() -eq 'ok' }
if (-not $status) { throw 'GPU resource dump did not complete.' }
Write-Output "Captured $caseDirectory"
