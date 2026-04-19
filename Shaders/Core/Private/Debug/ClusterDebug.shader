Shader "Hidden/VividRP/ClusterDebug"
{
    SubShader
    {
        Tags { "RenderPipeline" = "VividRenderPipeline" }

        Pass
        {
            Name "ClusterDebug"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Debug.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/LightingLoop.hlsl"

            #define VIVID_TILE_CLUSTER_DEBUG_NONE 0
            #define VIVID_TILE_CLUSTER_DEBUG_TILE 1
            #define VIVID_TILE_CLUSTER_DEBUG_CLUSTER 2
            #define VIVID_TILE_CLUSTER_DEBUG_MATERIAL_FEATURE_VARIANTS 3
            #define VIVID_CLUSTER_DEBUGMODE_VISUALIZE_OPAQUE 0
            #define VIVID_CLUSTER_DEBUGMODE_VISUALIZE_SLICE 1
            #define VIVID_TILE_CLUSTER_CATEGORY_PUNCTUAL (1u << 0)

            TEXTURE2D(_SourceTexture);
            SAMPLER(sampler_SourceTexture);
            TEXTURE2D(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);

            float4 _SourceTextureScaleBias;
            float4 _CameraDepthTextureScaleBias;
            float4 _ClusterDebugLightViewportSize;
            float _ClusterDebugDistance;
            float _ClusterDebugMaxLightCount;
            uint _ViewTilesFlags;
            int _TileClusterDebug;
            int _ClusterDebugMode;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            float2 ApplyScaleBias(float2 uv, float4 scaleBias)
            {
                return uv * scaleBias.xy + scaleBias.zw;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            bool IsClusterDebugEnabled()
            {
                return _TileClusterDebug == VIVID_TILE_CLUSTER_DEBUG_CLUSTER
                    && (_ViewTilesFlags & VIVID_TILE_CLUSTER_CATEGORY_PUNCTUAL) != 0u
                    && _PunctualLightCount > 0u
                    && _ClusterTileCountX > 0
                    && _ClusterTileCountY > 0
                    && _ClusterSliceCount > 0;
            }

            bool IsSkyDepth(float deviceDepth)
            {
                return abs(deviceDepth - UNITY_RAW_FAR_CLIP_VALUE) <= 1e-6;
            }

            float4 AlphaBlend(float4 c0, float4 c1)
            {
                return float4(lerp(c0.rgb, c1.rgb, c1.a), c0.a + c1.a - c0.a * c1.a);
            }

            float3 EvaluateSliceTint(uint sliceIndex)
            {
                float sliceCount = max((float)_ClusterSliceCount, 1.0);
                float sliceRatio = (sliceIndex + 0.5) / sliceCount;
                return lerp(float3(0.2, 0.35, 1.0), float3(1.0, 0.3, 0.25), sliceRatio);
            }

            float ResolveClusterViewDepth(float2 pixelUv, float deviceDepth, out bool isValid)
            {
                if (_ClusterDebugMode == VIVID_CLUSTER_DEBUGMODE_VISUALIZE_SLICE)
                {
                    isValid = true;
                    return clamp(_ClusterDebugDistance, _ClusterNearClip, _ClusterFarClip);
                }

                if (IsSkyDepth(deviceDepth))
                {
                    isValid = false;
                    return 0.0;
                }

                isValid = true;
                return VividClusteredLighting::GetViewDepth(pixelUv, deviceDepth);
            }

            float EvaluatePunctualLightDistanceAttenuationForDebug(PunctualLightData punctualLight, float distanceSquared)
            {
                float attenuation = saturate(1.0 - distanceSquared * punctualLight.inverseRangeSquared);
                return attenuation * attenuation;
            }

            float EvaluatePunctualLightSpotAttenuationForDebug(PunctualLightData punctualLight, float3 lightDirectionWS)
            {
                if (punctualLight.lightType != VIVID_PUNCTUAL_LIGHT_TYPE_SPOT)
                    return 1.0;

                float spotCosine = saturate(dot(punctualLight.directionWS, -lightDirectionWS));
                float attenuation = saturate(spotCosine * punctualLight.angleScale + punctualLight.angleOffset);
                return attenuation * attenuation;
            }

            uint GetBruteForcePunctualLightCount(float3 positionWS)
            {
                uint lightCount = 0u;

                [loop]
                for (uint lightIndex = 0u; lightIndex < _PunctualLightCount; lightIndex++)
                {
                    PunctualLightData punctualLight = GetPunctualLight(lightIndex);
                    float3 lightVectorWS = punctualLight.positionWS - positionWS;
                    float distanceSquared = dot(lightVectorWS, lightVectorWS);

                    if (distanceSquared <= 1e-6)
                        continue;

                    float inverseDistance = rsqrt(distanceSquared);
                    float3 lightDirectionWS = lightVectorWS * inverseDistance;
                    float attenuation = EvaluatePunctualLightDistanceAttenuationForDebug(punctualLight, distanceSquared)
                        * EvaluatePunctualLightSpotAttenuationForDebug(punctualLight, lightDirectionWS);

                    if (attenuation > 0.0)
                        lightCount++;
                }

                return lightCount;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                 uint2 viewportSize = uint2(
                    max((uint)_ClusterDebugLightViewportSize.x, 1u),
                    max((uint)_ClusterDebugLightViewportSize.y, 1u));
                uint2 pixelCoord = min(
                    (uint2)(saturate(input.uv) * viewportSize),
                    viewportSize - 1u);
                float2 pixelUv = (float2(pixelCoord) + 0.5) * _ClusterDebugLightViewportSize.zw;
                float2 sourceUv = ApplyScaleBias(pixelUv, _SourceTextureScaleBias);
                float2 depthUv = ApplyScaleBias(pixelUv, _CameraDepthTextureScaleBias);
                float4 sourceColor = SAMPLE_TEXTURE2D(_SourceTexture, sampler_SourceTexture, sourceUv);

                if (!IsClusterDebugEnabled())
                    return sourceColor;

                float deviceDepth = SAMPLE_TEXTURE2D_LOD(_CameraDepthTexture, sampler_PointClamp, depthUv, 0).r;
                bool isValid;
                float viewDepth = ResolveClusterViewDepth(pixelUv, deviceDepth, isValid);

                if (!isValid)
                    return sourceColor;

                VividLightingLoopContext lightLoop = VividLightingLoop::Create(pixelCoord, viewDepth);
                uint lightCount = VividLightingLoop::GetPunctualLightCount(lightLoop);
                uint sliceIndex = VividClusteredLighting::GetSliceIndex(pixelCoord, viewDepth);
                float3 sliceTint = EvaluateSliceTint(sliceIndex);
                uint tileSize = max((uint)_ClusterTileSize, 1u);
                uint2 tileSize2 = uint2(tileSize, tileSize);
                uint maxLightCount = max((uint)_ClusterDebugMaxLightCount, 1u);
                float4 result = sourceColor;
                uint bruteForceLightCount = 0u;

                if (_ClusterDebugMode == VIVID_CLUSTER_DEBUGMODE_VISUALIZE_OPAQUE)
                {
                    float3 positionWS = ComputeWorldSpacePosition(pixelUv, deviceDepth, UNITY_MATRIX_I_VP);
                    bruteForceLightCount = GetBruteForcePunctualLightCount(positionWS);
                }

                if (lightCount > 0u)
                {
                    float4 heatmapOverlay = OverlayHeatMap(pixelCoord, tileSize2, lightCount, maxLightCount, 0.35);
                    if (_ClusterDebugMode == VIVID_CLUSTER_DEBUGMODE_VISUALIZE_SLICE && heatmapOverlay.a < 0.99)
                        heatmapOverlay.rgb = lerp(heatmapOverlay.rgb, sliceTint, 0.2);

                    result = AlphaBlend(result, heatmapOverlay);
                }
                else if (_ClusterDebugMode == VIVID_CLUSTER_DEBUGMODE_VISUALIZE_SLICE)
                {
                    result = AlphaBlend(result, float4(sliceTint, 0.05));
                }

                uint2 pixelInTile = pixelCoord % tileSize;
                bool border = pixelInTile.x == 0u
                    || pixelInTile.y == 0u
                    || pixelInTile.x == tileSize - 1u
                    || pixelInTile.y == tileSize - 1u;
                float3 borderColor = _ClusterDebugMode == VIVID_CLUSTER_DEBUGMODE_VISUALIZE_SLICE ? sliceTint : float3(1.0, 1.0, 1.0);

                if (border)
                    result = AlphaBlend(result, float4(borderColor, lightCount > 0u ? 0.22 : 0.12));

                if (_ClusterDebugMode == VIVID_CLUSTER_DEBUGMODE_VISUALIZE_OPAQUE && lightCount < bruteForceLightCount)
                    result = AlphaBlend(result, float4(1.0, 0.1, 0.05, 0.8));

                return result;
            }
            ENDHLSL
        }
    }
}
