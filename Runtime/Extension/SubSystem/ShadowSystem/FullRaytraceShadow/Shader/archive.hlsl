// #include "UnityRayQuery.cginc"
// #pragma kernel main
//
// #define SHADER_STAGE_RAY_TRACING 1
// #pragma require inlineraytracing
//
// #pragma only_renderers d3d11 xboxseries ps5
// #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
// #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
// #include "Packages/com.unity.render-pipelines.core/Runtime/Sampling/Common.hlsl"
// #include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/ShaderVariablesRaytracing.hlsl"
// #include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/RayTracingCommon.hlsl"
// #include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/RaytracingIntersection.hlsl"
// #include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/RayTracingFallbackHierarchy.cs.hlsl"
// #include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/RaytracingSampling.hlsl"
// #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Sampling/Sampling.hlsl"
// #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GBufferCommon.hlsl"
// #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Extension/LightGrid/ClusterLight.hlsl"
//
// #include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/Filter/Denoiser/NRD/NRD.hlsl"
// #include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/Filter/Denoiser/NRD/ml.hlsl"
//
//
// #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"
//
// Texture2D<float> _SceneDepth;
// Texture2D<float4> _SceneNormal;
//
//
// RWTexture2D<float> _UnfilterShadowTexture;
//
// RaytracingAccelerationStructure _AccelerationStructure;
//
//
// Texture2D<uint3> gIn_ScramblingRanking;
// Texture2D<uint4> gIn_Sobol;
//
//
// RWTexture2D<float2> gOut_ShadowData;
// // RWTexture2D<float4>  gOut_Shadow_Translucency;
//
//
// float3 gSunBasisX;
// float3 gSunBasisY;
// float3 gSunDirection;
// float2 gJitter;
// float gTanSunAngularRadius;
// float gTanPixelAngularRadius;
// float gUnproject;
// int gFrameIndex;
//
//
//
// // float2 GetConeAngleFromAngularRadius(float mip, float tanConeAngle)
// // {
// //     // In any case, we are limited by the output resolution
// //     tanConeAngle = max(tanConeAngle, _TanSunAngularRadius);
// //
// //     return float2(mip, tanConeAngle);
// // }
// //
// // float2 GetConeAngleFromRoughness(float mip, float roughness)
// // {
// //     float tanConeAngle = roughness * roughness * 0.05; // TODO: tweaked to be accurate and give perf boost
// //
// //     return GetConeAngleFromAngularRadius(mip, tanConeAngle);
// // }
//
//
// // SIGMA single light
//
// // Infinite ( directional ) light source
// // X => IN_PENUMBRA
// float PackPenumbra(float distanceToOccluder, float tanOfLightAngularRadius)
// {
//     float penumbraSize = distanceToOccluder * tanOfLightAngularRadius;
//     float penumbraRadius = penumbraSize * 0.5;
//
//     return distanceToOccluder >= HALF_MAX ? HALF_MAX : min(penumbraRadius, 32768.0);
// }
//
//
// float2 GetBlueNoise(uint2 pixelPos, bool isCheckerboard, uint seed = 0)
// {
//     // Blue noise
//     #define BLUE_NOISE_SPATIAL_DIM              128 // see StaticTexture::ScramblingRanking
//     #define BLUE_NOISE_TEMPORAL_DIM             4 // good values: 4-8 for shadows, 8-16 for occlusion, 8-32 for lighting
//
//     // https://eheitzresearch.wordpress.com/772-2/
//     // https://belcour.github.io/blog/research/publication/2019/06/17/sampling-bluenoise.html
//
//     // Sample index
//     uint frameIndex = isCheckerboard ? (gFrameIndex >> 1) : gFrameIndex;
//     uint sampleIndex = (frameIndex + seed) & (BLUE_NOISE_TEMPORAL_DIM - 1);
//
//     // The algorithm
//     uint3 A = gIn_ScramblingRanking[pixelPos & (BLUE_NOISE_SPATIAL_DIM - 1)];
//     uint rankedSampleIndex = sampleIndex ^ A.z;
//     uint4 B = gIn_Sobol[uint2(rankedSampleIndex & 255, 0)];
//     float4 blue = (float4(B ^ A.xyxy) + 0.5) * (1.0 / 256.0);
//
//     // ( Optional ) Randomize in [ 0; 1 / 256 ] area to get rid of possible banding
//     uint d = Sequence::Bayer4x4ui(pixelPos, gFrameIndex);
//     float2 dither = (float2(d & 3, d >> 2) + 0.5) * (1.0 / 4.0);
//     blue += (dither.xyxy - 0.5) * (1.0 / 256.0);
//
//     // // Don't use blue noise in these cases
//     // [flatten]
//     // if (gDenoiserType == DENOISER_REFERENCE || gRR || gTracingMode == RESOLUTION_FULL_PROBABILISTIC)
//     //     blue.xy = Rng::Hash::GetFloat2();
//
//     return saturate(blue.xy);
// }
//
//
// [numthreads( 16, 16, 1 )]
// void main(uint2 pixelPos : SV_DispatchThreadId)
// {
//     // Pixel and sample UV
//     float2 pixelUv = float2(pixelPos + 0.5) * _ScreenSize.zw;
//     float2 sampleUv = pixelUv + gJitter;
//
//
//     // Do not generate NANs for unused threads
//     if (pixelUv.x > 1.0 || pixelUv.y > 1.0)
//     {
//         return;
//     }
//
//
//     // Load depth
//     float rawDepth = _SceneDepth[pixelPos];
//     // Background, early out.
//     if (rawDepth == UNITY_RAW_FAR_CLIP_VALUE)
//         return;
//     
//
//     //trace for positionWS
//     UnityRayQuery<RAY_FLAG_SKIP_PROCEDURAL_PRIMITIVES> primaryRayQuery;
//
//     float znear = _ProjectionParams.y;
//     float3 nearPlanePositionWS = ComputeWorldSpacePosition(sampleUv, znear, UNITY_MATRIX_I_VP);
//     float3 viewDirection = normalize(nearPlanePositionWS - _WorldSpaceCameraPos);
//
//     RayDesc primaryRay;
//     primaryRay.Origin = _WorldSpaceCameraPos;
//     primaryRay.Direction = viewDirection;
//     primaryRay.TMin = 0;
//     primaryRay.TMax = FLT_INF;
//     primaryRayQuery.TraceRayInline(_AccelerationStructure, 0, RAYTRACINGRENDERERFLAG_CAST_SHADOW, primaryRay);
//     primaryRayQuery.Proceed();
//     float shadowHitDist = primaryRayQuery.CommittedRayT();
//     float3 positionWS = _WorldSpaceCameraPos + viewDirection * shadowHitDist;
//
//
//
//     Rng::Hash::Initialize(pixelPos, gFrameIndex);
//     float2 rnd = GetBlueNoise(pixelPos, false);
//     rnd = ImportanceSampling::Cosine::GetRay(rnd).xy;
//     rnd *= gTanSunAngularRadius;
//     float3 sunDirection = normalize(gSunBasisX.xyz * rnd.x + gSunBasisY.xyz * rnd.y + gSunDirection.xyz);
//
//
//     float3 normalWS = UnpackGBufferNormal(_SceneNormal[pixelPos]);
//     UnityRayQuery < RAY_FLAG_SKIP_PROCEDURAL_PRIMITIVES > rayQuery;
//     RayDesc shadowRay;
//     shadowRay.Origin = positionWS + normalWS * 0.001;
//     shadowRay.Direction = sunDirection;
//     shadowRay.TMin = 0;
//     shadowRay.TMax = FLT_INF;
//     rayQuery.TraceRayInline(_AccelerationStructure, 0, RAYTRACINGRENDERERFLAG_CAST_SHADOW, shadowRay);
//     rayQuery.Proceed();
//
//     shadowHitDist = rayQuery.CommittedRayT();
//
//     _UnfilterShadowTexture[pixelPos] = PackPenumbra(shadowHitDist, gTanSunAngularRadius);
// }
