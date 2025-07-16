Shader "Hidden/Universal/DebugViewTiles"
{
    SubShader
    {
        Tags{ "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZWrite Off
            Cull Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            // Blend One One

            HLSLPROGRAM
            #pragma target 4.5
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch

            #pragma enable_d3d11_debug_symbols
            
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile USE_FPTL_LIGHTLIST USE_CLUSTERED_LIGHTLIST
            #pragma multi_compile SHOW_LIGHT_CATEGORIES SHOW_FEATURE_VARIANTS
            #pragma multi_compile _ IS_DRAWPROCEDURALINDIRECT
            #pragma multi_compile _ DISABLE_TILE_MODE

            #define USE_GPULIGHTS_CLUSTER

            //-------------------------------------------------------------------------------------
            // Include
            //-------------------------------------------------------------------------------------
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Debug.hlsl"
            // #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/DebuggingFullscreen.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/ClusterLighting/GPULights.cs.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Extension/GPUCulledLights.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/DebugViewEnums.cs.hlsl"

            //-------------------------------------------------------------------------------------
            // variable declaration
            //-------------------------------------------------------------------------------------

            uniform float4 _BlitScaleBias;
            uniform float4 _BlitScaleBiasRt;

            float _DebugTileClusterMode;
            int _ClusterDebugID;
            float _YFilp;
            int _DebugCategory;

            StructuredBuffer<uint> g_TileList;
            Buffer<uint> g_DispatchIndirectBuffer;

            StructuredBuffer<uint> g_CoarseLightList;
            // StructuredBuffer<uint> g_vLightListCluster;
            TEXTURE2D_X(_GBuffer0);

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
            
                // Reverting Y-coordinate due to post-processing.
                pos.y *= _YFilp > 0 ? -1 : 1;

                output.positionCS = pos;
                output.texcoord   = uv * _BlitScaleBias.xy + _BlitScaleBias.zw;
                return output;
            }

            float GetLinearDepth(float2 pixXY, float zDptBufSpace)    // 0 is near 1 is far
            {
                float4x4 g_mInvScrProjection = g_mInvScrProjectionArr;

            #ifdef USE_OBLIQUE_MODE
                float2 res2 = mul(g_mInvScrProjection, float4(pixXY, zDptBufSpace, 1.0)).zw;
                return res2.x / res2.y;
            #else
                // for perspective projection m22 is zero and m23 is +1/-1 (depends on left/right hand proj)
                // however this function must also work for orthographic projection so we keep it like this.
                float m22 = g_mInvScrProjection[2].z, m23 = g_mInvScrProjection[2].w;
                float m32 = g_mInvScrProjection[3].z, m33 = g_mInvScrProjection[3].w;

                return (m22*zDptBufSpace+m23) / (m32*zDptBufSpace+m33);
            #endif
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float4 outCol = 0;
                float2 uv = input.texcoord;
                uint2 pixelCoord = uint2(input.texcoord.xy * _ScreenParams.xy);
                float4 result = 0;

                int tileSize = _DebugTileClusterMode == 1 ? TILE_SIZE_BIG_TILE : TILE_SIZE_CLUSTERED;

                int2 tileCoord = (float2)pixelCoord / tileSize;

                int2 offsetInTile = pixelCoord - tileCoord * tileSize;

                uint num = 0;
                
                if (_DebugTileClusterMode == DEBUGTILECLUSTERMODE_COARSE_CULLING)
                {
                    // BigTileID
                    uint2 tileIDX = tileCoord;
                    uint iWidth = g_viDimensions.x;
                    uint iHeight = g_viDimensions.y;
                    uint nrBigTilesX = (iWidth+63)/64;
                    uint nrBigTilesY = (iHeight+63)/64;
                    
                    uint eyeIndex = 0;

                    int offs = tileIDX.y*nrBigTilesX + tileIDX.x;
                    num = g_CoarseLightList[MAX_NR_BIG_TILE_LIGHTS_PLUS_ONE * offs];


                    if (num > 0)
                    {
                        int maxIndex = -1;
                        for(int l0=(int) 0; l0<(int) num; l0 += 1)
                        {
                            int lightIndex = g_CoarseLightList[MAX_NR_BIG_TILE_LIGHTS_PLUS_ONE * offs + l0 + 1];
                            maxIndex = max(maxIndex, lightIndex);
                        }

        
                        result = OverlayHeatMap(pixelCoord, tileSize, num, 32, 0.1);
                    }
                
                }
                else
                {
                    // ClusterTileID
                    uint2 tileIDX = tileCoord;
                    uint iWidth = g_viDimensions.x;
                    uint iHeight = g_viDimensions.y;
                    uint nrClusterTilesX = _NumTileClusteredX;
                    uint nrClusterTilesY = _NumTileClusteredY;
                    
                    uint eyeIndex = 0;

                    int offs = tileIDX.y * nrClusterTilesX + tileIDX.x;
                    num = g_vLightListCluster[32 * offs];

                    float rawDepth = LoadSceneDepth(pixelCoord);
                #if UNITY_REVERSED_Z
                    rawDepth = 1.0 - rawDepth;
                #endif

                    float linearDepth  = LinearEyeDepth(rawDepth, _ZBufferParams);

                    linearDepth = GetLinearDepth(pixelCoord + float2(0.5,0.5), rawDepth);
                #if USE_LEFT_HAND_CAMERA_SPACE
                    // linearDepth = linearDepth;
                #else
                    linearDepth = -linearDepth;
                #endif

                    uint2 tileIndex    = tileIDX;
                    uint  clusterIndex = GetLightClusterIndex(tileIndex, linearDepth);

                    if (_DebugTileClusterMode == DEBUGTILECLUSTERMODE_CLUSTER_FOR_TILE)
                    {
                        clusterIndex = _ClusterDebugID;
                    }

                    uint lightCategory = _DebugCategory;
                    uint lightStart;
                    uint lightCount;
                    GetCountAndStartCluster(tileIndex, clusterIndex, lightCategory, lightStart, lightCount);

                    // Display max light index
                    uint v_lightListOffset = 0;
                    uint v_lightIdx = lightStart;
                    int maxIndex = 0;
                    while (v_lightListOffset < lightCount)
                    {
                        v_lightIdx = FetchIndex(lightStart, v_lightListOffset);
                        if (v_lightIdx == -1)
                            break;

                        maxIndex = max(maxIndex, v_lightIdx);
                        v_lightListOffset++;
                    }


                    if (lightCount > 0)
                    {   
                        result = OverlayHeatMap(pixelCoord, tileSize, lightCount, 32, 0.1);
                    }


                    // float3 positionWS = ComputeWorldSpacePosition(uv, LoadSceneDepth(pixelCoord), UNITY_MATRIX_I_VP);
                    // half4 gbuffer0 = SAMPLE_TEXTURE2D_X_LOD(_GBuffer0, sampler_PointClamp, uv, 0);

                    // half3 albedo = gbuffer0.xyz;
                    // uint v_lightListOffset = 0;
                    // uint v_lightIdx = lightStart;
                    // int maxIndex = 0;
                    // while (v_lightListOffset < lightCount)
                    // {
                    //     v_lightIdx = FetchIndex(lightStart, v_lightListOffset);
                    //     if (v_lightIdx == -1)
                    //         break;

                    //     GPULightData gpuLight = FetchLight(v_lightIdx);
                    //     {
                    //         float3 lightVector = gpuLight.lightPosWS - positionWS.xyz;
                    //         float distanceSqr = max(dot(lightVector, lightVector), HALF_MIN);

                    //         half3 lightDirection = half3(lightVector * rsqrt(distanceSqr));

                    //         // full-float precision required on some platforms
                    //         float attenuation = DistanceAttenuation(distanceSqr, gpuLight.lightAttenuation.xy);



                    //         float3 lightResult = albedo * gpuLight.lightColor * attenuation;
                    //         result += float4(lightResult, 1);
                    //     }

                    //     v_lightListOffset++;
                    // }

                }





                return result;
            }


            ENDHLSL
        }
    }

    Fallback Off
}
