Shader "PerObjectShadow/ShadowProjector"
{
    Properties
    {
        _ColorMask("Color Mask", Float) = 15
        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        _Cull("__cull", Float) = 2.0
    }
    SubShader
    {
        // Universal Pipeline tag is required. If Universal render pipeline is not set in the graphics settings
        // this Subshader will fail. One can add a subshader below or fallback to Standard built-in to make this
        // material work with both Universal Render Pipeline and Builtin Unity Pipeline
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }
        LOD 300

        // PerObjectShadowProjector
        Pass
        {
            Name "PerObjectShadowProjector"
            Tags
            {
                "LightMode" = "PerObjectShadowProjector"
            }

            // -------------------------------------
            // Render State Commands
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0

            // -------------------------------------
            // Shader Stages
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            // -------------------------------------
            // Material Keywords

            //--------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            // -------------------------------------
            // Universal Pipeline keywords

            // -------------------------------------
            // Unity defined keywords


            // -------------------------------------
            // Includes
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // Shadow Casting Light geometric parameters. These variables are used when applying the shadow Normal Bias and are set by UnityEngine.Rendering.Universal.ShadowUtils.SetupShadowCasterConstantBuffer in com.unity.render-pipelines.universal/Runtime/ShadowUtils.cs
            // For Directional lights, _LightDirection is used when applying shadow Normal Bias.
            // For Spot lights and Point lights, _LightPosition is used to compute the actual light direction because it is different at each shadow caster geometry vertex.
            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float2 texcoord     : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 GetShadowPositionHClip(Attributes input)
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                float3 lightDirectionWS = _LightDirection;

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

            // #if UNITY_REVERSED_Z
            //     positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            // #else
            //     positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            // #endif

                return positionCS;
            }

            Varyings ShadowPassVertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.positionCS = GetShadowPositionHClip(input);
                return output;
            }

            half4 ShadowPassFragment(Varyings input) : SV_TARGET
            {
                UNITY_SETUP_INSTANCE_ID(input);

                return 0;
            }

            ENDHLSL
        }

        // PerObjectScreenSpaceShadow
        Pass
        {
            Name "PerObjectScreenSpaceShadow"
            Tags
            {
                "LightMode" = "PerObjectScreenSpaceShadow"
            }

            // -------------------------------------
            // Render State Commands
            ZTest Greater
            ZWrite Off
            Cull Front

            Blend One Zero
            BlendOp Min

            ColorMask [_ColorMask]

            HLSLPROGRAM


            // -------------------------------------
            // Shader Stages
            #pragma vertex vert
            #pragma fragment frag

            //--------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            // -------------------------------------
            // Universal Pipeline keywords
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            // -------------------------------------
            // Includes
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "PerObjectShadows.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

            struct a2v
			{
				float3 vertex :POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct v2f
			{
				float4 positionHCS  :SV_POSITION;
                float3 positionWS   :TEXCOORD0;
                float4 positionSS   :TEXCOORD1;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			UNITY_INSTANCING_BUFFER_START(UnityPerMaterial)
				UNITY_DEFINE_INSTANCED_PROP(float4x4, _PerObjectWorldToShadow)
                UNITY_DEFINE_INSTANCED_PROP(float4, _PerObjectUVScaleOffset)
                UNITY_DEFINE_INSTANCED_PROP(float4, _PerObjectPCSSData)
			UNITY_INSTANCING_BUFFER_END(UnityPerMaterial)
            float4 _PerObjectShadowScaledScreenParams;
            int _CamHistoryFrameCount;

            v2f vert(a2v v)
			{
				v2f o;

				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_TRANSFER_INSTANCE_ID(v, o);


				o.positionHCS = TransformObjectToHClip(v.vertex);
                o.positionSS = ComputeScreenPos(o.positionHCS);
                o.positionWS = TransformObjectToWorld(v.vertex);

				return o;
			}

            half4 frag(v2f i):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float2 screenUV = i.positionHCS.xy * _PerObjectShadowScaledScreenParams.zw;
                TransformScreenUV(screenUV);


            #if UNITY_REVERSED_Z
                float depth = SampleSceneDepth(screenUV);
            #else
                float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(screenUV).x);
            #endif


                float3 positionWS = ComputeWorldSpacePosition(screenUV, depth, UNITY_MATRIX_I_VP);

                // Clip: ShadowFrustum HClip Sapce cull
                float3 positionSHCS = TransformWorldToObject(positionWS);
                positionSHCS = positionSHCS * float3(1.0, -1.0, 1.0);

                float clipValue = 1.0 - Max3(abs(positionSHCS).x, abs(positionSHCS).y, abs(positionSHCS).z);
                clip(clipValue);


                // Prepare Instance Values
                float4x4 worldToShadowMatrix = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _PerObjectWorldToShadow);
                float4 uvScaleOffset = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _PerObjectUVScaleOffset);
                float4 pcssData = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _PerObjectPCSSData);

                float radial2ShadowmapDepth = pcssData.x;
                float texelSizeWS           = pcssData.y;
                float farToNear             = pcssData.z;
                float shadowmapTileInvSize  = pcssData.w;
                float blockerInvTangent     = _PerCascadePCSSData[0].w;

                float4 shadowCoord = TransformWorldToPerObjectShadowCoord(positionWS, worldToShadowMatrix);

                float2 minCoord = uvScaleOffset.zw;
                float2 maxCoord = uvScaleOffset.xy + uvScaleOffset.zw;

                // float attenuation = PerObjectRealtimeShadow(shadowCoord);

                float2 noiseJitter = InterleavedGradientNoise(screenUV * _ScreenSize.xy, _CamHistoryFrameCount);
                noiseJitter *= TWO_PI;
                noiseJitter.x = sin(noiseJitter.x);
                noiseJitter.y = cos(noiseJitter.y);

                float penumbra = GetPerObjectShadowPenumbra();

                float filterSize = penumbra * 0.03 / texelSizeWS; // texel count
                filterSize = max(filterSize, 1.0);

                float attenuation = PerObjectShadowmapPCF(TEXTURE2D_ARGS(_PerObjectShadowmapTexture, sampler_LinearClampCompare), 
                                                        shadowCoord, shadowmapTileInvSize, minCoord, maxCoord, 16.0, filterSize, noiseJitter, 
                                                        texelSizeWS, farToNear, blockerInvTangent);


                return half4(attenuation.xxx, 1);
            }
            ENDHLSL
        }
    }
}
