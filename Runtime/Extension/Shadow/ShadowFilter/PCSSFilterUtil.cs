using System;
using Features.Shadow.ShadowCommon;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Features.Shadow.ShadowFilter
{
    public static class PCSSFilterUtil
    {
        public static int _DirLightShadowUVMinMax = Shader.PropertyToID("_DirLightShadowUVMinMax");
        public static int _DirLightShadowPenumbraParams = Shader.PropertyToID("_DirLightShadowPenumbraParams");
        public static int _DirLightShadowScatterParams = Shader.PropertyToID("_DirLightShadowScatterParams");
        public static int _PerCascadePCSSData = Shader.PropertyToID("_PerCascadePCSSData");

        public static void SetupPCSS(RasterCommandBuffer cmd, UniversalLightData lightData, UniversalShadowData shadowData, Shadows shadows)
        {
            Vector4[] PerCascadePCSSData = new Vector4[4];


            int shadowLightIndex = lightData.mainLightIndex;
            ref readonly URPLightShadowCullingInfos shadowCullingInfos = ref shadowData.visibleLightsShadowCullingInfos.UnsafeElementAt(shadowLightIndex);
            var renderTargetWidth = shadowData.mainLightRenderTargetWidth;
            var renderTargetHeight = shadowData.mainLightRenderTargetHeight;

            VisibleLight visMainLight = lightData.visibleLights[shadowLightIndex];


            Vector4 dir = -visMainLight.light.transform.localToWorldMatrix.GetColumn(2);
            float halfBlockerSearchAngularDiameterTangent = dir.y / MathF.Sqrt(1 - dir.y * dir.y + 0.0001f);

            for (int i = 0; i < shadowData.mainLightShadowCascadesCount; ++i)
            {
                ref readonly ShadowSliceData sliceData = ref shadowCullingInfos.slices.UnsafeElementAt(i);

                float farToNear = MathF.Abs(2.0f / sliceData.projectionMatrix.m22);

                float viewPortSizeWS = 1.0f / sliceData.projectionMatrix.m11 * 2.0f;
                float radial2ShadowmapDepth = Mathf.Abs(sliceData.projectionMatrix.m00 / sliceData.projectionMatrix.m22);

                float texelSizeWS = viewPortSizeWS / renderTargetWidth;

                PerCascadePCSSData[i] = new Vector4(1.0f / (radial2ShadowmapDepth), texelSizeWS, farToNear, 1.0f / halfBlockerSearchAngularDiameterTangent);
            }


            float invShadowAtlasWidth = 1.0f / renderTargetWidth;
            float invShadowAtlasHeight = 1.0f / renderTargetHeight;
            float invHalfShadowAtlasWidth = 0.5f * invShadowAtlasWidth;
            float invHalfShadowAtlasHeight = 0.5f * invShadowAtlasHeight;


            cmd.SetGlobalVectorArray(_PerCascadePCSSData, PerCascadePCSSData);

            cmd.SetGlobalVector(_DirLightShadowUVMinMax,
                new Vector4(invHalfShadowAtlasWidth, invHalfShadowAtlasHeight,
                    1.0f - invHalfShadowAtlasWidth, 1.0f - invHalfShadowAtlasHeight));

            cmd.SetGlobalVector(_DirLightShadowPenumbraParams,
                new Vector4(shadows.penumbra.value, shadows.occlusionPenumbra.value,
                    0, 0));

            cmd.SetGlobalVector(_DirLightShadowScatterParams,
                new Vector4(shadows.scatterR.value, shadows.scatterG.value,
                    shadows.scatterB.value, (float)shadows.shadowScatterMode.value));
        }
    }
}