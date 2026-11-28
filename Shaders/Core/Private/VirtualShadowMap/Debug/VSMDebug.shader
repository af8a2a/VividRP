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

            Texture2DArray<uint> _VSMPhysicalPagePool;
            int _VSMPrototypeAvailable;
            int _VSMDebugVisualizationMode;
            int _VSMDebugPoolMode;
            int _VSMDebugDepthLayer;
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
                uint layers;
                _VSMPhysicalPagePool.GetDimensions(pageWidth, pageHeight, layers);
                uint2 pageSize = max(uint2(pageWidth, pageHeight), 1u);
                uint2 texel = min(
                    uint2(saturate(input.uv) * pageSize),
                    pageSize - 1u);
                // Serialized Combined/Dynamic modes both show the final UE depth slice.
                uint slice = _VSMDebugPoolMode == VIVID_VSM_DEBUG_POOL_STATIC ? 1u : 0u;
                uint rawDepth = _VSMPhysicalPagePool.Load(int4(texel, slice, 0));

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
            #include "../Paging/VSMPageDefinitions.hlsl"
            #include "VSMPageDebug.hlsl"

            StructuredBuffer<uint> _VSMPrototypePageTable;
            StructuredBuffer<uint4> _VSMPrototypePageMetadata;
            StructuredBuffer<uint> _VSMPageRequestFlags;
            StructuredBuffer<uint> _VSMPrototypeAllocatorCounters;
            float4 _VSMDebugPageLayout; // pages/axis, virtual entry count, physical capacity
            float4 _VSMDebugOutputSize;
            int _VSMDebugVisualizationMode;
            uint _VSMDebugFrameIndex;

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

            float3 ExtendedLegend(float2 pixel, float width, float scale, float3 background)
            {
                uint cell = min((uint)(pixel.x * 6.0 / width), 5u);
                int2 p = (int2)((pixel - float2(cell * width / 6.0, 16.0 * scale)) / scale);
                uint3 labelBits = uint3(0x3b6bu, 0x72cfu, 0x12cfu); // DEF
                float3 color = float3(1.0, 0.6, 0.0);
                uint mode = (uint)_VSMDebugVisualizationMode;
                if (mode == 12u)
                {
                    static const uint roles[6] = {257u, 2049u, 513u, 1025u, 1u, 0u};
                    // CRS, PAR, PRI, TRN, REQ, NON
                    static const uint3 labels[6] = {uint3(0x724fu,0x5aebu,0x79cfu), uint3(0x12ebu,0x5beau,0x5aebu), uint3(0x12ebu,0x5aebu,0x7497u), uint3(0x2497u,0x5aebu,0x5ffdu), uint3(0x5aebu,0x72cfu,0x4f6fu), uint3(0x5ffdu,0x7b6fu,0x5ffdu)};
                    labelBits = labels[cell];
                    color = cell == 5u ? 0.3 : VSMDebugRequestColor(roles[cell]);
                }
                else if (mode == 13u || mode == 14u)
                {
                    uint value = mode == 13u ? cell : cell * 24u;
                    color = mode == 13u ? VSMDebugIndexColor(value)
                        : lerp(float3(0.1,0.8,0.2), float3(1,0.1,0.1), value / 120.0);
                    return Number(p - int2(2, 2), value) ? color : background;
                }
                else if (mode == 15u || mode == 16u)
                {
                    // CCH, DRW, DEF, UNM
                    static const uint3 labels[4] = {uint3(0x724fu,0x724fu,0x5bedu), uint3(0x3b6bu,0x5aebu,0x5fedu), uint3(0x3b6bu,0x72cfu,0x12cfu), uint3(0x7b6du,0x5ffdu,0x5bfdu)};
                    static const float3 colors[4] = {float3(0.1,0.8,0.2), float3(1,0.1,0.1), float3(1,0.6,0), float3(0.3,0.3,0.3)};
                    if (cell >= 4u) return background;
                    labelBits = labels[cell]; color = colors[cell];
                }
                else if (mode == 17u)
                {
                    // EMP, STA, DYN, BTH, UNK, DEF
                    static const uint3 labels[6] = {uint3(0x72cfu,0x5bfdu,0x12ebu), uint3(0x79cfu,0x2497u,0x5beau), uint3(0x3b6bu,0x24adu,0x5ffdu), uint3(0x3aebu,0x2497u,0x5bedu), uint3(0x7b6du,0x5ffdu,0x5aedu), uint3(0x3b6bu,0x72cfu,0x12cfu)};
                    static const float3 colors[6] = {float3(0.2,0.2,0.2), float3(0.1,0.8,0), float3(0,0,1), float3(0.1,0.8,1), float3(1,0,1), float3(1,0.6,0)};
                    labelBits = labels[cell]; color = colors[cell];
                }
                else if (cell != 0u) return background;
                return Label(p - int2(2, 2), labelBits) ? color : background;
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

                if (_VSMDebugVisualizationMode >= 11)
                    return ExtendedLegend(pixel, width, scale, color);
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
                uint flags = metadata.w | _VSMPageRequestFlags[index];
                bool allocated = metadata.y != 0u && (metadata.x & 2u) != 0u;
                uint dirtyMask = kVSMPageDirty | kVSMPageDynamicDirty;
                uint state = !allocated ? 8u : ((flags & dirtyMask) != 0u ? 6u : ((flags & 8u) != 0u ? 7u : 5u));
                if ((flags & 64u) != 0u) state = 9u;
                if ((flags & 128u) != 0u) state = 10u;
                uint mode = (uint)_VSMDebugVisualizationMode;
                bool highlighted = mode == 3u
                    || (mode == 4u && (flags & 1u) != 0u)
                    || (mode == 5u && allocated)
                    || (mode == 6u && allocated && (flags & dirtyMask) != 0u)
                    || (mode == 7u && allocated && (flags & 8u) != 0u && (flags & dirtyMask) == 0u)
                    || (mode == 8u && !allocated)
                    || (mode == 9u && (flags & 64u) != 0u)
                    || (mode == 10u && (flags & 128u) != 0u);
                float3 color = highlighted ? StateColor(mode == 3u ? state : mode) : float3(0.015, 0.015, 0.015);
                if (mode == 3u && allocated && (metadata.x & kVSMPageDeferred) != 0u)
                    color = float3(1.0, 0.6, 0.0);
                if (mode == 11u && allocated && (metadata.x & kVSMPageDeferred) != 0u)
                    color = float3(1.0, 0.6, 0.0);
                if (mode == 12u) color = VSMDebugRequestColor(flags);
                if (mode == 13u && allocated)
                    color = VSMDebugIndexColor(metadata.y - 1u);
                if (mode == 14u && allocated)
                {
                    uint age = _VSMDebugFrameIndex >= metadata.z ? _VSMDebugFrameIndex - metadata.z : 0u;
                    color = lerp(float3(0.1, 0.8, 0.2), float3(1.0, 0.1, 0.1), saturate(age / 120.0));
                }
                if (mode == 15u || mode == 16u)
                    color = VSMDebugCacheColor(metadata, allocated, mode - 14u);
                if (mode == 17u) color = VSMDebugOccupancyColor(metadata.x, allocated);
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

        Pass
        {
            Name "VSMLocalPageDebug"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Shadow/VirtualShadowMap/VividVirtualShadowMapAddressing.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Shadow/VirtualShadowMap/VividVirtualShadowMapProjection.hlsl"
            #include "../Paging/VSMPageDefinitions.hlsl"
            #include "VSMPageDebug.hlsl"

            StructuredBuffer<uint> _VSMPrototypePageTable;
            StructuredBuffer<uint4> _VSMPrototypePageMetadata;
            StructuredBuffer<uint> _VSMPageRequestFlags;
            StructuredBuffer<uint> _VSMPrototypeAllocatorCounters;
            StructuredBuffer<VividVSMProjection> _VSMProjections;
            StructuredBuffer<uint4> _VSMPagePressure;
            float4 _VSMDebugPageLayout; // physPagesPerRow, totalEntries, physicalCapacity, 0
            float4 _VSMDebugLocalInfo;  // dirCount, projCount, focusedLocalIndex (-1 = all), physicalCapacity
            float4 _VSMDebugOutputSize;
            int _VSMDebugVisualizationMode;
            uint _VSMDebugFrameIndex;

            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vert(uint vertexID : SV_VertexID)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
                return output;
            }

            float3 LocalStateColor(uint mode)
            {
                if (mode == 4u) return float3(1.0, 0.85, 0.1);
                if (mode == 5u) return float3(0.1, 1.0, 0.3);
                if (mode == 6u) return float3(1.0, 0.45, 0.05);
                if (mode == 7u) return float3(0.1, 0.55, 1.0);
                if (mode == 8u) return float3(0.3, 0.3, 0.3);
                if (mode == 9u) return float3(0.7, 0.25, 1.0);
                return float3(1.0, 0.05, 0.1);
            }

            bool LocalGlyph(int2 p, uint bits)
            {
                return all(p >= 0) && p.x < 3 && p.y < 5
                    && ((bits >> (p.y * 3 + p.x)) & 1u) != 0u;
            }

            bool LocalLabel(int2 p, uint3 letters)
            {
                return p.x >= 0 && p.x < 12 && LocalGlyph(int2(p.x % 4, p.y), letters[p.x / 4]);
            }

            bool LocalNumber(int2 p, uint value)
            {
                static const uint digits[10] = {0x7b6fu, 0x749au, 0x73e7u, 0x79e7u, 0x49edu, 0x79cfu, 0x7bcfu, 0x2527u, 0x7befu, 0x79efu};
                if (p.x < 0 || p.x >= 20) return false;
                uint divisor = 10000u;
                for (int i = 0; i < p.x / 4; i++) divisor /= 10u;
                if (value < divisor && divisor != 1u) return false;
                return LocalGlyph(int2(p.x % 4, p.y), digits[(value / divisor) % 10u]);
            }

            float3 LocalHeader(float2 pixel, float width, float scale)
            {
                uint column = min((uint)(pixel.x * 4.0 / width), 3u);
                int2 p = (int2)((pixel - float2(column * width / 4.0, 0.0)) / scale);
                uint value = _VSMPrototypeAllocatorCounters[column];
                uint mode = column == 0u ? 5u : (column == 1u ? 4u : (column == 2u ? 6u : 10u));
                uint3 letters = column == 0u ? uint3(0x5aebu, 0x72cfu, 0x79cfu)
                    : (column == 1u ? uint3(0x5aebu, 0x72cfu, 0x4f6fu)
                    : (column == 2u ? uint3(0x5ffdu, 0x72cfu, 0x5fedu) : uint3(0x7b6fu, 0x2b6du, 0x12cfu)));
                float3 color = float3(0.025, 0.025, 0.025);
                if (LocalLabel(p - int2(2, 3), letters) || LocalNumber(p - int2(16, 3), value))
                    color = LocalStateColor(mode);
                if (column == 0u && (LocalGlyph(p - int2(38, 3), 0x12a4u)
                    || LocalNumber(p - int2(42, 3), (uint)_VSMDebugPageLayout.z)))
                    color = LocalStateColor(mode);
                if (p.y >= 10 && p.y < 12
                    && frac(pixel.x * 4.0 / width) < saturate(value / max(_VSMDebugPageLayout.z, 1.0)))
                    color = LocalStateColor(mode);

                // Row 2: display pool pressure bias (scaled x100) and active local VSM count.
                int2 p2 = (int2)((pixel - float2(0.0, 16.0 * scale)) / scale);
                uint pressureBiasX100 = (uint)round(saturate(asfloat(_VSMPagePressure[0].x) / 4.0) * 400.0);
                uint localCount = (uint)max(_VSMDebugLocalInfo.y - _VSMDebugLocalInfo.x, 0.0);
                if (LocalLabel(p2 - int2(2, 2), uint3(0x12ebu, 0x5aebu, 0x79cfu))
                    || LocalNumber(p2 - int2(16, 2), pressureBiasX100))
                    color = pressureBiasX100 > 0u ? float3(1.0, 0.6, 0.1) : float3(0.2, 0.9, 0.4);
                if (LocalLabel(p2 - int2(44, 2), uint3(0x7249u, 0x7b6fu, 0x724fu))
                    || LocalNumber(p2 - int2(58, 2), localCount))
                    color = float3(0.4, 0.85, 1.0);
                return color;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 size = max(_VSMDebugOutputSize.xy, 1.0);
                float scale = clamp(floor(size.x / 640.0), 1.0, 3.0);
                float headerHeight = min(26.0 * scale, size.y * 0.25);
                float2 pixel = input.positionCS.xy;
                if (pixel.y < headerHeight)
                    return float4(LocalHeader(pixel, size.x, scale), 1.0);

                uint dirCount = (uint)max(_VSMDebugLocalInfo.x, 0.0);
                uint projCount = (uint)max(_VSMDebugLocalInfo.y, 0.0);
                uint localCount = projCount > dirCount ? (projCount - dirCount) : 0u;
                if (localCount == 0u)
                    return float4(0.02, 0.02, 0.02, 1.0);

                int focusedLocal = (int)round(_VSMDebugLocalInfo.z);
                bool focusSingleVSM = focusedLocal >= 0 && (uint)focusedLocal < localCount;

                uint tileCount = localCount;
                uint vsmId = dirCount;
                uint mipLevel = 0u;
                if (focusSingleVSM)
                {
                    vsmId = dirCount + (uint)focusedLocal;
                    VividVSMProjection focusProj = _VSMProjections[vsmId];
                    tileCount = VividVSMProjectionIsSinglePage(focusProj) ? 1u : max(focusProj.addressing.w, 1u);
                }

                uint columns = max((uint)ceil(sqrt((float)tileCount)), 1u);
                uint2 grid = uint2(columns, (tileCount + columns - 1u) / columns);
                float2 gridUV = saturate(float2(pixel.x / size.x,
                    (pixel.y - headerHeight) / max(size.y - headerHeight, 1.0))) * grid;
                uint2 tile = min((uint2)gridUV, grid - 1u);
                uint tileIdx = tile.y * columns + tile.x;
                if (tileIdx >= tileCount)
                    return float4(0.015, 0.015, 0.015, 1.0);

                if (focusSingleVSM)
                    mipLevel = tileIdx;
                else
                    vsmId = dirCount + tileIdx;

                VividVSMProjection proj = _VSMProjections[vsmId];
                bool isSingle = VividVSMProjectionIsSinglePage(proj);
                uint mipPages = isSingle ? 1u : VividVSMMipPagesPerAxis(max(proj.addressing.z, 1u), mipLevel);
                float2 localUV = gridUV - (float2)tile;
                uint2 page = min((uint2)(localUV * mipPages), mipPages - 1u);
                uint index = VividVSMCalcProjectionPageIndex(proj, isSingle ? 0u : mipLevel, page);
                if (index >= (uint)_VSMDebugPageLayout.y)
                    return float4(0.015, 0.015, 0.015, 1.0);

                uint4 metadata = _VSMPrototypePageMetadata[index];
                uint flags = metadata.w | _VSMPageRequestFlags[index];
                bool allocated = metadata.y != 0u && (metadata.x & kVSMPageAllocated) != 0u;
                uint dirtyMask = kVSMPageDirty | kVSMPageDynamicDirty;
                uint state = !allocated ? 8u : ((flags & dirtyMask) != 0u ? 6u : ((flags & kVSMPageCached) != 0u ? 7u : 5u));
                if ((flags & kVSMPageDebugEvicted) != 0u) state = 9u;
                if ((flags & kVSMPageDebugOverflow) != 0u) state = 10u;
                uint mode = (uint)_VSMDebugVisualizationMode;
                bool highlighted = mode == 3u
                    || (mode == 4u && (flags & kVSMPageRequested) != 0u)
                    || (mode == 5u && allocated)
                    || (mode == 6u && allocated && (flags & dirtyMask) != 0u)
                    || (mode == 7u && allocated && (flags & kVSMPageCached) != 0u && (flags & dirtyMask) == 0u)
                    || (mode == 8u && !allocated)
                    || (mode == 9u && (flags & kVSMPageDebugEvicted) != 0u)
                    || (mode == 10u && (flags & kVSMPageDebugOverflow) != 0u);
                float3 color = highlighted ? LocalStateColor(mode == 3u ? state : mode) : float3(0.015, 0.015, 0.015);
                if (mode == 3u && allocated && (metadata.x & kVSMPageDeferred) != 0u)
                    color = float3(1.0, 0.6, 0.0);
                if (mode == 11u && allocated && (metadata.x & kVSMPageDeferred) != 0u)
                    color = float3(1.0, 0.6, 0.0);
                if (mode == 12u) color = VSMDebugRequestColor(flags);
                if (mode == 13u && allocated) color = VSMDebugIndexColor(metadata.y - 1u);
                if (mode == 14u && allocated)
                {
                    uint age = _VSMDebugFrameIndex >= metadata.z ? _VSMDebugFrameIndex - metadata.z : 0u;
                    color = lerp(float3(0.1, 0.8, 0.2), float3(1.0, 0.1, 0.1), saturate(age / 120.0));
                }
                if (mode == 15u || mode == 16u)
                    color = VSMDebugCacheColor(metadata, allocated, mode - 14u);
                if (mode == 17u) color = VSMDebugOccupancyColor(metadata.x, allocated);
                if (any(frac(localUV * mipPages) < 0.03)) color *= 0.4;

                // Corner tag: M<mip> when focused on a VSM, or P/S/D + index when viewing all local VSMs.
                int2 labelPixel = (int2)(localUV * float2(size.x, size.y - headerHeight) / grid / scale);
                if (labelPixel.x < 34 && labelPixel.y < 9)
                {
                    color = float3(0.025, 0.025, 0.025);
                    uint tagGlyph = focusSingleVSM ? 0x5bfdu // M
                        : (isSingle ? 0x3b6bu // D (Distant single-page)
                        : (proj.meta.x == VIVID_VSM_LIGHT_TYPE_POINT ? 0x12ebu
                        : (proj.meta.x == VIVID_VSM_LIGHT_TYPE_RECT ? 0x5aebu
                        : (proj.meta.x == VIVID_VSM_LIGHT_TYPE_TUBE ? 0x2497u : 0x79cfu)))); // P / R / T / S
                    uint tagNumber = focusSingleVSM ? mipLevel : tileIdx;
                    if (LocalGlyph(labelPixel - int2(2, 2), tagGlyph)
                        || LocalNumber(labelPixel - int2(8, 2), tagNumber))
                        color = isSingle ? float3(1.0, 0.7, 1.0) : float3(1.0, 1.0, 1.0);
                }
                return float4(color, 1.0);
            }
            ENDHLSL
        }

    }
}
