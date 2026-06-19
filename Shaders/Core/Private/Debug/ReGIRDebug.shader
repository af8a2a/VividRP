Shader "Hidden/VividRP/ReGIRDebug"
{
    SubShader
    {
        Tags { "RenderPipeline" = "VividRenderPipeline" }

        Pass
        {
            Name "ReGIRDebug"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/ReGIR.hlsl"

            #define VIVID_REGIR_DEBUG_NONE 0
            #define VIVID_REGIR_DEBUG_CELLS 1
            #define VIVID_REGIR_DEBUG_RESERVOIR_OCCUPANCY 2
            #define VIVID_REGIR_DEBUG_RESERVOIR_WEIGHT 3
            #define VIVID_REGIR_DEBUG_MAX_SLOTS_PER_CELL 1024u

            TEXTURE2D(_SourceTexture);
            SAMPLER(sampler_SourceTexture);
            TEXTURE2D(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);

            StructuredBuffer<VividReGIRParameters> _ReGIRParameters;
            StructuredBuffer<VividReGIRReservoir> _ReGIRReservoirs;

            float4 _SourceTextureScaleBias;
            float4 _CameraDepthTextureScaleBias;
            float4 _ReGIRDebugViewportSize;
            int _ReGIRDebugMode;
            float _ReGIRDebugOpacity;

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

            uint VividReGIRDebugHash(uint value)
            {
                value ^= value >> 16;
                value *= 0x7feb352du;
                value ^= value >> 15;
                value *= 0x846ca68bu;
                value ^= value >> 16;
                return value;
            }

            float3 VividReGIRDebugHashColor(uint seed)
            {
                uint hash = VividReGIRDebugHash(seed);
                float3 color;
                color.x = (hash & 0x7ffu) / 2047.0;
                color.y = ((hash >> 11) & 0x7ffu) / 2047.0;
                color.z = ((hash >> 22) & 0x3ffu) / 1023.0;
                return saturate(0.2 + color * 0.8);
            }

            bool IsSkyDepth(float deviceDepth)
            {
                return abs(deviceDepth - UNITY_RAW_FAR_CLIP_VALUE) <= 1e-6;
            }

            float4 AlphaBlend(float4 background, float4 foreground)
            {
                return float4(
                    lerp(background.rgb, foreground.rgb, foreground.a),
                    background.a + foreground.a - background.a * foreground.a);
            }

            float3 HeatColor(float value)
            {
                float heat = saturate(value);
                float3 cold = float3(0.05, 0.35, 1.0);
                float3 mid = float3(0.1, 0.95, 0.45);
                float3 hot = float3(1.0, 0.25, 0.05);
                return heat < 0.5
                    ? lerp(cold, mid, heat * 2.0)
                    : lerp(mid, hot, heat * 2.0 - 1.0);
            }

            float ResolveCellEdgeFactor(VividReGIRParameters parameters, float3 worldPos)
            {
                if (parameters.mode != VIVID_REGIR_MODE_GRID)
                    return 0.0;

                float cellSize = max(parameters.cellSize, 1e-5);
                float3 gridSize = float3(
                    max(parameters.gridSizeX, 1u),
                    max(parameters.gridSizeY, 1u),
                    max(parameters.gridSizeZ, 1u));
                float3 gridOrigin = parameters.centerWS - gridSize * (cellSize * 0.5);
                float3 localCell = (worldPos - gridOrigin) / cellSize;
                float3 cellFrac = frac(localCell);
                float3 distanceToEdge = min(cellFrac, 1.0 - cellFrac);
                float edgeDistance = min(distanceToEdge.x, min(distanceToEdge.y, distanceToEdge.z));
                return 1.0 - smoothstep(0.0, 0.035, edgeDistance);
            }

            uint ResolveReservoirStats(
                VividReGIRParameters parameters,
                uint cellIndex,
                out float weightSum)
            {
                uint validCount = 0u;
                weightSum = 0.0;

                uint lightsPerCell = min(parameters.lightsPerCell, VIVID_REGIR_DEBUG_MAX_SLOTS_PER_CELL);
                uint slotStart = cellIndex * parameters.lightsPerCell;
                uint slotEnd = min(slotStart + lightsPerCell, parameters.slotCount);

                [loop]
                for (uint slotIndex = slotStart; slotIndex < slotEnd; slotIndex++)
                {
                    VividReGIRReservoir reservoir = _ReGIRReservoirs[slotIndex];
                    if (reservoir.lightIndex == VIVID_REGIR_INVALID_LIGHT_INDEX || reservoir.weight <= 0.0)
                        continue;

                    validCount++;
                    weightSum += reservoir.weight;
                }

                return validCount;
            }

            float3 EvaluateReGIRDebugOverlay(
                VividReGIRParameters parameters,
                uint cellIndex,
                float3 worldPos,
                out float alpha)
            {
                alpha = saturate(_ReGIRDebugOpacity);

                if (_ReGIRDebugMode == VIVID_REGIR_DEBUG_CELLS)
                {
                    float3 cellCenter;
                    float cellRadius;
                    bool cellFound = VividReGIRCellIndexToWorldPos(parameters, cellIndex, cellCenter, cellRadius);
                    float distanceToCenter = cellFound
                        ? saturate(length(cellCenter - worldPos) / max(cellRadius, 1e-5))
                        : 0.0;
                    float edge = ResolveCellEdgeFactor(parameters, worldPos);
                    return lerp(
                        distanceToCenter * VividReGIRDebugHashColor((uint)cellIndex),
                        float3(1.0, 1.0, 1.0),
                        edge * 0.35);
                }

                float weightSum;
                uint validCount = ResolveReservoirStats(parameters, cellIndex, weightSum);
                float occupancy = validCount / max((float)parameters.lightsPerCell, 1.0);
                float value = occupancy;

                if (_ReGIRDebugMode == VIVID_REGIR_DEBUG_RESERVOIR_WEIGHT)
                {
                    float averageWeight = validCount > 0u ? weightSum / (float)validCount : 0.0;
                    value = saturate(log2(1.0 + max(averageWeight, 0.0)) / 8.0);
                }

                alpha *= lerp(0.25, 1.0, saturate(value));
                float edgeFactor = ResolveCellEdgeFactor(parameters, worldPos);
                return lerp(HeatColor(value), float3(1.0, 1.0, 1.0), edgeFactor * 0.5);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                uint2 viewportSize = uint2(
                    max((uint)_ReGIRDebugViewportSize.x, 1u),
                    max((uint)_ReGIRDebugViewportSize.y, 1u));
                uint2 pixelCoord = min(
                    (uint2)(saturate(input.uv) * viewportSize),
                    viewportSize - 1u);
                float2 pixelUv = (float2(pixelCoord) + 0.5) * _ReGIRDebugViewportSize.zw;
                float2 sourceUv = ApplyScaleBias(pixelUv, _SourceTextureScaleBias);
                float2 depthUv = ApplyScaleBias(pixelUv, _CameraDepthTextureScaleBias);
                float4 sourceColor = SAMPLE_TEXTURE2D(_SourceTexture, sampler_SourceTexture, sourceUv);

                if (_ReGIRDebugMode == VIVID_REGIR_DEBUG_NONE || _ReGIRDebugOpacity <= 0.0)
                    return sourceColor;

                VividReGIRParameters parameters = _ReGIRParameters[0];
                if (parameters.cellSize <= 0.0
                    || parameters.gridSizeX == 0u
                    || parameters.gridSizeY == 0u
                    || parameters.gridSizeZ == 0u
                    || parameters.lightsPerCell == 0u
                    || parameters.slotCount == 0u)
                {
                    return sourceColor;
                }

                float deviceDepth = SAMPLE_TEXTURE2D_LOD(_CameraDepthTexture, sampler_PointClamp, depthUv, 0).r;
                if (IsSkyDepth(deviceDepth))
                    return sourceColor;

                float3 worldPos = ComputeWorldSpacePosition(pixelUv, deviceDepth, UNITY_MATRIX_I_VP);
                int cellIndex = VividReGIRWorldPosToCellIndex(parameters, worldPos);
                if (cellIndex < 0)
                    return sourceColor;

                float overlayAlpha;
                float3 overlayColor = EvaluateReGIRDebugOverlay(parameters, (uint)cellIndex, worldPos, overlayAlpha);
                return AlphaBlend(sourceColor, float4(overlayColor, overlayAlpha));
            }
            ENDHLSL
        }
    }

    FallBack Off
}
