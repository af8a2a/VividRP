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
                {
                    if (_VSMDebugVisualizationMode >= 3)
                    {
                        float stripe = ((uint)(input.positionCS.x + input.positionCS.y) / 8u) % 2u;
                        return float4(lerp(float3(0.06, 0.06, 0.06), float3(0.3, 0.16, 0.04), stripe), 1.0);
                    }
                    return float4(0.0, 0.0, 0.0, 1.0);
                }

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

        Pass
        {
            Name "VSMPageDebug"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"

            StructuredBuffer<uint> _VSMPrototypePageTable;
            StructuredBuffer<uint4> _VSMPrototypePageMetadata;
            StructuredBuffer<uint> _VSMPrototypeAllocatorCounters;
            float4 _VSMDebugPageLayout; // pages/axis, virtual entry count, physical capacity
            float4 _VSMDebugOutputSize;
            int _VSMDebugVisualizationMode;

            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vert(uint vertexID : SV_VertexID)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
                return output;
            }

            float3 StateColor(uint mode)
            {
                if (mode == 4u) return float3(1.0, 0.85, 0.1);  // requested
                if (mode == 5u) return float3(0.1, 1.0, 0.3);   // allocated
                if (mode == 6u) return float3(1.0, 0.45, 0.05); // dirty/redrawn
                if (mode == 7u) return float3(0.1, 0.55, 1.0);  // cached without redraw
                if (mode == 8u) return float3(0.3, 0.3, 0.3);  // unmapped
                if (mode == 9u) return float3(0.7, 0.25, 1.0); // evicted
                return float3(1.0, 0.05, 0.1);                 // overflow
            }

            // Tiny embedded 3x5 glyphs keep the HUD GPU-only, with no font texture/readback.
            bool Glyph(int2 p, uint bits)
            {
                return all(p >= 0) && p.x < 3 && p.y < 5
                    && ((bits >> (p.y * 3 + p.x)) & 1u) != 0u;
            }

            bool Label(int2 p, uint3 letters)
            {
                return p.x >= 0 && p.x < 12 && Glyph(int2(p.x % 4, p.y), letters[p.x / 4]);
            }

            bool Number(int2 p, uint value)
            {
                static const uint digits[10] = {0x7b6fu, 0x749au, 0x73e7u, 0x79e7u, 0x49edu, 0x79cfu, 0x7bcfu, 0x2527u, 0x7befu, 0x79efu};
                if (p.x < 0 || p.x >= 20) return false;
                uint divisor = 10000u;
                for (int i = 0; i < p.x / 4; i++) divisor /= 10u;
                if (value < divisor && divisor != 1u) return false;
                return Glyph(int2(p.x % 4, p.y), digits[(value / divisor) % 10u]);
            }

            float3 Header(float2 pixel, float width, float scale)
            {
                uint column = min((uint)(pixel.x * 4.0 / width), 3u);
                int2 p = (int2)((pixel - float2(column * width / 4.0, 0.0)) / scale);
                uint value = _VSMPrototypeAllocatorCounters[column];
                uint mode = column == 0u ? 5u : (column == 1u ? 4u : (column == 2u ? 6u : 10u));
                uint3 letters = column == 0u ? uint3(0x5aebu, 0x72cfu, 0x79cfu)
                    : (column == 1u ? uint3(0x5aebu, 0x72cfu, 0x4f6fu)
                    : (column == 2u ? uint3(0x5ffdu, 0x72cfu, 0x5fedu) : uint3(0x7b6fu, 0x2b6du, 0x12cfu)));
                float3 color = float3(0.025, 0.025, 0.025);
                if (Label(p - int2(2, 3), letters) || Number(p - int2(16, 3), value))
                    color = StateColor(mode);
                // Resident count / physical budget, plus budget-relative bars for all counters.
                if (column == 0u && (Glyph(p - int2(38, 3), 0x12a4u)
                    || Number(p - int2(42, 3), (uint)_VSMDebugPageLayout.z)))
                    color = StateColor(mode);
                if (p.y >= 10 && p.y < 12
                    && frac(pixel.x * 4.0 / width) < saturate(value / max(_VSMDebugPageLayout.z, 1.0)))
                    color = StateColor(mode);

                uint legend = min((uint)(pixel.x * 7.0 / width), 6u);
                p = (int2)((pixel - float2(legend * width / 7.0, 16.0 * scale)) / scale);
                static const uint modes[7] = {5u, 4u, 6u, 7u, 8u, 9u, 10u};
                static const uint3 labels[7] = {uint3(0x5bfdu, 0x5beau, 0x12ebu), uint3(0x5aebu, 0x72cfu, 0x4f6fu), uint3(0x3b6bu, 0x5aebu, 0x2497u), uint3(0x724fu, 0x724fu, 0x5bedu), uint3(0x7b6du, 0x5ffdu, 0x5bfdu), uint3(0x72cfu, 0x2b6du, 0x7497u), uint3(0x7b6fu, 0x2b6du, 0x12cfu)};
                if (Label(p - int2(2, 2), labels[legend])) color = StateColor(modes[legend]);
                return color;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 size = max(_VSMDebugOutputSize.xy, 1.0);
                float scale = clamp(floor(size.x / 640.0), 1.0, 3.0);
                float headerHeight = min(26.0 * scale, size.y * 0.25);
                float2 pixel = input.positionCS.xy;
                if (pixel.y < headerHeight)
                    return float4(Header(pixel, size.x, scale), 1.0);

                uint pagesPerAxis = (uint)max(_VSMDebugPageLayout.x, 1.0);
                uint levelCount = max((uint)_VSMDebugPageLayout.y / (pagesPerAxis * pagesPerAxis), 1u);
                uint columns = (uint)ceil(sqrt((float)levelCount));
                uint2 grid = uint2(columns, (levelCount + columns - 1u) / columns);
                float2 gridUV = saturate(float2(pixel.x / size.x,
                    (pixel.y - headerHeight) / max(size.y - headerHeight, 1.0))) * grid;
                uint2 tile = min((uint2)gridUV, grid - 1u);
                uint cascade = tile.y * columns + tile.x;
                float2 localUV = gridUV - (float2)tile;
                uint2 page = min((uint2)(localUV * pagesPerAxis), pagesPerAxis - 1u);
                uint index = cascade * pagesPerAxis * pagesPerAxis + page.y * pagesPerAxis + page.x;
                if (index >= (uint)_VSMDebugPageLayout.y)
                    return float4(0.015, 0.015, 0.015, 1.0);

                uint4 metadata = _VSMPrototypePageMetadata[index];
                uint flags = metadata.w; // last allocation/static-render submission, not next feedback
                bool allocated = _VSMPrototypePageTable[index] != 0u && (metadata.x & 2u) != 0u;
                uint state = !allocated ? 8u : ((flags & 4u) != 0u ? 6u : ((flags & 8u) != 0u ? 7u : 5u));
                if ((flags & 64u) != 0u) state = 9u;
                if ((flags & 128u) != 0u) state = 10u;
                uint mode = (uint)_VSMDebugVisualizationMode;
                bool highlighted = mode == 3u
                    || (mode == 4u && (flags & 1u) != 0u)
                    || (mode == 5u && allocated)
                    || (mode == 6u && allocated && (flags & 4u) != 0u)
                    || (mode == 7u && allocated && (flags & 8u) != 0u && (flags & 4u) == 0u)
                    || (mode == 8u && !allocated)
                    || (mode == 9u && (flags & 64u) != 0u)
                    || (mode == 10u && (flags & 128u) != 0u);
                float3 color = highlighted ? StateColor(mode == 3u ? state : mode) : float3(0.015, 0.015, 0.015);
                if (any(frac(localUV * pagesPerAxis) < 0.03)) color *= 0.4;
                // Label every clipmap level; unused cells in the last row stay blank.
                int2 labelPixel = (int2)(localUV * float2(size.x, size.y - headerHeight) / grid / scale);
                if (labelPixel.x < 32 && labelPixel.y < 9)
                {
                    color = float3(0.025, 0.025, 0.025);
                    if (Glyph(labelPixel - int2(2, 2), 0x124fu)
                        || Number(labelPixel - int2(8, 2), cascade)) color = 1.0;
                }
                return float4(color, 1.0);
            }
            ENDHLSL
        }

    }
}
