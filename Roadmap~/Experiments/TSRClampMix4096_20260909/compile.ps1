$ErrorActionPreference='Stop'
$bndRoot=(Get-Location).Path
$bndOutput=Join-Path $bndRoot 'Temp~/tsr-clamp-compare/dxc'
New-Item -ItemType Directory -Path $bndOutput -Force | Out-Null
$bndInclude=Join-Path $bndRoot 'Temp~/vsm-bnd-compare/dxc2/include'
$bndResults=@(foreach($bndShader in @('TSRRejectShading','TSRUpdateHistory','TSRResolveHistory','TSRReprojectHistory')) {
 $bndSource=Join-Path $bndRoot ('Shaders/Core/Private/TSR/'+$bndShader+'.compute')
 foreach($bndWave in 0,1){foreach($bndHalf in 0,1){
  $bndName=$bndShader+'_wave'+$bndWave+'_half'+$bndHalf
  $bndArgs=@('-T','cs_6_2','-E','CS','-D','SHADER_API_D3D11=1','-D','UNITY_COMPILER_DXC=1','-enable-16bit-types','-Wno-conversion','-I',$bndInclude,'-Fo',(Join-Path $bndOutput ($bndName+'.dxil')))
  if($bndWave){$bndArgs+=@('-D','VIVID_TSR_WAVE_OPS=1')};if($bndHalf){$bndArgs+=@('-D','UNITY_DEVICE_SUPPORTS_NATIVE_16BIT=1')}
  & 'C:/VulkanSDK/1.4.350.0/Bin/dxc.exe' @bndArgs $bndSource *> (Join-Path $bndOutput ($bndName+'.log'))
  [ordered]@{variant=$bndName;exitCode=$LASTEXITCODE;sourceSha256=(Get-FileHash -LiteralPath $bndSource).Hash}
 }}
})
$bndReport=[ordered]@{checkedUtc=[DateTime]::UtcNow.ToString('o');total=$bndResults.Count;passed=@($bndResults|Where-Object exitCode -eq 0).Count;results=$bndResults;unityTestFrameworkRun=$false}
$bndReport|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $bndOutput 'validation.json') -Encoding utf8
$bndReport|Select-Object checkedUtc,total,passed|ConvertTo-Json
if(@($bndResults|Where-Object exitCode -ne 0).Count){exit 1}
