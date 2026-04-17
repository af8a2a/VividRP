TEXTURECUBE(_SkyTexture);
SAMPLER(sampler_SkyTexture);

float4 _MipFogParameters;
float4 _FogColor;
float _FogColorMode;
float4 _SkyTextureTint;
float4 _SkyTextureParams;

#define AMBIENT_PROBE_BUFFER 1

#define _MipFogNear         _MipFogParameters.x
#define _MipFogFar          _MipFogParameters.y
#define _MipFogMaxMip       _MipFogParameters.z

#define _SkyTextureExposure _SkyTextureParams.x
#define _SkyTextureRotation _SkyTextureParams.y
#define _SkyTextureMaxMip   _SkyTextureParams.z
#define _SkyTextureEnabled  _SkyTextureParams.w
