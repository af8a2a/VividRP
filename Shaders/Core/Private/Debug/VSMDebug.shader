Shader "Hidden/VividRP/VSMDebug"
{
    SubShader
    {
        Tags { "RenderPipeline" = "VividRenderPipeline" }

        Pass
        {
            Name "VSMDebug"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"

            #define VIVID_VSM_DEBUG_DEVICE_DEPTH 0
            #define VIVID_VSM_DEBUG_OCCUPANCY 1
            #define VIVID_VSM_DEBUG_DEPTH_HEAT_MAP 2
            #define VIVID_VSM_DEBUG_POOL_COMBINED 0
            #define VIVID_VSM_DEBUG_POOL_STATIC 1
            #define VIVID_VSM_DEBUG_POOL_DYNAMIC 2

            Texture2D<uint> _VSMPrototypeStaticPhysicalPage;
            Texture2D<uint> _VSMPrototypeDynamicPhysicalPage;
            int _VSMPrototypeAvailable;
            int _VSMDebugVisualizationMode;
            int _VSMDebugPoolMode;
            float _VSMDebugExposure;

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

            float3 DepthHeatMap(float value)
            {
                float3 offsets = float3(3.0, 2.0, 1.0);
                return saturate(1.5 - abs(value * 4.0 - offsets));
            }

            float4 Frag(Varyings input) : SV_Target
            {
                if (_VSMPrototypeAvailable == 0)
                    return float4(0.0, 0.0, 0.0, 1.0);

                uint pageWidth;
                uint pageHeight;
                _VSMPrototypeStaticPhysicalPage.GetDimensions(pageWidth, pageHeight);
                uint2 pageSize = max(uint2(pageWidth, pageHeight), 1u);
                uint2 texel = min(
                    uint2(saturate(input.uv) * pageSize),
                    pageSize - 1u);
                uint staticRawDepth = _VSMPrototypeStaticPhysicalPage.Load(
                    int3(texel, 0));
                uint dynamicRawDepth = _VSMPrototypeDynamicPhysicalPage.Load(
                    int3(texel, 0));
                uint rawDepth = _VSMDebugPoolMode == VIVID_VSM_DEBUG_POOL_STATIC
                    ? staticRawDepth
                    : (_VSMDebugPoolMode == VIVID_VSM_DEBUG_POOL_DYNAMIC
                        ? dynamicRawDepth
                        : max(staticRawDepth, dynamicRawDepth));

                if (_VSMDebugVisualizationMode == VIVID_VSM_DEBUG_OCCUPANCY)
                {
                    float occupied = rawDepth != 0u ? 1.0 : 0.0;
                    return float4(occupied * float3(0.15, 1.0, 0.25), 1.0);
                }

                if (rawDepth == 0u)
                    return float4(0.0, 0.0, 0.0, 1.0);

                float deviceDepth = saturate(
                    asfloat(rawDepth) * exp2(_VSMDebugExposure));
                if (_VSMDebugVisualizationMode == VIVID_VSM_DEBUG_DEPTH_HEAT_MAP)
                    return float4(DepthHeatMap(deviceDepth), 1.0);

                return float4(deviceDepth.xxx, 1.0);
            }
            ENDHLSL
        }
    }
}
