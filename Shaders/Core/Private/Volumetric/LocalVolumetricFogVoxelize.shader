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
        [NoScaleOffset]_Mask("Mask", 3D) = "white" {}
        _ScrollSpeed("Scroll Speed", Vector) = (0, 0, 0, 0)
        _Tiling("Tiling", Vector) = (1, 1, 1, 0)
        _AlphaOnlyTexture("Alpha Only Texture", Float) = 0
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
            #pragma multi_compile_fragment _ _ENABLE_VOLUMETRIC_FOG_MASK

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
            float3 _ScrollSpeed;
            float3 _Tiling;
            float _AlphaOnlyTexture;
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
            Texture3D<float4> _Mask;
            SAMPLER(sampler_Mask);
            Texture3D<float4> _VolumetricMask;
            SAMPLER(sampler_VolumetricMask);

            struct VertexToFragment
            {
                float4 positionCS : SV_POSITION;
                float3 viewDirectionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                nointerpolation float viewIndex : TEXCOORD2;
                nointerpolation uint depthSlice : SV_RenderTargetArrayIndex;
            };

            struct FragInputs
            {
                float4 positionSS;
                float3 positionRWS;
                float3 positionPredisplacementRWS;
                uint2 positionPixel;
                float4 texCoord0;
            };

            struct SurfaceDescriptionInputs
            {
                float4 uv0;
                float3 TimeParameters;
            };

            struct SurfaceDescription
            {
                float3 BaseColor;
                float Alpha;
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
                float encodedDepth = ((float)sliceIndex + 0.5) * _VBufferRcpSliceCount + _VBufferRcpSliceCount;
                return DecodeLogarithmicDepthGeneralized(encodedDepth, _VBufferDepthDecodingParams);
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
                uint sliceCount = _VolumetricMaterialData[materialDataIndex].sliceCount;
                uint viewIndex = instanceId / sliceCount;
                materialDataIndex += viewIndex * (uint)_VBufferLocalFogCount;
                output.viewIndex = viewIndex;

                uint sliceStartIndex = _VolumetricMaterialData[materialDataIndex].startSliceIndex;
                uint sliceIndex = sliceStartIndex + (instanceId % sliceCount);
                output.depthSlice = sliceIndex + viewIndex * (uint)_VBufferSliceCount;

                output.positionCS = GetQuadVertexPosition(vertexId);
                output.positionCS.xy =
                    output.positionCS.xy * _VolumetricMaterialData[materialDataIndex].viewSpaceBounds.zw
                    + _VolumetricMaterialData[materialDataIndex].viewSpaceBounds.xy;

                float sliceDepth = VBufferDistanceToSliceIndex(sliceIndex);
                output.positionCS.z = EyeDepthToLinear(sliceDepth, _ZBufferParams);
                output.positionCS.w = 1.0;

                float3 positionRWS = ComputeWorldSpacePosition(output.positionCS, UNITY_MATRIX_I_VP);
                output.viewDirectionWS = GetWorldSpaceViewDir(positionRWS);
                output.positionOS = mul(UNITY_MATRIX_I_M, float4(positionRWS, 1.0)).xyz;

                return output;
            }

            FragInputs BuildFragInputs(VertexToFragment input, float3 voxelPositionRWS, float3 voxelClipSpace)
            {
                FragInputs output;
                ZERO_INITIALIZE(FragInputs, output);

                output.positionSS = input.positionCS;
                output.positionRWS = voxelPositionRWS;
                output.positionPredisplacementRWS = voxelPositionRWS;
                output.positionPixel = uint2(input.positionCS.xy);
                output.texCoord0 = float4(saturate(voxelClipSpace * 0.5 + 0.5), 0.0);

                return output;
            }

            SurfaceDescriptionInputs FragInputsToSurfaceDescriptionInputs(FragInputs input)
            {
                SurfaceDescriptionInputs output;
                ZERO_INITIALIZE(SurfaceDescriptionInputs, output);

                output.uv0 = input.texCoord0;
                output.TimeParameters = _TimeParameters.xyz;

                return output;
            }

            SurfaceDescription SurfaceDescriptionFunction(SurfaceDescriptionInputs input)
            {
                SurfaceDescription surface;
                ZERO_INITIALIZE(SurfaceDescription, surface);

                float4 maskValue = float4(1.0, 1.0, 1.0, 1.0);
                if (_VolumetricMaskMode > 0.5)
                {
                    float3 maskUv = input.uv0.xyz * max(_VolumetricTiling, 1e-4) + _VolumetricScroll;
                    float4 maskSample = _VolumetricMask.SampleLevel(sampler_VolumetricMask, maskUv, 0);
                    float4 alphaOnlyMask = float4(1.0, 1.0, 1.0, maskSample.a);
                    maskValue = 0.5 > _VolumetricAlphaOnlyTexture ? maskSample : alphaOnlyMask;
                }
#if defined(_ENABLE_VOLUMETRIC_FOG_MASK)
                else
                {
                    float3 maskUv = input.uv0.xyz * max(_Tiling, 1e-4) + _ScrollSpeed * input.TimeParameters.x;
                    float4 maskSample = _Mask.SampleLevel(sampler_Mask, maskUv, 0);
                    float4 alphaOnlyMask = float4(1.0, 1.0, 1.0, maskSample.a);
                    maskValue = 0.5 > _AlphaOnlyTexture ? maskSample : alphaOnlyMask;
                }
#endif

                surface.BaseColor = maskValue.rgb;
                surface.Alpha = maskValue.a;
                return surface;
            }

            void GetVolumeData(FragInputs fragInputs, float3 viewWS, out float3 scatteringColor, out float density)
            {
                SurfaceDescriptionInputs surfaceDescriptionInputs = FragInputsToSurfaceDescriptionInputs(fragInputs);
                SurfaceDescription surfaceDescription = SurfaceDescriptionFunction(surfaceDescriptionInputs);

                scatteringColor = surfaceDescription.BaseColor;
                density = surfaceDescription.Alpha;
            }

            float ComputeFadeFactor(float3 coordNDC, float distance)
            {
                bool exponential = (uint)_VolumetricMaterialFalloffMode == LOCALVOLUMETRICFOGFALLOFFMODE_EXPONENTIAL;
                bool multiplyBlendMode = (uint)_FogVolumeBlendMode == LOCALVOLUMETRICFOGBLENDINGMODE_MULTIPLY;

                return ComputeVolumeFadeFactor(
                    coordNDC,
                    distance,
                    _VolumetricMaterialRcpPosFaceFade,
                    _VolumetricMaterialRcpNegFaceFade,
                    _VolumetricMaterialInvertFade != 0,
                    _VolumetricMaterialRcpDistFadeLen,
                    _VolumetricMaterialEndTimesRcpDistFadeLen,
                    exponential,
                    multiplyBlendMode);
            }

            void Frag(VertexToFragment input, out float4 outColor : SV_Target0)
            {
                float sliceDistance = VBufferDistanceToSliceIndex(input.depthSlice % (uint)_VBufferSliceCount);
                float3 rayCenterDirWS = normalize(-input.viewDirectionWS);
                float3 voxelCenterRWS = GetCurrentViewPosition() + sliceDistance * rayCenterDirWS;

                float3x3 obbFrame = float3x3(
                    _VolumetricMaterialObbRight,
                    _VolumetricMaterialObbUp,
                    cross(_VolumetricMaterialObbRight, _VolumetricMaterialObbUp));
                float3 voxelCenterBS = mul(GetAbsolutePositionWS(voxelCenterRWS - _VolumetricMaterialObbCenter), transpose(obbFrame));
                float3 voxelCenterCS = voxelCenterBS * rcp(max(_VolumetricMaterialObbExtents, 1e-4));

                bool overlap = Max3(abs(voxelCenterCS.x), abs(voxelCenterCS.y), abs(voxelCenterCS.z)) <= 1.0;
                if (!overlap)
                    clip(-1);

                FragInputs fragInputs = BuildFragInputs(input, voxelCenterRWS, voxelCenterCS);
                float3 albedo;
                float extinction;
                GetVolumeData(fragInputs, input.viewDirectionWS, albedo, extinction);

                extinction *= rcp(max(_FogVolumeFogDistanceProperty, 0.05));
                albedo *= _FogVolumeSingleScatteringAlbedo.rgb;
                float3 voxelCenterNDC = saturate(voxelCenterCS * 0.5 + 0.5);
                float fade = ComputeFadeFactor(voxelCenterNDC, sliceDistance);
                if ((uint)_FogVolumeBlendMode == LOCALVOLUMETRICFOGBLENDINGMODE_MULTIPLY)
                {
                    outColor = max(0.0, lerp(float4(1.0, 1.0, 1.0, 1.0), float4(albedo * extinction, extinction), fade.xxxx));
                }
                else
                {
                    extinction *= fade;
                    outColor = max(0.0, float4(saturate(albedo * extinction), extinction));
                }
            }
            ENDHLSL
        }
    }

    FallBack Off
}
