Shader "Hidden/VividRP/Editor/Material Graph Preview"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.42, 0.55, 0.72, 1)
        _TopColor("Top Color", Color) = (0.88, 0.52, 0.24, 1)
        _Roughness("Roughness", Range(0, 1)) = 0.45
        _Metallic("Metallic", Range(0, 1)) = 0.15
        _LayerWeight("Layer Weight", Range(0, 1)) = 0.5
        _ProgramID("Program ID", Integer) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex vert_img
            #pragma fragment Frag

            #include "UnityCG.cginc"
            #include "Packages/com.vivid.render-pipelines/Runtime/SubSystem/GPUDriven/VividGPUDrivenStructs.cs.hlsl"

            #define VIVID_MATERIAL_PROGRAM_VERSION 2u
            #define VIVIDMATERIALPARAMETERLAYOUTID_GENERIC_PARAMETER_LANES 2u
            #define VIVIDMATERIALRESOURCELAYOUTID_GENERIC_RESOURCE_RECORDS 2u

            float4 _BaseColor;
            float4 _TopColor;
            float _Roughness;
            float _Metallic;
            float _LayerWeight;
            uint _ProgramID;

            struct VividSurfaceSampleContext
            {
                float2 UV;
            };

            struct VividEvaluatedSlabSurface
            {
                float3 BaseColor;
                float3 NormalTS;
                float PerceptualRoughness;
                float Metallic;
                float AmbientOcclusion;
                uint HasNormal;
            };

            struct VividMaterialResourceData
            {
                VividSurfaceBindingData SurfaceBinding;
                float4 TextureTilingOffset;
                float4 MetallicSmoothnessRemap;
                float4 AmbientOcclusionRemap;
                float NormalsStrength;
                uint MaskMode;
                uint Padding0;
                uint Padding1;
            };

            StructuredBuffer<uint4> _MaterialParameterData;
            uint _MaterialParameterDataCount;
            StructuredBuffer<VividMaterialResourceData> _MaterialResourceData;
            uint _MaterialResourceDataCount;

            uint VividLoadMaterialParameterWord(
                const uint parameterAddress,
                const uint wordOffset)
            {
                const uint4 lane = _MaterialParameterData[
                    parameterAddress + (wordOffset >> 2u)];
                return lane[wordOffset & 3u];
            }

            bool VividLoadMaterialBool(const uint address, const uint offset)
            {
                return VividLoadMaterialParameterWord(address, offset) != 0u;
            }

            float VividLoadMaterialFloat(const uint address, const uint offset)
            {
                return asfloat(VividLoadMaterialParameterWord(address, offset));
            }

            float2 VividLoadMaterialFloat2(const uint address, const uint offset)
            {
                return asfloat(uint2(
                    VividLoadMaterialParameterWord(address, offset),
                    VividLoadMaterialParameterWord(address, offset + 1u)));
            }

            float3 VividLoadMaterialFloat3(const uint address, const uint offset)
            {
                return asfloat(uint3(
                    VividLoadMaterialParameterWord(address, offset),
                    VividLoadMaterialParameterWord(address, offset + 1u),
                    VividLoadMaterialParameterWord(address, offset + 2u)));
            }

            float4 VividLoadMaterialFloat4(const uint address, const uint offset)
            {
                return asfloat(uint4(
                    VividLoadMaterialParameterWord(address, offset),
                    VividLoadMaterialParameterWord(address, offset + 1u),
                    VividLoadMaterialParameterWord(address, offset + 2u),
                    VividLoadMaterialParameterWord(address, offset + 3u)));
            }

            VividMaterialResourceData PullMaterialResourceData(
                const uint resourceIndex)
            {
                return _MaterialResourceData[resourceIndex];
            }

            VividSlabMaterialData VividCreateSlabMaterialData(
                const VividMaterialResourceData resourceData)
            {
                VividSlabMaterialData result = (VividSlabMaterialData) 0;
                result.TextureTilingOffset = resourceData.TextureTilingOffset;
                result.MetallicSmoothnessRemap =
                    resourceData.MetallicSmoothnessRemap;
                result.AmbientOcclusionRemap = resourceData.AmbientOcclusionRemap;
                result.NormalsStrength = resourceData.NormalsStrength;
                result.MaskMode = resourceData.MaskMode;
                return result;
            }

            VividSlabMaterialData VividCreateSlabMaterialData(
                const VividMaterialData materialData)
            {
                VividSlabMaterialData result = (VividSlabMaterialData) 0;
                result.AlbedoColor = materialData.AlbedoColor;
                result.TextureTilingOffset = materialData.TextureTilingOffset;
                result.MetallicSmoothnessRemap = materialData.MetallicSmoothnessRemap;
                result.AmbientOcclusionRemap = materialData.AmbientOcclusionRemap;
                result.NormalsStrength = materialData.NormalsStrength;
                result.Roughness = materialData.Roughness;
                result.Metallic = materialData.Metallic;
                return result;
            }

            VividSlabMaterialData VividGetBaseSlabMaterialData(
                const VividDualSlabMaterialData materialData)
            {
                VividSlabMaterialData result = (VividSlabMaterialData) 0;
                result.AlbedoColor = materialData.BaseAlbedoColor;
                result.TextureTilingOffset = materialData.BaseTextureTilingOffset;
                result.MetallicSmoothnessRemap = materialData.BaseMetallicSmoothnessRemap;
                result.AmbientOcclusionRemap = materialData.BaseAmbientOcclusionRemap;
                result.NormalsStrength = materialData.BaseNormalsStrength;
                result.Roughness = materialData.BaseRoughness;
                result.Metallic = materialData.BaseMetallic;
                return result;
            }

            VividSlabMaterialData VividGetTopSlabMaterialData(
                const VividDualSlabMaterialData materialData)
            {
                VividSlabMaterialData result = (VividSlabMaterialData) 0;
                result.AlbedoColor = materialData.TopAlbedoColor;
                result.TextureTilingOffset = materialData.TopTextureTilingOffset;
                result.MetallicSmoothnessRemap = materialData.TopMetallicSmoothnessRemap;
                result.AmbientOcclusionRemap = materialData.TopAmbientOcclusionRemap;
                result.NormalsStrength = materialData.TopNormalsStrength;
                result.Roughness = materialData.TopRoughness;
                result.Metallic = materialData.TopMetallic;
                return result;
            }

            VividSurfaceSampleContext VividCreateSurfaceSampleContextGrad(
                const VividSurfaceBindingData bindingData,
                const float2 uv,
                const float2 uvDdx,
                const float2 uvDdy,
                const float4 positionCS)
            {
                VividSurfaceSampleContext result;
                result.UV = uv;
                return result;
            }

            float4 VividSampleBaseColorGrad(
                const VividSurfaceBindingData bindingData,
                const VividSurfaceSampleContext context)
            {
                return 1.0f.xxxx;
            }

            VividEvaluatedSlabSurface VividEvaluateAOTSlabSurfaceDetail(
                const VividSlabMaterialData slabData,
                const VividSurfaceBindingData surfaceBindingData,
                const VividSurfaceSampleContext context,
                const bool evaluateNormal,
                const bool evaluateMask,
                const float3 baseColor,
                const float perceptualRoughness,
                const float metallic)
            {
                VividEvaluatedSlabSurface result = (VividEvaluatedSlabSurface) 0;
                result.BaseColor = baseColor;
                result.NormalTS = float3(0.0f, 0.0f, 1.0f);
                result.PerceptualRoughness = perceptualRoughness;
                result.Metallic = metallic;
                result.AmbientOcclusion = 1.0f;
                return result;
            }

            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividMaterialSurfaceAOT.generated.hlsl"

            VividMaterialData CreateSingleSlabParameters()
            {
                VividMaterialData result = (VividMaterialData) 0;
                result.AlbedoColor = _BaseColor;
                result.TextureTilingOffset = float4(1.0f, 1.0f, 0.0f, 0.0f);
                result.Emission = 0.0f.xxxx;
                result.MetallicSmoothnessRemap = float4(0.0f, 1.0f, 0.0f, 1.0f);
                result.AmbientOcclusionRemap = float4(0.0f, 1.0f, 0.0f, 0.0f);
                result.NormalsStrength = 1.0f;
                result.Roughness = _Roughness;
                result.Metallic = _Metallic;
                return result;
            }

            VividDualSlabMaterialData CreateDualSlabParameters()
            {
                VividDualSlabMaterialData result = (VividDualSlabMaterialData) 0;
                result.BaseAlbedoColor = _BaseColor;
                result.BaseTextureTilingOffset = float4(1.0f, 1.0f, 0.0f, 0.0f);
                result.BaseMetallicSmoothnessRemap = float4(0.0f, 1.0f, 0.0f, 1.0f);
                result.BaseAmbientOcclusionRemap = float4(0.0f, 1.0f, 0.0f, 0.0f);
                result.BaseNormalsStrength = 1.0f;
                result.BaseRoughness = _Roughness;
                result.BaseMetallic = _Metallic;
                result.TopAlbedoColor = _TopColor;
                result.TopTextureTilingOffset = float4(1.0f, 1.0f, 0.0f, 0.0f);
                result.TopMetallicSmoothnessRemap = float4(0.0f, 1.0f, 0.0f, 1.0f);
                result.TopAmbientOcclusionRemap = float4(0.0f, 1.0f, 0.0f, 0.0f);
                result.TopNormalsStrength = 1.0f;
                result.TopRoughness = saturate(_Roughness * 0.55f);
                result.TopMetallic = saturate(_Metallic * 0.35f);
                result.LayerWeight = _LayerWeight;
                return result;
            }

            float3 ShadeSlab(
                const VividAOTSurfaceSlabValues slab,
                const float3 normalWS,
                const float3 viewDirection)
            {
                const float3 lightDirection = normalize(float3(-0.45f, 0.7f, 0.55f));
                const float3 halfDirection = normalize(lightDirection + viewDirection);
                const float nDotL = saturate(dot(normalWS, lightDirection));
                const float nDotH = saturate(dot(normalWS, halfDirection));
                const float roughness = max(0.04f, slab.PerceptualRoughness);
                const float specularPower = lerp(160.0f, 4.0f, roughness);
                const float3 f0 = lerp(0.04f.xxx, slab.BaseColor.rgb, slab.Metallic);
                const float3 diffuse = slab.BaseColor.rgb * (1.0f - slab.Metallic)
                    * (0.16f + 0.84f * nDotL);
                const float3 specular = f0 * pow(nDotH, specularPower)
                    * lerp(1.4f, 0.35f, roughness);
                return diffuse + specular;
            }

            float4 Frag(v2f_img input) : SV_Target
            {
                const float2 centered = input.uv * 2.0f - 1.0f;
                const float radiusSquared = dot(centered, centered);
                const float3 background = lerp(
                    float3(0.055f, 0.06f, 0.075f),
                    float3(0.16f, 0.17f, 0.19f),
                    saturate(input.uv.y));
                if (radiusSquared > 0.92f)
                    return float4(background, 1.0f);

                const float z = sqrt(saturate(0.92f - radiusSquared));
                const float3 normalWS = normalize(float3(centered, z));
                const float3 viewDirection = float3(0.0f, 0.0f, 1.0f);
                VividAOTSurfaceContext context = (VividAOTSurfaceContext) 0;
                context.UV0 = input.uv;
                context.UV0Ddx = ddx(input.uv);
                context.UV0Ddy = ddy(input.uv);
                context.GeometryNormalWS = normalWS;
                context.GeometryTangentWS = float4(1.0f, 0.0f, 0.0f, 1.0f);
                context.PositionCS = input.pos;

                VividSurfaceBindingData binding = (VividSurfaceBindingData) 0;
                binding.BaseColorResource = INVALID_RESOURCE;
                binding.NormalResource = INVALID_RESOURCE;
                binding.MaskResource = INVALID_RESOURCE;
                binding.UVScaleBias = float4(1.0f, 1.0f, 0.0f, 0.0f);

                VividAOTDeferredExportContract exportContract;
                VividAOTSurfaceProgramOutput surface;
                VividMaterialRuntimeHeader runtimeHeader =
                    (VividMaterialRuntimeHeader) 0;
                runtimeHeader.ProgramID = _ProgramID;
                VividMaterialProgramData programData =
                    (VividMaterialProgramData) 0;
                programData.Version = VIVID_MATERIAL_PROGRAM_VERSION;
                programData.ParameterLayoutID =
                    VIVIDMATERIALPARAMETERLAYOUTID_GENERIC_PARAMETER_LANES;
                programData.ResourceLayoutID =
                    VIVIDMATERIALRESOURCELAYOUTID_GENERIC_RESOURCE_RECORDS;
                if (!VividTryEvaluateAOTSurfaceProgram(
                        runtimeHeader,
                        programData,
                        context,
                        exportContract,
                        surface))
                {
                    return float4(0.55f, 0.08f, 0.12f, 1.0f);
                }

                float3 color = ShadeSlab(surface.BaseSlab, normalWS, viewDirection);
                if (surface.ClosureCount > 1u)
                {
                    const float3 topColor = ShadeSlab(
                        surface.TopSlab,
                        normalWS,
                        viewDirection);
                    color = lerp(color, topColor, saturate(surface.LayerWeight));
                    if (surface.LayerOperator == 2u)
                    {
                        const float fresnel = pow(1.0f - saturate(normalWS.z), 5.0f);
                        color += lerp(0.02f, 0.18f, fresnel)
                            * saturate(surface.LayerWeight);
                    }
                }

                color += surface.Emission;
                const float rim = pow(1.0f - saturate(normalWS.z), 3.0f);
                color += rim * float3(0.08f, 0.11f, 0.16f);
                return float4(color, 1.0f);
            }
            ENDHLSL
        }
    }
}
