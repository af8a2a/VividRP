#ifndef VIVIDRP_LIGHTING_SHADERVARIABLESGLOBALLIGHTLOOP_INCLUDED
#define VIVIDRP_LIGHTING_SHADERVARIABLESGLOBALLIGHTLOOP_INCLUDED

CBUFFER_START(ShaderVariablesGlobalLightLoop)
    float g_fClustScale;
    float g_fClustBase;
    float g_fNearPlane;
    float g_fFarPlane;
    int g_iLog2NumClusters;
    uint g_isLogBaseBufferEnabled;
    uint _NumTileClusteredX;
    uint _NumTileClusteredY;
CBUFFER_END

#endif
