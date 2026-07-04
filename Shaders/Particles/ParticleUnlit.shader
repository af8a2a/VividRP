Shader "VividRP/Particles/Unlit"
{
    Properties
    {
        [Main(SurfaceOptions, _, on, off)] _SurfaceOptions("Surface Options", Float) = 1
        [SubEnum(SurfaceOptions, Opaque, 0, Transparent, 1)] _SurfaceType("Surface Type", Float) = 1.0
        [SubEnum(SurfaceOptions, Alpha, 0, Premultiply, 1, Additive, 2, Multiply, 3)] _BlendMode("Blend Mode", Float) = 0.0
        [SubToggle(SurfaceOptions, _)] _AlphaCutoffEnable("Alpha Clipping", Float) = 0.0
        [Sub(SurfaceOptions)] _AlphaCutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        [SubEnum(SurfaceOptions, Back, 2, Front, 1, Off, 0)] _CullMode("Cull", Float) = 0.0
        [SubToggle(SurfaceOptions, _)] _TransparentZWrite("Transparent ZWrite", Float) = 0.0
        [Sub(SurfaceOptions)] _QueueOffset("Queue Offset", Float) = 0.0
        [HideInInspector] _TransparentSortPriority("Transparent Sort Priority", Float) = 0.0
        [HideInInspector] _DoubleSidedEnable("Double Sided Enable", Float) = 1.0

        [Main(SurfaceInputs, _, on, off)] _SurfaceInputs("Surface Inputs", Float) = 1
        [MainTexture] [Tex(SurfaceInputs, _UnlitColor)] _UnlitColorMap("Color Map", 2D) = "white" {}
        [HideInInspector] [MainColor] _UnlitColor("Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _AlphaRemapMin("Alpha Remap Min", Float) = 0.0
        [HideInInspector] _AlphaRemapMax("Alpha Remap Max", Float) = 1.0

        [HideInInspector] _SrcBlend("__src", Float) = 5.0
        [HideInInspector] _DstBlend("__dst", Float) = 10.0
        [HideInInspector] _AlphaSrcBlend("__alphaSrc", Float) = 1.0
        [HideInInspector] _AlphaDstBlend("__alphaDst", Float) = 10.0
        [HideInInspector] _ZWrite("__zw", Float) = 0.0

        [HideInInspector] _MainTex("Color Map", 2D) = "white" {}
        [HideInInspector] _Color("Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        [HideInInspector] _BaseMap("Base Map", 2D) = "white" {}
        [HideInInspector] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _VividParticleRenderMode("Vivid Particle Render Mode", Float) = 0.0
        [HideInInspector] _VividParticlePositionSize("Vivid Particle Position Size", Vector) = (0, 0, 0, 1)
        [HideInInspector] _VividParticleRotation("Vivid Particle Rotation", Vector) = (0, 0, 0, 1)
        [HideInInspector] _VividParticleVelocityStretch("Vivid Particle Velocity Stretch", Vector) = (0, 1, 0, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "VividRenderPipeline"
        }

        HLSLINCLUDE
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/AutoExposure.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _UnlitColor;
                float4 _UnlitColorMap_ST;
                float4 _BaseColor;
                float4 _VividParticlePositionSize;
                float4 _VividParticleRotation;
                float4 _VividParticleVelocityStretch;
                float _AlphaCutoff;
                float _AlphaRemapMin;
                float _AlphaRemapMax;
                float _VividParticleRenderMode;
            CBUFFER_END

            TEXTURE2D(_UnlitColorMap);
            SAMPLER(sampler_UnlitColorMap);

            #ifdef UNITY_DOTS_INSTANCING_ENABLED
                UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                    UNITY_DOTS_INSTANCED_PROP(float4, _VividParticlePositionSize)
                    UNITY_DOTS_INSTANCED_PROP(float4, _BaseColor)
                    UNITY_DOTS_INSTANCED_PROP(float4, _VividParticleRotation)
                    UNITY_DOTS_INSTANCED_PROP(float4, _VividParticleVelocityStretch)
                UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)

                #define VIVID_PARTICLE_POSITION_SIZE UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_CUSTOM_DEFAULT(float4, _VividParticlePositionSize, float4(0.0, 0.0, 0.0, 1.0))
                #define VIVID_PARTICLE_INSTANCE_COLOR UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_CUSTOM_DEFAULT(float4, _BaseColor, float4(1.0, 1.0, 1.0, 1.0))
                #define VIVID_PARTICLE_ROTATION UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_CUSTOM_DEFAULT(float4, _VividParticleRotation, float4(0.0, 0.0, 0.0, 1.0))
                #define VIVID_PARTICLE_VELOCITY_STRETCH UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_CUSTOM_DEFAULT(float4, _VividParticleVelocityStretch, float4(0.0, 1.0, 0.0, 1.0))
            #else
                #define VIVID_PARTICLE_POSITION_SIZE _VividParticlePositionSize
                #define VIVID_PARTICLE_INSTANCE_COLOR _BaseColor
                #define VIVID_PARTICLE_ROTATION _VividParticleRotation
                #define VIVID_PARTICLE_VELOCITY_STRETCH _VividParticleVelocityStretch
            #endif

            struct Attributes
            {
                float3 positionOS : POSITION;
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

            float2 TransformParticleUnlitUV(float2 uv)
            {
                return uv * _UnlitColorMap_ST.xy + _UnlitColorMap_ST.zw;
            }

            float4 SampleParticleUnlitColor(float2 uv)
            {
                float4 colorSample = SAMPLE_TEXTURE2D(_UnlitColorMap, sampler_UnlitColorMap, uv);
                float alpha = lerp(_AlphaRemapMin, _AlphaRemapMax, colorSample.a) * _UnlitColor.a;
                return float4(colorSample.rgb * _UnlitColor.rgb, alpha);
            }

            void ApplyParticleUnlitAlphaClip(float alpha)
            {
            #if defined(_ALPHATEST_ON)
                clip(alpha - _AlphaCutoff);
            #endif
            }

            float3 RotateParticleVector(float3 value, float4 rotation)
            {
                float rotationLengthSq = dot(rotation, rotation);
                if (rotationLengthSq <= 0.000001)
                    return value;

                rotation *= rsqrt(rotationLengthSq);
                float3 t = 2.0 * cross(rotation.xyz, value);
                return value + rotation.w * t + cross(rotation.xyz, t);
            }

            float3 SafeNormalizeParticleVector(float3 value, float3 fallback)
            {
                float lengthSq = dot(value, value);
                return lengthSq > 0.000001 ? value * rsqrt(lengthSq) : fallback;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float4 positionSize = VIVID_PARTICLE_POSITION_SIZE;
                float4 rotation = VIVID_PARTICLE_ROTATION;
                float4 velocityStretch = VIVID_PARTICLE_VELOCITY_STRETCH;
                float3 centerRWS = positionSize.xyz;
                float size = max(positionSize.w, 0.0001);
                float4x4 viewToWorld = GetViewToWorldMatrix();
                float3 viewRight = normalize(float3(viewToWorld._m00, viewToWorld._m10, viewToWorld._m20));
                float3 viewUp = normalize(float3(viewToWorld._m01, viewToWorld._m11, viewToWorld._m21));
                float3 viewForward = -normalize(float3(viewToWorld._m02, viewToWorld._m12, viewToWorld._m22));
                float3 meshLocal = input.positionOS * size;
                float3 positionRWS;

                if (_VividParticleRenderMode < 0.5)
                {
                    positionRWS = centerRWS
                        + viewRight * meshLocal.x
                        + viewUp * meshLocal.y;
                }
                else if (_VividParticleRenderMode < 1.5)
                {
                    float3 stretchUp = SafeNormalizeParticleVector(velocityStretch.xyz, viewUp);
                    float3 stretchRight = cross(viewForward, stretchUp);
                    stretchRight = SafeNormalizeParticleVector(stretchRight, viewRight);
                    float stretchLength = max(velocityStretch.w, size);
                    positionRWS = centerRWS
                        + stretchRight * meshLocal.x
                        + stretchUp * (input.positionOS.y * stretchLength);
                }
                else if (_VividParticleRenderMode < 2.5)
                {
                    positionRWS = centerRWS
                        + float3(1.0, 0.0, 0.0) * meshLocal.x
                        + float3(0.0, 0.0, 1.0) * meshLocal.y;
                }
                else if (_VividParticleRenderMode < 3.5)
                {
                    float3 cameraPositionRWS = float3(viewToWorld._m03, viewToWorld._m13, viewToWorld._m23);
                    float3 toCamera = cameraPositionRWS - centerRWS;
                    toCamera.y = 0.0;
                    float toCameraLength = length(toCamera);
                    float3 verticalNormal = toCameraLength > 0.0001 ? toCamera / toCameraLength : -float3(viewToWorld._m02, 0.0, viewToWorld._m22);
                    float3 verticalRight = cross(float3(0.0, 1.0, 0.0), verticalNormal);
                    verticalRight = length(verticalRight) > 0.0001 ? normalize(verticalRight) : viewRight;
                    positionRWS = centerRWS
                        + verticalRight * meshLocal.x
                        + float3(0.0, 1.0, 0.0) * meshLocal.y;
                }
                else
                {
                    positionRWS = centerRWS + RotateParticleVector(meshLocal, rotation);
                }

                output.positionCS = TransformWorldToHClip(positionRWS);
                output.uv = TransformParticleUnlitUV(input.uv);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float4 color = SampleParticleUnlitColor(input.uv) * VIVID_PARTICLE_INSTANCE_COLOR;
                ApplyParticleUnlitAlphaClip(color.a);
                return float4((color.rgb), color.a);
            }
        ENDHLSL

        Pass
        {
            Name "VividForward"
            Tags { "LightMode" = "VividForward" }

            Blend [_SrcBlend] [_DstBlend], [_AlphaSrcBlend] [_AlphaDstBlend]
            ZWrite [_ZWrite]
            ZTest LEqual
            Cull [_CullMode]

            HLSLPROGRAM
                #pragma target 4.5
                #pragma multi_compile_instancing
                #pragma multi_compile _ DOTS_INSTANCING_ON
                #pragma shader_feature_local_fragment _ALPHATEST_ON
                #pragma vertex Vert
                #pragma fragment Frag
            ENDHLSL
        }

        Pass
        {
            Name "SRPDefaultUnlit"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend [_SrcBlend] [_DstBlend], [_AlphaSrcBlend] [_AlphaDstBlend]
            ZWrite [_ZWrite]
            ZTest LEqual
            Cull [_CullMode]

            HLSLPROGRAM
                #pragma target 4.5
                #pragma multi_compile_instancing
                #pragma multi_compile _ DOTS_INSTANCING_ON
                #pragma shader_feature_local_fragment _ALPHATEST_ON
                #pragma vertex Vert
                #pragma fragment Frag
            ENDHLSL
        }
    }

    CustomEditor "VividRP.Editor.ParticleUnlitShaderGUI"

    FallBack Off
}
