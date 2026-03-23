param(
    [string]$ProjectRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Copy-IfDifferent
{
    param(
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][string]$DestinationPath
    )

    if (!(Test-Path -LiteralPath $SourcePath))
    {
        throw "Missing source file: $SourcePath"
    }

    $destinationExists = Test-Path -LiteralPath $DestinationPath
    if ($destinationExists)
    {
        $sourceHash = (Get-FileHash -LiteralPath $SourcePath -Algorithm SHA256).Hash
        $destinationHash = (Get-FileHash -LiteralPath $DestinationPath -Algorithm SHA256).Hash
        if ($sourceHash -eq $destinationHash)
        {
            Write-Host "Up-to-date: $DestinationPath"
            return $false
        }
    }

    Copy-Item -LiteralPath $SourcePath -Destination $DestinationPath -Force
    Write-Host "Copied: $DestinationPath"
    return $true
}

function Remove-IfExists
{
    param(
        [Parameter(Mandatory = $true)][string]$Path
    )

    if (Test-Path -LiteralPath $Path)
    {
        Remove-Item -LiteralPath $Path -Force
        Write-Host "Removed stale copy: $Path"
    }
}

function New-MetaFromTemplate
{
    param(
        [Parameter(Mandatory = $true)][string]$TemplatePath,
        [Parameter(Mandatory = $true)][string]$DestinationMetaPath
    )

    if (Test-Path -LiteralPath $DestinationMetaPath)
    {
        Write-Host "Preserved existing meta: $DestinationMetaPath"
        return
    }

    $guid = [guid]::NewGuid().ToString("N")
    $templateContent = Get-Content -LiteralPath $TemplatePath -Raw
    $templateContent = $templateContent.Replace("__GUID__", $guid)
    [System.IO.File]::WriteAllText($DestinationMetaPath, $templateContent, [System.Text.Encoding]::UTF8)
    Write-Host "Created meta: $DestinationMetaPath"
}

$packageRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ProjectRoot))
{
    $ProjectRoot = (Resolve-Path (Join-Path $packageRoot "..\..")).Path
}
else
{
    $ProjectRoot = (Resolve-Path $ProjectRoot).Path
}

$assetsPath = Join-Path $ProjectRoot "Assets"
$projectSettingsPath = Join-Path $ProjectRoot "ProjectSettings"
if (!(Test-Path -LiteralPath $assetsPath) -or !(Test-Path -LiteralPath $projectSettingsPath))
{
    throw "Project root is invalid: $ProjectRoot"
}

$sourceDir = Join-Path $packageRoot "BindlessNative~\Windows\x86_64"
$sourceDllPath = Join-Path $sourceDir "UnityBindless.dll"
$sourcePdbPath = Join-Path $sourceDir "UnityBindless.pdb"
$sourceMetaTemplatePath = Join-Path $sourceDir "UnityBindless.dll.meta.template"

$destinationDir = Join-Path $ProjectRoot "Assets\Plugins\VividRP\x86_64"
$destinationDllPath = Join-Path $destinationDir "UnityBindless.dll"
$destinationPdbPath = Join-Path $destinationDir "UnityBindless.pdb"
$destinationMetaPath = Join-Path $destinationDir "UnityBindless.dll.meta"

New-Item -ItemType Directory -Force -Path $destinationDir | Out-Null

$stalePaths = @(
    (Join-Path $ProjectRoot "Assets\UnityBindless.dll"),
    (Join-Path $ProjectRoot "Assets\UnityBindless.dll.meta"),
    (Join-Path $ProjectRoot "Assets\UnityBindless.pdb"),
    (Join-Path $ProjectRoot "Assets\VividRPGenerated\Plugins\x86_64\UnityBindless.dll"),
    (Join-Path $ProjectRoot "Assets\VividRPGenerated\Plugins\x86_64\UnityBindless.dll.meta"),
    (Join-Path $ProjectRoot "Assets\VividRPGenerated\Plugins\x86_64\UnityBindless.pdb")
)

foreach ($stalePath in $stalePaths)
{
    Remove-IfExists -Path $stalePath
}

$copiedDll = Copy-IfDifferent -SourcePath $sourceDllPath -DestinationPath $destinationDllPath
if (Test-Path -LiteralPath $sourcePdbPath)
{
    Copy-IfDifferent -SourcePath $sourcePdbPath -DestinationPath $destinationPdbPath | Out-Null
}

New-MetaFromTemplate -TemplatePath $sourceMetaTemplatePath -DestinationMetaPath $destinationMetaPath

if ($copiedDll)
{
    Write-Host ""
    Write-Host "Bindless native plugin is ready at: $destinationDllPath"
}
else
{
    Write-Host ""
    Write-Host "Bindless native plugin was already up-to-date at: $destinationDllPath"
}

Write-Host "Next step: restart Unity so the project-local preloaded plugin can hook D3D12 before device startup."
