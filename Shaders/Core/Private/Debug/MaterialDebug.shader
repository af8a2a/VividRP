Shader "Hidden/VividRP/MaterialDebug"
{
    SubShader
    {
        Tags { "RenderPipeline" = "VividRenderPipeline" }

        Pass
        {
            Name "MaterialDebug"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/GBuffer.hlsl"

            #define VIVID_MATERIAL_DEBUG_NONE 0
            #define VIVID_MATERIAL_DEBUG_BASE_COLOR 1
            #define VIVID_MATERIAL_DEBUG_NORMAL_WS 2
            #define VIVID_MATERIAL_DEBUG_LINEAR_ROUGHNESS 3
            #define VIVID_MATERIAL_DEBUG_PERCEPTUAL_ROUGHNESS 4
            #define VIVID_MATERIAL_DEBUG_SMOOTHNESS 5
            #define VIVID_MATERIAL_DEBUG_METALLIC 6
            #define VIVID_MATERIAL_DEBUG_AMBIENT_OCCLUSION 7
            #define VIVID_MATERIAL_DEBUG_CUSTOM_DATA 8
            #define VIVID_MATERIAL_DEBUG_CUSTOM_DATA_1 9
            #define VIVID_MATERIAL_DEBUG_MATERIAL_ID 10
            #define VIVID_MATERIAL_DEBUG_EMISSIVE 11
            #define VIVID_MATERIAL_DEBUG_BAKED_GI 12
            #define VIVID_MATERIAL_DEBUG_HAS_BAKED_GI 13
            #define VIVID_MATERIAL_DEBUG_DEPTH 14
            #define VIVID_MATERIAL_DEBUG_BAKE_DIFFUSE_LIGHTING_WITH_ALBEDO_PLUS_EMISSIVE 15
            #define VIVID_MATERIAL_DEBUG_DIFFUSE_COLOR 16
            #define VIVID_MATERIAL_DEBUG_NORMAL_VIEW_SPACE 17
            #define VIVID_MATERIAL_DEBUG_SPECULAR_OCCLUSION 18
            #define VIVID_MATERIAL_DEBUG_FRESNEL0 19
            #define VIVID_MATERIAL_DEBUG_FRESNEL90 20
            #define VIVID_MATERIAL_DEBUG_COAT_MASK 21
            #define VIVID_MATERIAL_DEBUG_COAT_ROUGHNESS 22
            #define VIVID_MATERIAL_DEBUG_MATERIAL_FEATURES 23

            static const float3 kVividMaterialDebugDielectricF0 = float3(0.04, 0.04, 0.04);
            static const float kVividMaterialDebugClearCoatRoughness = 0.01;

            TEXTURE2D(_SourceTexture);
            SAMPLER(sampler_SourceTexture);
            TEXTURE2D(_CameraDepthTexture);
            TEXTURE2D(_GBuffer0);
            TEXTURE2D(_GBuffer1);
            TEXTURE2D(_GBuffer2);
            TEXTURE2D(_GBuffer3);
            TEXTURE2D(_GBuffer4);

            float4 _SourceTextureScaleBias;
            float4 _CameraDepthTextureScaleBias;
            float4 _GBuffer0ScaleBias;
            float4 _GBuffer1ScaleBias;
            float4 _GBuffer2ScaleBias;
            float4 _GBuffer3ScaleBias;
            float4 _GBuffer4ScaleBias;
            int _MaterialDebugMode;
            float _MaterialDebugExposure;

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

            bool IsSkyDepth(float deviceDepth)
            {
                return abs(deviceDepth - UNITY_RAW_FAR_CLIP_VALUE) <= 1e-6;
            }

            float3 HashColor(uint seed)
            {
                float seedValue = (float)(seed + 1u);
                float3 value = float3(seedValue, seedValue + 29.0, seedValue + 71.0);
                value = frac(sin(value * float3(12.9898, 78.233, 39.425)) * 43758.5453);
                return saturate(0.25 + value * 0.75);
            }

            float3 EvaluateMaterialIdColor(uint materialId)
            {
                if (materialId == VIVID_GBUFFER_MATERIAL_STANDARD)
                    return float3(0.2, 0.55, 1.0);

                if (materialId == VIVID_GBUFFER_MATERIAL_FABRIC)
                    return float3(0.9, 0.35, 0.85);

                if (materialId == VIVID_GBUFFER_MATERIAL_CLEARCOAT)
                    return float3(1.0, 0.75, 0.15);

                return HashColor(materialId);
            }

            float3 EvaluateMaterialFeatureColor(uint materialId)
            {
                if (materialId == VIVID_GBUFFER_MATERIAL_STANDARD)
                    return float3(0.0, 0.45, 1.0);

                if (materialId == VIVID_GBUFFER_MATERIAL_FABRIC)
                    return float3(0.8, 0.2, 0.95);

                if (materialId == VIVID_GBUFFER_MATERIAL_CLEARCOAT)
                    return float3(1.0, 0.65, 0.0);

                return HashColor(1u << (materialId & 7u));
            }

            float3 EncodeDirectionDebug(float3 direction)
            {
                return IsNormalized(direction) ? direction * 0.5 + 0.5 : float3(1.0, 0.0, 0.0);
            }

            float3 EvaluateDiffuseColor(VividGBufferSurfaceData surfaceData)
            {
                return surfaceData.baseColor * (1.0 - surfaceData.metallic);
            }

            real Luminance(real3 linearRgb)
            {
                return dot(linearRgb, real3(0.2126729, 0.7151522, 0.0721750));
            }

            float3 EvaluateFresnel0(VividGBufferSurfaceData surfaceData)
            {
                float3 baseSpecular = lerp(kVividMaterialDebugDielectricF0, surfaceData.baseColor, surfaceData.metallic);
                if (surfaceData.materialId != VIVID_GBUFFER_MATERIAL_FABRIC)
                    return baseSpecular;

                float luminance = Luminance(surfaceData.baseColor);
                float3 sheenTint = lerp(luminance.xxx, surfaceData.baseColor, 0.35);
                return lerp(baseSpecular, sheenTint, saturate(surfaceData.customData));
            }

            float EvaluateCoatMask(VividGBufferSurfaceData surfaceData)
            {
                return surfaceData.materialId == VIVID_GBUFFER_MATERIAL_CLEARCOAT
                    ? saturate(surfaceData.customData)
                    : 0.0;
            }

            float3 EvaluateMaterialDebugColor(
                VividGBufferSurfaceData surfaceData,
                float deviceDepth,
                float4 sourceColor)
            {
                float exposureMultiplier = exp2(_MaterialDebugExposure);

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_DEPTH)
                    return Linear01Depth(deviceDepth, _ZBufferParams).xxx;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_BAKE_DIFFUSE_LIGHTING_WITH_ALBEDO_PLUS_EMISSIVE)
                    return (surfaceData.bakedGI * EvaluateDiffuseColor(surfaceData) + surfaceData.emissive) * exposureMultiplier;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_BASE_COLOR)
                    return surfaceData.baseColor;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_DIFFUSE_COLOR)
                    return EvaluateDiffuseColor(surfaceData);

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_NORMAL_WS)
                    return EncodeDirectionDebug(surfaceData.normalWS);

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_NORMAL_VIEW_SPACE)
                    return EncodeDirectionDebug(TransformWorldToViewDir(surfaceData.normalWS, true));

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_LINEAR_ROUGHNESS)
                    return surfaceData.linearRoughness.xxx;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_PERCEPTUAL_ROUGHNESS)
                    return GetPerceptualRoughnessFromLinearRoughness(surfaceData.linearRoughness).xxx;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_SMOOTHNESS)
                    return (1.0 - GetPerceptualRoughnessFromLinearRoughness(surfaceData.linearRoughness)).xxx;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_METALLIC)
                    return surfaceData.metallic.xxx;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_AMBIENT_OCCLUSION)
                    return surfaceData.ambientOcclusion.xxx;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_SPECULAR_OCCLUSION)
                    return surfaceData.ambientOcclusion.xxx;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_FRESNEL0)
                    return EvaluateFresnel0(surfaceData);

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_FRESNEL90)
                    return float3(1.0, 1.0, 1.0);

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_COAT_MASK)
                    return EvaluateCoatMask(surfaceData).xxx;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_COAT_ROUGHNESS)
                {
                    float coatRoughness = EvaluateCoatMask(surfaceData) > 0.0
                        ? kVividMaterialDebugClearCoatRoughness
                        : 0.0;
                    return coatRoughness.xxx;
                }

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_MATERIAL_FEATURES)
                    return EvaluateMaterialFeatureColor(surfaceData.materialId);

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_CUSTOM_DATA)
                    return surfaceData.customData.xxx;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_CUSTOM_DATA_1)
                    return surfaceData.customData1.xxx;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_MATERIAL_ID)
                    return EvaluateMaterialIdColor(surfaceData.materialId);

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_EMISSIVE)
                    return surfaceData.emissive * exposureMultiplier;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_BAKED_GI)
                    return surfaceData.bakedGI * exposureMultiplier;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_HAS_BAKED_GI)
                    return surfaceData.hasBakedGI.xxx;

                return sourceColor.rgb;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 sourceUv = ApplyScaleBias(input.uv, _SourceTextureScaleBias);
                float4 sourceColor = SAMPLE_TEXTURE2D(_SourceTexture, sampler_SourceTexture, sourceUv);

                float2 depthUv = ApplyScaleBias(input.uv, _CameraDepthTextureScaleBias);
                float deviceDepth = SAMPLE_TEXTURE2D_LOD(_CameraDepthTexture, sampler_PointClamp, depthUv, 0).r;
                if (IsSkyDepth(deviceDepth) || _MaterialDebugMode == VIVID_MATERIAL_DEBUG_NONE)
                    return sourceColor;

                float4 rt0 = SAMPLE_TEXTURE2D_LOD(_GBuffer0, sampler_PointClamp, ApplyScaleBias(input.uv, _GBuffer0ScaleBias), 0);
                float4 rt1 = SAMPLE_TEXTURE2D_LOD(_GBuffer1, sampler_PointClamp, ApplyScaleBias(input.uv, _GBuffer1ScaleBias), 0);
                float4 rt2 = SAMPLE_TEXTURE2D_LOD(_GBuffer2, sampler_PointClamp, ApplyScaleBias(input.uv, _GBuffer2ScaleBias), 0);
                float4 rt3 = SAMPLE_TEXTURE2D_LOD(_GBuffer3, sampler_PointClamp, ApplyScaleBias(input.uv, _GBuffer3ScaleBias), 0);
                float4 rt4 = SAMPLE_TEXTURE2D_LOD(_GBuffer4, sampler_PointClamp, ApplyScaleBias(input.uv, _GBuffer4ScaleBias), 0);
                VividGBufferSurfaceData surfaceData = UnpackVividGBufferSurfaceData(rt0, rt1, rt2, rt3, rt4);

                return float4(EvaluateMaterialDebugColor(surfaceData, deviceDepth, sourceColor), sourceColor.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
