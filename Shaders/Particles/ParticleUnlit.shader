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
        [HideInInspector] _VividParticleSharedData("Vivid Particle Shared Data", Vector) = (0, 0, 0, 0)
        [HideInInspector] _VividParticleSpanSharedData("Vivid Particle Span Shared Data", Vector) = (0, 0, 0, 0)
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
            #ifdef DOTS_INSTANCING_ON
                cbuffer UnityDOTSInstancing_BuiltinPropertyMetadata
                {
                    uint unity_DOTSInstancingF48_Metadataunity_ObjectToWorld;
                    uint unity_DOTSInstancingF48_Metadataunity_WorldToObject;
                    uint unity_DOTSInstancingF48_Metadataunity_MatrixPreviousM;
                    uint unity_DOTSInstancingF48_Metadataunity_MatrixPreviousMI;
                }

                #define unity_WorldTransformParams LoadDOTSInstancedData_WorldTransformParams()
                #define unity_RenderingLayer LoadDOTSInstancedData_RenderingLayer()
                #define UNITY_SETUP_DOTS_SH_COEFFS
                #define UNITY_SETUP_DOTS_RENDER_BOUNDS
            #endif

            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/AutoExposure.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _UnlitColor;
                float4 _UnlitColorMap_ST;
                float4 _BaseColor;
                float4 _VividParticleSharedData;
                float4 _VividParticleSpanSharedData;
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
                    UNITY_DOTS_INSTANCED_PROP(float4, _VividParticleSharedData)
                    UNITY_DOTS_INSTANCED_PROP(float4, _VividParticleSpanSharedData)
                    UNITY_DOTS_INSTANCED_PROP(float4, _VividParticlePositionSize)
                    UNITY_DOTS_INSTANCED_PROP(float4, _BaseColor)
                    UNITY_DOTS_INSTANCED_PROP(float4, _VividParticleRotation)
                    UNITY_DOTS_INSTANCED_PROP(float4, _VividParticleVelocityStretch)
                UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)

                #define VIVID_PARTICLE_SHARED_DATA_METADATA UNITY_DOTS_INSTANCED_METADATA_NAME(float4, _VividParticleSharedData)
                #define VIVID_PARTICLE_SPAN_SHARED_DATA_METADATA UNITY_DOTS_INSTANCED_METADATA_NAME(float4, _VividParticleSpanSharedData)
                #define VIVID_PARTICLE_POSITION_SIZE_METADATA UNITY_DOTS_INSTANCED_METADATA_NAME(float4, _VividParticlePositionSize)
                #define VIVID_PARTICLE_INSTANCE_COLOR_METADATA UNITY_DOTS_INSTANCED_METADATA_NAME(float4, _BaseColor)
                #define VIVID_PARTICLE_ROTATION_METADATA UNITY_DOTS_INSTANCED_METADATA_NAME(float4, _VividParticleRotation)
                #define VIVID_PARTICLE_VELOCITY_STRETCH_METADATA UNITY_DOTS_INSTANCED_METADATA_NAME(float4, _VividParticleVelocityStretch)
            #else
            #endif

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 particleSlot : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float particleVisible : TEXCOORD1;
                float4 instanceColor : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            struct VividParticleData
            {
                float4 positionSize;
                float4 instanceColor;
                float4 rotation;
                float4 velocityStretch;
                float visible;
                uint sharpIndex;
            };

            #ifdef UNITY_DOTS_INSTANCING_ENABLED
                static const uint VIVID_PARTICLE_PAGE_BASE_MASK = 0x00ffffffu;
                static const uint VIVID_PARTICLE_PAGE_COUNT_SHIFT = 24u;
                static const uint VIVID_PARTICLE_SHARED_DATA_STRIDE = 144u;

                float4 VividLoadParticleFloat4(uint metadata, uint particleIndex, float4 defaultValue)
                {
                    if (metadata == 0u)
                        return defaultValue;

                    uint address = metadata & kAddressMask;
                    if (IsDOTSInstancedProperty(metadata))
                        address += particleIndex * 16u;

                    return asfloat(DOTSInstanceData_Load4(address));
                }

                uint4 VividLoadParticleUint4(uint metadata, uint dataIndex, uint4 defaultValue)
                {
                    if (metadata == 0u)
                        return defaultValue;

                    uint address = metadata & kAddressMask;
                    if (IsDOTSInstancedProperty(metadata))
                        address += dataIndex * 16u;

                    return DOTSInstanceData_Load4(address);
                }

                float4 VividLoadParticleSharedFloat4(uint metadata, uint sharpIndex, uint elementIndex, float4 defaultValue)
                {
                    if (metadata == 0u)
                        return defaultValue;

                    uint address = (metadata & kAddressMask)
                        + sharpIndex * VIVID_PARTICLE_SHARED_DATA_STRIDE
                        + elementIndex * 16u;
                    return asfloat(DOTSInstanceData_Load4(address));
                }

                uint VividResolveParticleIndex(Attributes input, out float visible, out uint sharpIndex)
                {
                    uint instanceIndex = GetDOTSInstanceIndex();
                    sharpIndex = 0u;
                    if (_VividParticleRenderMode < 3.5)
                    {
                        uint slot = (uint)(input.particleSlot.x + 0.5);
                        uint4 spanData = VividLoadParticleUint4(
                            VIVID_PARTICLE_SPAN_SHARED_DATA_METADATA,
                            instanceIndex,
                            uint4(
                                0u,
                                instanceIndex & VIVID_PARTICLE_PAGE_BASE_MASK,
                                instanceIndex >> VIVID_PARTICLE_PAGE_COUNT_SHIFT,
                                0u));
                        sharpIndex = spanData.x;
                        visible = slot <= spanData.z ? 1.0 : 0.0;
                        return spanData.y + min(slot, spanData.z);
                    }

                    visible = 1.0;
                    return instanceIndex;
                }

                VividParticleData VividLoadParticleData(Attributes input)
                {
                    VividParticleData data;
                    uint particleIndex = VividResolveParticleIndex(input, data.visible, data.sharpIndex);
                    float4 sharedVisibility = VividLoadParticleSharedFloat4(
                        VIVID_PARTICLE_SHARED_DATA_METADATA,
                        data.sharpIndex,
                        8u,
                        float4(0.0, 0.0, 0.0, 1.0));
                    data.positionSize = VividLoadParticleFloat4(VIVID_PARTICLE_POSITION_SIZE_METADATA, particleIndex, float4(0.0, 0.0, 0.0, 1.0));
                    data.instanceColor = VividLoadParticleFloat4(VIVID_PARTICLE_INSTANCE_COLOR_METADATA, particleIndex, float4(1.0, 1.0, 1.0, 1.0));
                    data.rotation = VividLoadParticleFloat4(VIVID_PARTICLE_ROTATION_METADATA, particleIndex, float4(0.0, 0.0, 0.0, 1.0));
                    data.velocityStretch = VividLoadParticleFloat4(VIVID_PARTICLE_VELOCITY_STRETCH_METADATA, particleIndex, float4(0.0, 1.0, 0.0, 1.0));
                    data.visible *= sharedVisibility.w >= 0.0 ? 1.0 : 0.0;
                    return data;
                }
            #else
                VividParticleData VividLoadParticleData(Attributes input)
                {
                    VividParticleData data;
                    data.positionSize = _VividParticlePositionSize;
                    data.instanceColor = _BaseColor;
                    data.rotation = _VividParticleRotation;
                    data.velocityStretch = _VividParticleVelocityStretch;
                    data.visible = 1.0;
                    data.sharpIndex = 0u;
                    return data;
                }
            #endif

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

                VividParticleData particle = VividLoadParticleData(input);
                output.particleVisible = particle.visible;
                output.instanceColor = particle.instanceColor;
                output.uv = TransformParticleUnlitUV(input.uv);
                if (particle.visible <= 0.0)
                {
                    output.positionCS = float4(0.0, 0.0, 0.0, 1.0);
                    return output;
                }

                float4 positionSize = particle.positionSize;
                float4 rotation = particle.rotation;
                float4 velocityStretch = particle.velocityStretch;
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
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                clip(input.particleVisible - 0.5);
                float4 color = SampleParticleUnlitColor(input.uv) * input.instanceColor;
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
