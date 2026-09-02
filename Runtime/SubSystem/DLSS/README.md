You should clone my repo: https://github.com/af8a2a/Unity-DLSS-RR  
and build these dll with ReleaseWithDebugInfo option:
- nvngx_dlss.dll
- nvngx_dlssd.dll
- nvngx_dlssg.dll
- nvngx_dlssnr.dll
- UnityDLSS.dll


then, place these dll into this directory

`nvngx_dlssnr.dll` enables the separate **DLSS 5 Neural Rendering** camera
mode. Select it under Camera > VividRP > Anti-Aliasing. Its optional 2x path
renders at half resolution; odd output dimensions automatically use the
full-resolution path.
