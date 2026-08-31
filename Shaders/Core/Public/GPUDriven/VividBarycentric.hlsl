#ifndef VIVIDRP_BARYCENTRIC_INCLUDED
#define VIVIDRP_BARYCENTRIC_INCLUDED

struct VividBarycentricDerivatives
{
    float3 lambda;
    float3 ddx;
    float3 ddy;
};

float2 ScreenCoordsToNDC(const float2 screenCoords)
{
    float2 ndc = screenCoords.xy * _ScreenSize.zw * 2.0f - 1.0f;
    #ifdef UNITY_UV_STARTS_AT_TOP
    ndc.y *= -1.0f;
    #endif
    return ndc;
}

float2 ScreenCoordsToNDC(const float4 screenCoords)
{
    return ScreenCoordsToNDC(screenCoords.xy);
}

VividBarycentricDerivatives CalculateFullBarycentric(
    const float4 pt0,
    const float4 pt1,
    const float4 pt2,
    const float2 pixelNDC,
    const float2 invWinSize)
{
    VividBarycentricDerivatives result = (VividBarycentricDerivatives) 0;

    float3 invW = rcp(float3(pt0.w, pt1.w, pt2.w));

    float2 ndc0 = pt0.xy * invW.x;
    float2 ndc1 = pt1.xy * invW.y;
    float2 ndc2 = pt2.xy * invW.z;

    float invDet = rcp(determinant(float2x2(ndc2 - ndc1, ndc0 - ndc1)));
    result.ddx = float3(ndc1.y - ndc2.y, ndc2.y - ndc0.y, ndc0.y - ndc1.y) * invDet * invW;
    result.ddy = float3(ndc2.x - ndc1.x, ndc0.x - ndc2.x, ndc1.x - ndc0.x) * invDet * invW;
    float ddxSum = dot(result.ddx, 1.0f.xxx);
    float ddySum = dot(result.ddy, 1.0f.xxx);

    float2 deltaVec = pixelNDC - ndc0;
    float interpInvW = invW.x + deltaVec.x * ddxSum + deltaVec.y * ddySum;
    float interpW = rcp(interpInvW);

    result.lambda.x = interpW * (invW.x + deltaVec.x * result.ddx.x + deltaVec.y * result.ddy.x);
    result.lambda.y = interpW * (deltaVec.x * result.ddx.y + deltaVec.y * result.ddy.y);
    result.lambda.z = interpW * (deltaVec.x * result.ddx.z + deltaVec.y * result.ddy.z);

    float2 pixelStepNDC = 2.0f * invWinSize;
    #ifdef UNITY_UV_STARTS_AT_TOP
    pixelStepNDC.y *= -1.0f;
    #endif
    result.ddx *= pixelStepNDC.x;
    result.ddy *= pixelStepNDC.y;
    ddxSum *= pixelStepNDC.x;
    ddySum *= pixelStepNDC.y;

    float interpW_ddx = rcp(interpInvW + ddxSum);
    float interpW_ddy = rcp(interpInvW + ddySum);

    result.ddx = interpW_ddx * (result.lambda * interpInvW + result.ddx) - result.lambda;
    result.ddy = interpW_ddy * (result.lambda * interpInvW + result.ddy) - result.lambda;

    return result;
}

float3 InterpolateWithBarycentric(const VividBarycentricDerivatives barycentric, float v0, float v1, float v2)
{
    float3 mergedValue = float3(v0, v1, v2);
    float3 result;
    result.x = dot(mergedValue, barycentric.lambda);
    result.y = dot(mergedValue, barycentric.ddx);
    result.z = dot(mergedValue, barycentric.ddy);
    return result;
}

float3 InterpolateWithBarycentricNoDerivatives(
    const VividBarycentricDerivatives barycentric,
    float3 v0,
    float3 v1,
    float3 v2)
{
    return float3(
        InterpolateWithBarycentric(barycentric, v0.x, v1.x, v2.x).x,
        InterpolateWithBarycentric(barycentric, v0.y, v1.y, v2.y).x,
        InterpolateWithBarycentric(barycentric, v0.z, v1.z, v2.z).x
    );
}

float4 InterpolateWithBarycentricNoDerivatives(
    const VividBarycentricDerivatives barycentric,
    float4 v0,
    float4 v1,
    float4 v2)
{
    return float4(
        InterpolateWithBarycentric(barycentric, v0.x, v1.x, v2.x).x,
        InterpolateWithBarycentric(barycentric, v0.y, v1.y, v2.y).x,
        InterpolateWithBarycentric(barycentric, v0.z, v1.z, v2.z).x,
        InterpolateWithBarycentric(barycentric, v0.w, v1.w, v2.w).x
    );
}

VividBarycentricDerivatives InterpolateWithBarycentric(
    const VividBarycentricDerivatives barycentric,
    float3 v0,
    float3 v1,
    float3 v2)
{
    float3 x = InterpolateWithBarycentric(barycentric, v0.x, v1.x, v2.x);
    float3 y = InterpolateWithBarycentric(barycentric, v0.y, v1.y, v2.y);
    float3 z = InterpolateWithBarycentric(barycentric, v0.z, v1.z, v2.z);

    VividBarycentricDerivatives result;
    result.lambda = float3(x.x, y.x, z.x);
    result.ddx = float3(x.y, y.y, z.y);
    result.ddy = float3(x.z, y.z, z.z);
    return result;
}

#endif // VIVIDRP_BARYCENTRIC_INCLUDED
