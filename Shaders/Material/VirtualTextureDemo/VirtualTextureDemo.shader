Shader "VividRP/Material/VirtualTextureDemo"
{
    Properties
    {
        [Main(SurfaceInputs, _, on, off)] _SurfaceInputs("Surface Inputs", Float) = 1
        [MainTexture] [Tex(SurfaceInputs, _BaseTint)] _BaseMap("SVT Source Map", 2D) = "white" {}
        [MainColor] _BaseTint("Base Tint", Color) = (1, 1, 1, 1)
        [HideInInspector] _MainTex("BaseMap", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "VividRenderPipeline"
        }

        Pass
        {
            Name "VividVT"
            Tags { "LightMode" = "VividVT" }

            Blend One Zero
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
                #pragma target 5.0
                #pragma require randomwrite
                #pragma multi_compile_instancing
                #pragma vertex Vert
                #pragma fragment Frag

                #define VIVID_VT_ENABLE_FEEDBACK_RW 1

                #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Input.hlsl"
                #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"
                #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/AutoExposure.hlsl"
                #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/VirtualTexture/VirtualTexture.hlsl"

                CBUFFER_START(UnityPerMaterial)
                    float4 _BaseTint;
                    float4 _BaseMap_ST;
                CBUFFER_END

                struct Attributes
                {
                    float4 positionOS : POSITION;
                    float2 uv : TEXCOORD0;
                    UNITY_VERTEX_INPUT_INSTANCE_ID
                };

                struct Varyings
                {
                    float4 positionCS : SV_POSITION;
                    float2 uv : TEXCOORD0;
                    UNITY_VERTEX_INPUT_INSTANCE_ID
                    UNITY_VERTEX_OUTPUT_STEREO
                };

                #define VirtualTextureDebugMode_None 0
                #define VirtualTextureDebugMode_Residency 1
                #define VirtualTextureDebugMode_MipBias 2
                #define VirtualTextureDebugMode_PhysicalPageId 3

                Varyings Vert(Attributes input)
                {
                    Varyings output;

                    UNITY_SETUP_INSTANCE_ID(input);
                    UNITY_TRANSFER_INSTANCE_ID(input, output);
                    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                    output.uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                    return output;
                }

                float3 HashPhysicalPageColor(uint physicalPageId)
                {
                    float3 seed = float3(
                        frac((physicalPageId + 1u) * 0.1031),
                        frac((physicalPageId + 1u) * 0.11369),
                        frac((physicalPageId + 1u) * 0.13787));
                    return saturate(0.2 + seed * 0.8);
                }

                float3 ResolveDebugColor(float requestedMipLevel, VTResolvedAddress resolved, float4 sampledColor)
                {
                    if (_VTDebugMode == VirtualTextureDebugMode_Residency)
                    {
                        if (!resolved.valid)
                            return float3(1.0, 0.0, 1.0);

                        if (resolved.pendingUpload)
                            return float3(0.15, 0.7, 1.0);

                        if (resolved.fallback)
                            return float3(1.0, 0.78, 0.15);

                        return float3(0.15, 1.0, 0.25);
                    }

                    if (_VTDebugMode == VirtualTextureDebugMode_MipBias)
                    {
                        if (!resolved.valid)
                            return float3(1.0, 0.0, 1.0);

                        float mipBias = saturate(abs(requestedMipLevel - (float)resolved.resolvedMip) / max((float)VT_MIP_COUNT - 1.0, 1.0));
                        return lerp(float3(0.1, 0.9, 0.2), float3(1.0, 0.15, 0.1), mipBias);
                    }

                    if (_VTDebugMode == VirtualTextureDebugMode_PhysicalPageId)
                    {
                        if (!resolved.valid)
                            return float3(1.0, 0.0, 1.0);

                        float3 pageColor = HashPhysicalPageColor(resolved.physicalPageId);
                        return resolved.fallback ? lerp(pageColor, float3(1.0, 1.0, 1.0), 0.35) : pageColor;
                    }

                    return sampledColor.rgb;
                }

                half4 Frag(Varyings input) : SV_Target
                {
                    UNITY_SETUP_INSTANCE_ID(input);
                    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                    VTMipRange requestedMips = VTComputeRequestedMipRange(input.uv);
                    VTResolvedAddress lowerResolved = VTResolveAddress(input.uv, requestedMips.lowerMip);
                    VTResolvedAddress upperResolved = VTResolveAddress(input.uv, requestedMips.upperMip);
                    bool resolvedMipsDiffer = !VTResolvedAddressMatches(lowerResolved, upperResolved);

                    VTWriteFallbackSample(input.uv, requestedMips.lowerMip, lowerResolved, input.positionCS);
                    if (resolvedMipsDiffer)
                        VTWriteFallbackSample(input.uv, requestedMips.upperMip, upperResolved, input.positionCS);

                    if (!lowerResolved.resident)
                        VTWriteFeedback(input.uv, requestedMips.lowerMip, input.positionCS);
                    if (requestedMips.upperMip != requestedMips.lowerMip && !upperResolved.resident)
                        VTWriteFeedback(input.uv, requestedMips.upperMip, input.positionCS);

                    float4 sampledColor = VTSampleBaseColor(
                        input.uv,
                        lowerResolved,
                        upperResolved,
                        requestedMips.blend) * _BaseTint;
                    sampledColor.rgb = ResolveDebugColor(requestedMips.level, lowerResolved, sampledColor);
                    return sampledColor;
                }
            ENDHLSL
        }
    }

    CustomEditor "LWGUI.LWGUI"

    FallBack Off
}
