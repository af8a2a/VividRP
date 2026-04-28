Shader "Hidden/VividRP/LocalVolumetricFogVoxelize"
{
    Properties
    {
        _FogVolumeSrcColorBlend("Src Color Blend", Float) = 1
        _FogVolumeDstColorBlend("Dst Color Blend", Float) = 1
        _FogVolumeSrcAlphaBlend("Src Alpha Blend", Float) = 1
        _FogVolumeDstAlphaBlend("Dst Alpha Blend", Float) = 1
        _FogVolumeColorBlendOp("Color Blend Op", Float) = 0
        _FogVolumeAlphaBlendOp("Alpha Blend Op", Float) = 0
        _FogVolumeBlendMode("Blend Mode", Float) = 1
        _FogVolumeSingleScatteringAlbedo("Single Scattering Albedo", Color) = (1, 1, 1, 1)
        _FogVolumeFogDistanceProperty("Fog Distance", Float) = 50
        _VolumetricMaskMode("Mask Mode", Float) = 0
        _VolumetricAlphaOnlyTexture("Alpha Only Texture", Float) = 0
        _VolumetricMask("Volumetric Mask", 3D) = "white" {}
        _VolumetricTiling("Volumetric Tiling", Vector) = (1, 1, 1, 0)
        _VolumetricScroll("Volumetric Scroll", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "VividRenderPipeline"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "FogVolumeVoxelize"
            Tags { "LightMode" = "FogVolumeVoxelize" }

            Cull Off
            ZWrite Off
            ZTest Always
            Blend [_FogVolumeSrcColorBlend] [_FogVolumeDstColorBlend], [_FogVolumeSrcAlphaBlend] [_FogVolumeDstAlphaBlend]
            BlendOp [_FogVolumeColorBlendOp], [_FogVolumeAlphaBlendOp]

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Private/Volumetric/VBuffer.hlsl"

            #define LOCALVOLUMETRICFOGBLENDINGMODE_OVERWRITE 0
            #define LOCALVOLUMETRICFOGBLENDINGMODE_ADDITIVE 1
            #define LOCALVOLUMETRICFOGBLENDINGMODE_MULTIPLY 2
            #define LOCALVOLUMETRICFOGBLENDINGMODE_MIN 3
            #define LOCALVOLUMETRICFOGBLENDINGMODE_MAX 4
            #define LOCALVOLUMETRICFOGFALLOFFMODE_EXPONENTIAL 1

            uint _VolumetricFogGlobalIndex;

            struct VividVolumetricMaterialRenderingData
            {
                float4 viewSpaceBounds;
                uint startSliceIndex;
                uint sliceCount;
                uint padding0;
                uint padding1;
                float4 obbVertexPositionWS[8];
            };

            StructuredBuffer<VividVolumetricMaterialRenderingData> _VolumetricMaterialData;
            ByteAddressBuffer _VolumetricGlobalIndirectionBuffer;

            float4 _FogVolumeSingleScatteringAlbedo;
            float _FogVolumeFogDistanceProperty;
            float _FogVolumeBlendMode;
            float3 _VolumetricMaterialObbRight;
            float3 _VolumetricMaterialObbUp;
            float3 _VolumetricMaterialObbExtents;
            float3 _VolumetricMaterialObbCenter;
            float3 _VolumetricMaterialRcpPosFaceFade;
            float3 _VolumetricMaterialRcpNegFaceFade;
            int _VolumetricMaterialInvertFade;
            float _VolumetricMaterialRcpDistFadeLen;
            float _VolumetricMaterialEndTimesRcpDistFadeLen;
            int _VolumetricMaterialFalloffMode;
            float _VolumetricMaskMode;
            float _VolumetricAlphaOnlyTexture;
            float3 _VolumetricTiling;
            float3 _VolumetricScroll;
            Texture3D<float4> _VolumetricMask;

            struct VertexToFragment
            {
                float4 positionCS : SV_POSITION;
                float3 viewDirectionWS : TEXCOORD0;
                nointerpolation float viewIndex : TEXCOORD1;
                nointerpolation uint depthSlice : SV_RenderTargetArrayIndex;
            };

            float Remap01Vivid(float value, float rcpLength, float startTimesRcpLength)
            {
                return saturate(value * rcpLength - startTimesRcpLength);
            }

            float Remap10Vivid(float value, float rcpLength, float endTimesRcpLength)
            {
                return saturate(endTimesRcpLength - value * rcpLength);
            }

            float ApplyExponentialFadeFactor(float fade, bool exponential, bool multiplyBlendMode)
            {
                if (exponential)
                {
                    fade = multiplyBlendMode
                        ? 1.0 - pow(abs(fade - 1.0), 2.2)
                        : pow(fade, 2.2);
                }

                return fade;
            }

            float ComputeVolumeFadeFactor(
                float3 coordNDC,
                float distanceToCamera,
                float3 rcpPosFaceFade,
                float3 rcpNegFaceFade,
                bool invertFade,
                float rcpDistFadeLen,
                float endTimesRcpDistFadeLen,
                bool exponentialFalloff,
                bool multiplyBlendMode)
            {
                float3 posF = float3(
                    Remap10Vivid(coordNDC.x, rcpPosFaceFade.x, rcpPosFaceFade.x),
                    Remap10Vivid(coordNDC.y, rcpPosFaceFade.y, rcpPosFaceFade.y),
                    Remap10Vivid(coordNDC.z, rcpPosFaceFade.z, rcpPosFaceFade.z));
                float3 negF = float3(
                    Remap01Vivid(coordNDC.x, rcpNegFaceFade.x, 0.0),
                    Remap01Vivid(coordNDC.y, rcpNegFaceFade.y, 0.0),
                    Remap01Vivid(coordNDC.z, rcpNegFaceFade.z, 0.0));
                float distanceFade = Remap10Vivid(distanceToCamera, rcpDistFadeLen, endTimesRcpDistFadeLen);
                float fade = posF.x * posF.y * posF.z * negF.x * negF.y * negF.z;

                fade = ApplyExponentialFadeFactor(fade, exponentialFalloff, multiplyBlendMode);
                fade = distanceFade * (invertFade ? 1.0 - fade : fade);
                return saturate(fade);
            }

            float VBufferDistanceToSliceIndex(uint sliceIndex)
            {
                return GetVBufferSliceDistance((float)sliceIndex + 0.5);
            }

            float EyeDepthToLinear(float linearDepth, float4 zBufferParam)
            {
                linearDepth = rcp(linearDepth);
                linearDepth -= zBufferParam.w;
                return linearDepth / zBufferParam.z;
            }

            VertexToFragment Vert(uint instanceId : SV_InstanceID, uint vertexId : SV_VertexID)
            {
                VertexToFragment output;

                uint materialDataIndex = _VolumetricGlobalIndirectionBuffer.Load(_VolumetricFogGlobalIndex << 2);
                uint sliceCount = max(_VolumetricMaterialData[materialDataIndex].sliceCount, 1u);
                uint viewIndex = instanceId / sliceCount;
                materialDataIndex += viewIndex * (uint)_VBufferLocalFogCount;

                uint sliceStartIndex = _VolumetricMaterialData[materialDataIndex].startSliceIndex;
                uint sliceIndex = sliceStartIndex + (instanceId % sliceCount);
                output.viewIndex = viewIndex;
                output.depthSlice = sliceIndex + viewIndex * (uint)_VBufferSliceCount;

                output.positionCS = GetQuadVertexPosition(vertexId);
                output.positionCS.xy =
                    output.positionCS.xy * _VolumetricMaterialData[materialDataIndex].viewSpaceBounds.zw
                    + _VolumetricMaterialData[materialDataIndex].viewSpaceBounds.xy;

                float sliceDepth = VBufferDistanceToSliceIndex(sliceIndex);
                output.positionCS.z = EyeDepthToLinear(sliceDepth, _ZBufferParams);
                output.positionCS.w = 1.0;

                float3 rayDirectionWS = GetVBufferRayDirectionWSFromPixelCoord(
                    (output.positionCS.xy * 0.5 + 0.5) * _VBufferViewportSize.xy);
                output.viewDirectionWS = -rayDirectionWS;

                return output;
            }

            float SampleVolumetricMask(float3 coordNDC)
            {
                if (_VolumetricMaskMode <= 0.5)
                    return 1.0;

                float3 maskUv = saturate(coordNDC * max(_VolumetricTiling, 1e-4) + _VolumetricScroll);
                float4 maskSample = _VolumetricMask.SampleLevel(sampler_LinearClamp, maskUv, 0);
                return _VolumetricAlphaOnlyTexture > 0.5 ? maskSample.a : maskSample.r;
            }

            void Frag(VertexToFragment input, out float4 outColor : SV_Target0)
            {
                float sliceDistance = VBufferDistanceToSliceIndex(input.depthSlice % (uint)_VBufferSliceCount);
                float3 rayCenterDirWS = normalize(-input.viewDirectionWS);
                float3 voxelCenterWS = _WorldSpaceCameraPos + sliceDistance * rayCenterDirWS;

                float3x3 obbFrame = float3x3(
                    normalize(_VolumetricMaterialObbRight),
                    normalize(_VolumetricMaterialObbUp),
                    normalize(cross(_VolumetricMaterialObbRight, _VolumetricMaterialObbUp)));
                float3 voxelCenterBS = mul(voxelCenterWS - _VolumetricMaterialObbCenter, transpose(obbFrame));
                float3 voxelCenterCS = voxelCenterBS * rcp(max(_VolumetricMaterialObbExtents, 1e-4));

                if (Max3(abs(voxelCenterCS.x), abs(voxelCenterCS.y), abs(voxelCenterCS.z)) > 1.0)
                    clip(-1);

                float3 voxelCenterNDC = saturate(voxelCenterCS * 0.5 + 0.5);
                bool exponential = (uint)_VolumetricMaterialFalloffMode == LOCALVOLUMETRICFOGFALLOFFMODE_EXPONENTIAL;
                bool multiplyBlendMode = (uint)_FogVolumeBlendMode == LOCALVOLUMETRICFOGBLENDINGMODE_MULTIPLY;
                float fade = ComputeVolumeFadeFactor(
                    voxelCenterNDC,
                    sliceDistance,
                    _VolumetricMaterialRcpPosFaceFade,
                    _VolumetricMaterialRcpNegFaceFade,
                    _VolumetricMaterialInvertFade != 0,
                    _VolumetricMaterialRcpDistFadeLen,
                    _VolumetricMaterialEndTimesRcpDistFadeLen,
                    exponential,
                    multiplyBlendMode);
                fade *= SampleVolumetricMask(voxelCenterNDC);

                float extinction = rcp(max(_FogVolumeFogDistanceProperty, 0.05));
                float3 scattering = saturate(_FogVolumeSingleScatteringAlbedo.rgb) * extinction;

                if (multiplyBlendMode)
                {
                    outColor = max(0.0, lerp(float4(1.0, 1.0, 1.0, 1.0), float4(scattering, extinction), float4(fade, fade, fade, fade)));
                }
                else
                {
                    extinction *= fade;
                    outColor = max(0.0, float4(saturate(scattering * fade), extinction));
                }
            }
            ENDHLSL
        }
    }

    FallBack Off
}
