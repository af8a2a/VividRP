$ErrorActionPreference = 'Stop'
# Run from the package root. Downloads data archives only; no generator executable is run.
$taskOutput = Join-Path (Get-Location).Path 'Temp~/stbn-fast'
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
$resources = @(
    @('STBN.zip', 'https://raw.githubusercontent.com/NVIDIA-RTX/STBN/48b2839e4d8b7f0202ac72c6b0ae720d235a5b8b/Assets/STBN.zip', 'f262aaa79704b913ad1ac22b11674931c5c16a788688f8ce49ec43d59eb5c747'),
    @('FAST.zip', 'https://media.githubusercontent.com/media/electronicarts/fastnoise/2cf53e4bb510d07511fe63a312556d2a2e108c70/noise.zip', '35fe2ce496a837186931acc5afedcdf53ef22d1aaf6d01ad1dcd416c5ff68cea')
)
foreach ($resource in $resources) {
    $destination = Join-Path $taskOutput $resource[0]
    if (!(Test-Path -LiteralPath $destination)) { Invoke-WebRequest $resource[1] -OutFile $destination }
    if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $resource[2]) { throw ('Archive hash mismatch: ' + $resource[0]) }
}
python (Join-Path $PSScriptRoot 'extract-textures.py')
if ($LASTEXITCODE -ne 0) { throw 'Texture extraction failed.' }
