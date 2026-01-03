Shader "Hidden/Universal/WorldLightClusterDebug"
{
    SubShader
    {
        Tags{ "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "WorldLightClusterDebug"
            ZWrite Off
            Cull Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 4.5
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch

            #pragma vertex Vert
            #pragma fragment Frag

            //-------------------------------------------------------------------------------------
            // Include
            //-------------------------------------------------------------------------------------
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Debug.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/DebuggingFullscreen.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/DebugViewEnums.cs.hlsl"

            //-------------------------------------------------------------------------------------
            // World Light Cluster Resources
            //-------------------------------------------------------------------------------------
            StructuredBuffer<uint2> _WorldLightGridCells;   // (offset, count) per cell
            StructuredBuffer<uint> _WorldLightIndices;      // Light indices per cell

            int _WorldLightCount;
            int _WorldLightGridResolution;
            float3 _WorldLightGridMin;
            float2 _WorldLightGridCellSize; // x = cellSize, y = invCellSize

            //-------------------------------------------------------------------------------------
            // Debug Variables
            //-------------------------------------------------------------------------------------
            uniform float4 _BlitScaleBias;
            float _DebugWorldLightClusterMode;
            float _YFlip;
            int _MaxLightsPerCellDisplay;

            //-------------------------------------------------------------------------------------
            // Structures
            //-------------------------------------------------------------------------------------
            #if SHADER_API_GLES
            struct Attributes
            {
                float4 positionOS       : POSITION;
                float2 uv               : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            #else
            struct Attributes
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            #endif

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 texcoord   : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            //-------------------------------------------------------------------------------------
            // Helper Functions
            //-------------------------------------------------------------------------------------

            // Convert world position to grid cell coordinate
            int3 WorldToGridCell(float3 positionWS)
            {
                float3 localPos = positionWS - _WorldLightGridMin;
                return int3(localPos * _WorldLightGridCellSize.y);
            }

            // Get flat cell index from 3D coordinates
            int GetCellIndex(int3 cellCoord)
            {
                if (any(cellCoord < 0) || any(cellCoord >= _WorldLightGridResolution))
                    return -1;

                return cellCoord.x
                     + cellCoord.y * _WorldLightGridResolution
                     + cellCoord.z * _WorldLightGridResolution * _WorldLightGridResolution;
            }

            // Get light count at a cell
            uint GetCellLightCount(int cellIndex)
            {
                if (cellIndex < 0)
                    return 0;
                return _WorldLightGridCells[cellIndex].y;
            }

            // Reconstruct world position from depth
            float3 ComputeWorldSpacePosition(float2 positionNDC, float deviceDepth)
            {
                float4 positionCS = float4(positionNDC * 2.0 - 1.0, deviceDepth, 1.0);
                #if UNITY_UV_STARTS_AT_TOP
                positionCS.y = -positionCS.y;
                #endif
                float4 hpositionWS = mul(UNITY_MATRIX_I_VP, positionCS);
                return hpositionWS.xyz / hpositionWS.w;
            }

            // Generate a color from cell index for visualization
            float3 CellIndexToColor(int3 cellCoord)
            {
                // Use modulo to create repeating color pattern
                float3 color;
                color.r = frac(float(cellCoord.x) * 0.1234 + float(cellCoord.z) * 0.5678);
                color.g = frac(float(cellCoord.y) * 0.3456 + float(cellCoord.x) * 0.7890);
                color.b = frac(float(cellCoord.z) * 0.5678 + float(cellCoord.y) * 0.1234);
                return saturate(color * 0.8 + 0.2); // Ensure visible colors
            }

            // Check if pixel is on cell boundary
            bool IsOnCellBoundary(int3 cellCoord, float3 localPos, float cellSize)
            {
                float3 cellLocalPos = frac(localPos / cellSize);
                float borderWidth = 0.02; // 2% border

                return any(cellLocalPos < borderWidth) || any(cellLocalPos > (1.0 - borderWidth));
            }

            //-------------------------------------------------------------------------------------
            // Vertex Shader
            //-------------------------------------------------------------------------------------
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

            #if SHADER_API_GLES
                float4 pos = input.positionOS;
                float2 uv  = input.uv;
            #else
                float4 pos = GetFullScreenTriangleVertexPosition(input.vertexID);
                float2 uv  = GetFullScreenTriangleTexCoord(input.vertexID);
            #endif

                // Y-flip for game view vs scene view
                pos.y *= _YFlip > 0 ? -1 : 1;

                output.positionCS = pos;
                output.texcoord   = uv * _BlitScaleBias.xy + _BlitScaleBias.zw;
                return output;
            }

            //-------------------------------------------------------------------------------------
            // Fragment Shader
            //-------------------------------------------------------------------------------------
            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                uint2 pixelCoord = uint2(uv * _ScreenParams.xy);

                // Sample depth
                float rawDepth = LoadSceneDepth(pixelCoord);

                // Skip sky pixels
                if (rawDepth == UNITY_RAW_FAR_CLIP_VALUE)
                    return float4(0, 0, 0, 0);

                // Reconstruct world position
                float3 positionWS = ComputeWorldSpacePosition(uv, rawDepth);

                // Get cell coordinate
                int3 cellCoord = WorldToGridCell(positionWS);
                int cellIndex = GetCellIndex(cellCoord);

                float4 result = float4(0, 0, 0, 0);

                // Light Count Heatmap Mode
                if (_DebugWorldLightClusterMode == DEBUGWORLDLIGHTCLUSTERMODE_LIGHT_COUNT_HEATMAP)
                {
                    if (cellIndex >= 0)
                    {
                        uint lightCount = GetCellLightCount(cellIndex);

                        if (lightCount > 0)
                        {
                            // Use heatmap visualization (0 = blue, max = red)
                            int maxLights = max(_MaxLightsPerCellDisplay, 1);
                            result = OverlayHeatMap(pixelCoord, 16, lightCount, maxLights, 0.15);
                        }
                    }
                }
                // Cell Grid Mode
                else if (_DebugWorldLightClusterMode == DEBUGWORLDLIGHTCLUSTERMODE_CELL_GRID)
                {
                    if (cellIndex >= 0)
                    {
                        float3 localPos = positionWS - _WorldLightGridMin;
                        float cellSize = _WorldLightGridCellSize.x;

                        if (IsOnCellBoundary(cellCoord, localPos, cellSize))
                        {
                            // Draw grid lines in white
                            result = float4(1, 1, 1, 0.5);
                        }
                        else
                        {
                            // Fill cells with their index color (low opacity)
                            float3 cellColor = CellIndexToColor(cellCoord);
                            result = float4(cellColor, 0.1);
                        }
                    }
                    else
                    {
                        // Outside grid - red tint
                        result = float4(1, 0, 0, 0.2);
                    }
                }
                // Light Coverage Mode
                else if (_DebugWorldLightClusterMode == DEBUGWORLDLIGHTCLUSTERMODE_LIGHT_COVERAGE)
                {
                    if (cellIndex >= 0)
                    {
                        uint lightCount = GetCellLightCount(cellIndex);

                        if (lightCount > 0)
                        {
                            // Green for cells with lights
                            float intensity = saturate(float(lightCount) / max(_MaxLightsPerCellDisplay, 1));
                            result = float4(0, 0.5 + intensity * 0.5, 0, 0.3 + intensity * 0.2);
                        }
                        else
                        {
                            // Gray for empty cells
                            result = float4(0.2, 0.2, 0.2, 0.1);
                        }
                    }
                }
                // Cell Index Mode
                else if (_DebugWorldLightClusterMode == DEBUGWORLDLIGHTCLUSTERMODE_CELL_INDEX)
                {
                    if (cellIndex >= 0)
                    {
                        // Visualize cell index as color
                        float3 cellColor = CellIndexToColor(cellCoord);

                        // Add grid lines for clarity
                        float3 localPos = positionWS - _WorldLightGridMin;
                        float cellSize = _WorldLightGridCellSize.x;

                        if (IsOnCellBoundary(cellCoord, localPos, cellSize))
                        {
                            result = float4(1, 1, 1, 0.6);
                        }
                        else
                        {
                            result = float4(cellColor, 0.25);
                        }
                    }
                }

                return result;
            }

            ENDHLSL
        }
    }

    Fallback Off
}
