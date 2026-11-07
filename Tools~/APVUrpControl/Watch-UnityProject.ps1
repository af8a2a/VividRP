param(
    [Parameter(Mandatory)][string]$ProjectPath,
    [Parameter(Mandatory)][string]$LogDirectory,
    [ValidateSet('Create', 'Run')][string]$Mode = 'Run',
    [double]$MemoryLimitGB = 16,
    [int]$HandleLimit = 20000
)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath($ProjectPath).TrimEnd('\', '/')
[IO.Directory]::CreateDirectory($LogDirectory) | Out-Null
$started = Get-Date
$editor = 'E:/Unity/7000.0.0a16/Editor/Unity.exe'
if ($Mode -eq 'Create') {
    if (Test-Path -LiteralPath $project) { throw "Project already exists: $project" }
    $arguments = @('projects', 'create', [IO.Path]::GetFileName($project), '--path', [IO.Path]::GetDirectoryName($project),
        '--editor-version', '7000.0.0a16', '--template', 'com.unity.template.urp-blank', '--no-cloud', '--non-interactive', '--format', 'json')
    $launcher = Start-Process -FilePath 'C:/Users/11252/AppData/Local/Unity/bin/unity.exe' -ArgumentList $arguments -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $LogDirectory 'create.stdout.log') -RedirectStandardError (Join-Path $LogDirectory 'create.stderr.log')
} else {
    if (!(Test-Path -LiteralPath (Join-Path $project 'Packages/manifest.json'))) { throw 'Not a Unity project.' }
    $arguments = @('-batchmode', '-projectPath', $project, '-force-d3d12', '-logFile', (Join-Path $LogDirectory 'editor.log'))
    $launcher = Start-Process -FilePath $editor -ArgumentList $arguments -WindowStyle Hidden -PassThru
}
@{ mode = $Mode; launcher = $launcher.Id; project = $project; started = $started.ToString('o'); memoryLimitGB = $MemoryLimitGB; handleLimit = $HandleLimit } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $LogDirectory ($Mode.ToLower() + '.watchdog.json'))
$metrics = Join-Path $LogDirectory ($Mode.ToLower() + '.metrics.jsonl')
while (!$launcher.HasExited) {
    # Inspect command lines only to select this owned project; never log them (they may contain credentials).
    $owned = @(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object {
        $_.CreationDate -ge $started.AddSeconds(-2) -and $_.CommandLine -and
        $_.CommandLine.IndexOf($project, [StringComparison]::OrdinalIgnoreCase) -ge 0
    })
    $processes = @($owned | ForEach-Object { Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue })
    $memory = ($processes | Measure-Object PrivateMemorySize64 -Sum).Sum
    $handles = ($processes | Measure-Object HandleCount -Sum).Sum
    @{ time = (Get-Date).ToString('o'); ids = @($processes.Id); privateGB = [math]::Round($memory / 1GB, 3); handles = $handles } |
        ConvertTo-Json -Compress | Add-Content -LiteralPath $metrics
    if ($memory -gt $MemoryLimitGB * 1GB -or $handles -gt $HandleLimit) {
        foreach ($process in $processes) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
        if (!$launcher.HasExited) { Stop-Process -Id $launcher.Id -Force -ErrorAction SilentlyContinue }
        throw "Diagnostic Unity exceeded the measured startup limits. Stopped only project $project."
    }
    Start-Sleep -Seconds 2
    $launcher.Refresh()
}
if ($launcher.ExitCode -ne 0) { throw "Unity launcher exited with $($launcher.ExitCode). See logs in $LogDirectory." }
Write-Output "$Mode completed for $project"
