Shader "Hidden/VividRP/ReflectionProbeAtlasDebug"
{
    SubShader
    {
        Tags { "RenderPipeline" = "VividRenderPipeline" }

        Pass
        {
            Name "ReflectionProbeAtlasDebug"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"

            #define VIVID_REFLECTION_PROBE_ATLAS_DEBUG_NONE 0
            #define VIVID_REFLECTION_PROBE_ATLAS_DEBUG_ATLAS 1
            #define VIVID_REFLECTION_PROBE_ATLAS_DEBUG_SLOT 2

            TEXTURE2D_ARRAY(_ReflectionAtlas);
            SAMPLER(sampler_ReflectionAtlas);

            int _ReflectionAtlasDebugAvailable;
            int _ReflectionAtlasDebugMode;
            int _ReflectionAtlasDebugSlice;
            int _ReflectionAtlasDebugMip;
            int _ReflectionAtlasDebugMipCount;
            int _ReflectionAtlasDebugSliceCount;
            float _ReflectionAtlasDebugExposure;
            float4 _ReflectionAtlasDebugScaleOffset;
            int _ReflectionAtlasDebugHasScaleOffset;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                if (_ReflectionAtlasDebugAvailable == 0
                    || _ReflectionAtlasDebugMode == VIVID_REFLECTION_PROBE_ATLAS_DEBUG_NONE
                    || _ReflectionAtlasDebugSliceCount <= 0
                    || _ReflectionAtlasDebugMipCount <= 0)
                {
                    return float4(0.0, 0.0, 0.0, 1.0);
                }

                int sliceIndex = clamp(_ReflectionAtlasDebugSlice, 0, _ReflectionAtlasDebugSliceCount - 1);
                int mipLevel = clamp(_ReflectionAtlasDebugMip, 0, _ReflectionAtlasDebugMipCount - 1);
                float2 atlasUV = saturate(input.uv);

                if (_ReflectionAtlasDebugMode == VIVID_REFLECTION_PROBE_ATLAS_DEBUG_SLOT)
                {
                    if (_ReflectionAtlasDebugHasScaleOffset == 0)
                        return float4(0.0, 0.0, 0.0, 1.0);

                    atlasUV = atlasUV * _ReflectionAtlasDebugScaleOffset.xy + _ReflectionAtlasDebugScaleOffset.zw;
                }

                float4 atlasColor = SAMPLE_TEXTURE2D_ARRAY_LOD(
                    _ReflectionAtlas,
                    sampler_ReflectionAtlas,
                    saturate(atlasUV),
                    (float)sliceIndex,
                    (float)mipLevel);

                return float4(atlasColor.rgb * exp2(_ReflectionAtlasDebugExposure), 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
